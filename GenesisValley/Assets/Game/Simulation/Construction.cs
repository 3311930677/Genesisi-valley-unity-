using System;
using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.World;

namespace GenesisValley.Simulation
{
    public enum BuildResult
    {
        Progressed = 0,
        WaitingMaterials = 1,
        Completed = 2,
    }

    /// <summary>
    /// Construction process (GDD §13): site → reserve materials → deliver → foundation → walls → roof → acceptance →
    /// register beds / storage / farm. Materials are consumed at the start of each stage, never before.
    /// </summary>
    public static class ConstructionSystem
    {
        public static bool CanWork(BuildingEntity b)
        {
            if (b.IsComplete) return false;
            int need = b.WoodNeededThrough(b.Stage) - b.WoodConsumed;
            return need <= 0 || b.Site.Get(ItemType.Wood) >= need;
        }

        public static BuildResult AddWork(WorldState w, BuildingEntity b, float seconds, Villager worker)
        {
            if (b.IsComplete) return BuildResult.Completed;
            var stage = b.Stage;
            int need = b.WoodNeededThrough(stage) - b.WoodConsumed;
            if (need > 0)
            {
                if (b.Site.Get(ItemType.Wood) < need)
                {
                    b.WaitingReason = "等待木材 " + (b.WoodConsumed + b.Site.Get(ItemType.Wood)) + "/" + b.WoodNeededThrough(stage);
                    return BuildResult.WaitingMaterials;
                }
                InventorySystem.Consume(b.Site, ItemType.Wood, need, w.Stats);
                b.WoodConsumed += need;
                b.Site.Capacity = Math.Max(0, b.WoodRequired - b.WoodConsumed);
                if (b.Kind != BuildingKind.Field || stage != ConstructionStage.Foundation)
                    w.Emit(SimEventType.SiteStageStarted, EventImportance.Info, worker != null ? worker.Id : EntityId.None, b.Id, b.X, b.Z,
                        WorldState.KindName(b.Kind) + "工地开始" + StageName(b.Kind, stage));
            }
            b.WaitingReason = null;
            b.WorkDone = Math.Min(b.WorkRequired, b.WorkDone + seconds);
            if (b.WorkDone >= b.WorkRequired - 1e-3f)
            {
                // Make sure every stage's wood has been consumed before acceptance.
                int rest = b.WoodRequired - b.WoodConsumed;
                if (rest > 0)
                {
                    if (b.Site.Get(ItemType.Wood) < rest)
                    {
                        b.WorkDone = b.WorkRequired * 0.999f;
                        b.WaitingReason = "等待木材 " + (b.WoodConsumed + b.Site.Get(ItemType.Wood)) + "/" + b.WoodRequired;
                        return BuildResult.WaitingMaterials;
                    }
                    InventorySystem.Consume(b.Site, ItemType.Wood, rest, w.Stats);
                    b.WoodConsumed += rest;
                }
                Complete(w, b, worker);
                return BuildResult.Completed;
            }
            return BuildResult.Progressed;
        }

        public static string StageName(BuildingKind k, ConstructionStage s)
        {
            if (k == BuildingKind.Field) return s == ConstructionStage.Foundation ? "清理土地" : s == ConstructionStage.Walls ? "翻土开垄" : "围栏";
            if (k == BuildingKind.Bridge) return s == ConstructionStage.Foundation ? "打桩" : s == ConstructionStage.Walls ? "铺设桥面" : "安装栏杆";
            return s == ConstructionStage.Foundation ? "打地基" : s == ConstructionStage.Walls ? "砌墙" : s == ConstructionStage.Roof ? "盖屋顶" : "完工";
        }

        public static void Complete(WorldState w, BuildingEntity b, Villager worker)
        {
            b.State = BuildingState.Complete;
            b.CompletedTick = w.Tick;
            b.WorkDone = b.WorkRequired;
            b.WaitingReason = null;
            w.Stats.BuildingsCompleted++;
            w.Tasks.CancelFor(w, b.Id);
            // Leftover delivered wood (should be rare) becomes a pile so it is never lost or duplicated.
            if (b.Site != null && !b.Site.IsEmpty)
            {
                b.DoorPoint(out float px, out float pz);
                var pile = new ResourcePile { Id = w.Ids.Next(), X = px, Z = pz };
                pile.Inventory = new Inventory(pile.Id, 64);
                for (int i = 0; i < Inventory.ItemTypeCount; i++) InventorySystem.Transfer(b.Site, pile.Inventory, (ItemType)i, b.Site.Get((ItemType)i));
                w.Piles.Add(pile);
            }
            string who = worker != null ? worker.Name : "村民们";
            EntityId actor = worker != null ? worker.Id : EntityId.None;
            switch (b.Kind)
            {
                case BuildingKind.House:
                {
                    b.Beds = w.Config.HouseBeds;
                    w.Emit(SimEventType.BuildingCompleted, actor, b.Id, b.X, b.Z, who + " 和伙伴们建成了一栋住宅");
                    w.ReachMilestone(Milestones.FirstHouse, actor, b.Id, b.X, b.Z, who + " 完成了村庄的第一栋房屋");
                    AssignHomes(w);
                    break;
                }
                case BuildingKind.Storehouse:
                {
                    b.DoorPoint(out float dx, out float dz);
                    var s = new StorageEntity { Id = w.Ids.Next(), Kind = StorageKind.Warehouse, BuildingId = b.Id, X = b.X, Z = b.Z };
                    s.Inventory = new Inventory(s.Id, w.Config.StorehouseCapacity);
                    // Storage interaction happens at the door.
                    s.X = dx; s.Z = dz;
                    w.AddStorage(s);
                    b.StorageId = s.Id;
                    w.Emit(SimEventType.BuildingCompleted, actor, b.Id, b.X, b.Z, who + " 建成了谷仓，村庄储量增加了 " + w.Config.StorehouseCapacity);
                    w.ReachMilestone(Milestones.FirstStorehouse, actor, b.Id, b.X, b.Z, "村庄有了第一座谷仓");
                    break;
                }
                case BuildingKind.Field:
                    b.Crop = CropState.Fallow;
                    w.Emit(SimEventType.BuildingCompleted, actor, b.Id, b.X, b.Z, who + " 开垦了一片新农田");
                    break;
                case BuildingKind.FishingHut:
                    w.Emit(SimEventType.BuildingCompleted, actor, b.Id, b.X, b.Z, who + " 在河边搭起了渔点");
                    break;
                case BuildingKind.Bridge:
                {
                    int ra = w.Nav.RegionNear(b.AX, b.AZ, 3), rb = w.Nav.RegionNear(b.BX, b.BZ, 3);
                    SitePlanner.ApplyBridgeDeck(w, b, true);
                    w.Emit(SimEventType.BuildingCompleted, actor, b.Id, b.X, b.Z, who + " 建成了一座木桥");
                    if (ra != rb)
                        w.Emit(SimEventType.RegionConnected, actor, b.Id, b.X, b.Z, "新桥连通了河两岸，对岸的树木和土地现在可以到达了");
                    else
                        w.Emit(SimEventType.RegionConnected, EventImportance.Info, actor, b.Id, b.X, b.Z, "新桥缩短了过河的路");
                    w.ReachMilestone(Milestones.FirstBridge, actor, b.Id, b.X, b.Z, who + " 完成了村庄的第一座桥");
                    break;
                }
            }
        }

        /// <summary>Moves homeless villagers into free beds (oldest residents first, deterministic).</summary>
        public static void AssignHomes(WorldState w)
        {
            foreach (var house in w.Buildings)
            {
                if (house.Kind != BuildingKind.House || !house.IsComplete) continue;
                house.Residents.RemoveAll(id => w.FindVillager(id) == null);
                foreach (var v in w.Villagers)
                {
                    if (house.Residents.Count >= house.Beds) break;
                    if (v.HomeId.IsValid && w.FindBuilding(v.HomeId) != null) continue;
                    v.HomeId = house.Id;
                    house.Residents.Add(v.Id);
                    w.Emit(SimEventType.VillagerMovedIn, v.Id, house.Id, house.X, house.Z, v.Name + " 搬进了新家");
                }
            }
            bool allHoused = w.Villagers.Count > 0;
            foreach (var v in w.Villagers) if (!v.HomeId.IsValid) { allHoused = false; break; }
            if (allHoused) w.ReachMilestone(Milestones.AllHoused, EntityId.None, EntityId.None, 0, 0, "所有居民都住进了房子");
        }

        /// <summary>
        /// Cancels an unfinished site (e.g. a river now runs through it). Unconsumed materials are refunded as a pile;
        /// consumed materials are gone for good — nothing is duplicated (GDD §13/§15).
        /// </summary>
        public static void CancelSite(WorldState w, BuildingEntity b, string reason)
        {
            if (b.IsComplete) return;
            w.Tasks.CancelFor(w, b.Id);
            foreach (var v in w.Villagers) if (v.DropId == b.Id) v.DropId = EntityId.None;
            if (b.Site != null && !b.Site.IsEmpty)
            {
                var pile = new ResourcePile { Id = w.Ids.Next(), X = b.X, Z = b.Z };
                b.DoorPoint(out pile.X, out pile.Z);
                pile.Inventory = new Inventory(pile.Id, 64);
                for (int i = 0; i < Inventory.ItemTypeCount; i++) InventorySystem.Transfer(b.Site, pile.Inventory, (ItemType)i, b.Site.Get((ItemType)i));
                w.Piles.Add(pile);
            }
            SitePlanner.ApplyFootprint(w, b, false);
            w.RemoveBuilding(b);
            w.Emit(SimEventType.SiteCancelled, EntityId.None, b.Id, b.X, b.Z, WorldState.KindName(b.Kind) + "工地已取消：" + reason + "（未用木材已退回）");
        }

        /// <summary>Crop growth for completed fields (passive; wetter ground grows faster and yields more).</summary>
        public static void TickFields(WorldState w, float seconds)
        {
            var c = w.Config;
            foreach (var b in w.Buildings)
            {
                if (b.Kind != BuildingKind.Field || !b.IsComplete || b.Crop != CropState.Growing) continue;
                float wet = w.Water.Wetness(b.X, b.Z);
                float rate = (0.6f + 0.6f * wet) / (c.FieldGrowDays * c.SecondsPerGameDay);
                b.CropGrowth = Math.Min(1f, b.CropGrowth + rate * seconds);
                if (b.CropGrowth >= 1f)
                {
                    b.Crop = CropState.Ripe;
                    b.CropFood = FieldYield(w, b);
                }
            }
        }

        public static int FieldYield(WorldState w, BuildingEntity b)
        {
            float wet = w.Water.Wetness(b.X, b.Z);
            float fert = WaterMap.Fertility(w.Height, b.X, b.Z, w.Config.MaxWalkableSlopeDegrees);
            float y = w.Config.FieldHarvestFood * (0.6f + 0.4f * fert) * (0.7f + 0.5f * wet);
            return Math.Max(4, (int)Math.Round(y));
        }
    }
}
