using System;
using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.World;

namespace GenesisValley.Simulation
{
    /// <summary>
    /// Settlement-level decisions every 5 s (GDD §6). Priority: food and water first, then housing, then storage
    /// and bridges. It only publishes tasks and plans sites — it never adds resources or spawns finished buildings.
    /// Same-type projects have caps and cooldowns so a housing gap does not create 20 houses at once.
    /// </summary>
    public sealed class VillagePlanner
    {
        private readonly List<TreeEntity> _candidates = new List<TreeEntity>();

        public void Evaluate(WorldState w)
        {
            var camp = w.Camp;
            if (camp == null) return;
            w.Tasks.Prune();
            w.Travel.Ensure(camp.X, camp.Z);
            int campRegion = w.Nav.RegionNear(camp.X, camp.Z, 4);

            PlanFood(w, campRegion);
            PlanWater(w, campRegion);
            PlanHousing(w);
            PlanStorage(w);
            PlanSites(w, campRegion);
            PlanWoodBuffer(w, campRegion);
            PlanPiles(w);
            AssignRoles(w);
        }

        // ------------------------------------------------------------------ food

        private void PlanFood(WorldState w, int campRegion)
        {
            var c = w.Config;
            int pop = w.Population;
            if (pop == 0) return;
            float demand = pop * c.FoodPerAdultPerDay;
            int fishers = w.CountBuildings(BuildingKind.FishingHut, true);
            float supply = fishers * 5f;
            foreach (var b in w.Buildings) if (b.Kind == BuildingKind.Field) supply += EstimateFieldPerDay(w, b);
            int fieldSites = CountSites(w, BuildingKind.Field);
            int maxFieldSites = w.FoodDays < 1.5f ? 2 : 1;
            if (supply < demand * 1.3f && fieldSites < maxFieldSites) TryPlace(w, BuildingKind.Field);

            if (fishers == 0 && pop >= 8 && w.CountBuildings(BuildingKind.Field, true) >= 1 && CountSites(w, BuildingKind.FishingHut) == 0)
                TryPlace(w, BuildingKind.FishingHut);

            foreach (var b in w.Buildings)
            {
                if (!b.IsComplete) continue;
                if (b.Kind == BuildingKind.Field)
                {
                    if (b.Crop == CropState.Fallow && !w.Tasks.HasActiveTaskFor(b.Id, TaskType.Sow))
                        w.Tasks.Publish(w, TaskType.Sow, b.Id, EntityId.None);
                    else if (b.Crop == CropState.Ripe && b.CropFood > 0 && !w.Tasks.HasActiveTaskFor(b.Id, TaskType.Harvest))
                        w.Tasks.Publish(w, TaskType.Harvest, b.Id, EntityId.None, 0, 2);
                }
                else if (b.Kind == BuildingKind.FishingHut)
                {
                    bool wanted = w.FoodDays < c.FoodReserveDays * 1.6f && w.StorageUsed < w.StorageCapacity - 4;
                    if (wanted && !w.Tasks.HasActiveTaskFor(b.Id, TaskType.Fish) && w.Water.DistanceToWater(b.SpotX, b.SpotZ) < 5f)
                        w.Tasks.Publish(w, TaskType.Fish, b.Id, EntityId.None);
                }
            }

            if (w.FoodDays >= 4f && pop >= 5)
                w.ReachMilestone(Milestones.FirstFoodSurplus, EntityId.None, w.Camp.Id, w.Camp.X, w.Camp.Z, "村庄首次出现粮食盈余（够吃4天以上）");
        }

        /// <summary>Food per game day a field realistically yields (growth time depends on wetness, plus farm work).</summary>
        public static float EstimateFieldPerDay(WorldState w, BuildingEntity b)
        {
            var c = w.Config;
            float wet = w.Water.Wetness(b.X, b.Z);
            float days = c.FieldGrowDays / (0.6f + 0.6f * wet) + 0.12f;
            return ConstructionSystem.FieldYield(w, b) / days;
        }

        // ------------------------------------------------------------------ water

        private void PlanWater(WorldState w, int campRegion)
        {
            var c = w.Config;
            var camp = w.Camp;
            int target = (int)Math.Ceiling(w.Population * c.WaterPerAdultPerDay * c.WaterReserveDays);
            int projected = w.Stored(ItemType.Water);
            foreach (var t in w.Tasks.Tasks) if (t.IsActive && t.Type == TaskType.FetchWater) projected += c.CarryCapacity;
            if (projected >= target) return;
            if (!w.Water.NearestShore(camp.X, camp.Z, campRegion, out _, out float dist))
            {
                if (w.Tick - w.LastNoWaterHintTick > w.Config.SecondsToTicks(150))
                {
                    w.LastNoWaterHintTick = w.Tick;
                    w.Emit(SimEventType.Hint, EntityId.None, camp.Id, camp.X, camp.Z, "营地附近没有可达的水源。让水来到这片土地：用河流工具画一条河");
                }
                return;
            }
            if (w.StorageUsed >= w.StorageCapacity - c.CarryCapacity) return;
            int open = w.Tasks.CountActive(TaskType.FetchWater);
            int maxOpen = Math.Max(2, w.Population / 5);
            for (int i = open; i < maxOpen && projected < target; i++)
            {
                w.Tasks.Publish(w, TaskType.FetchWater, EntityId.None, NearestStorageTo(w, camp.X, camp.Z).Id, c.CarryCapacity);
                projected += c.CarryCapacity;
            }
        }

        // ------------------------------------------------------------------ housing / storage

        private void PlanHousing(WorldState w)
        {
            var c = w.Config;
            int bedsPlanned = 0;
            foreach (var b in w.Buildings) if (b.Kind == BuildingKind.House) bedsPlanned += c.HouseBeds;
            int homeless = w.Population - bedsPlanned;
            int built = w.CountBuildings(BuildingKind.House, false);
            if (homeless <= 0)
            {
                // Growth: a well-fed village with spare wood builds ahead so newcomers can settle (GDD §7 population).
                bool prosperous = w.FoodDays > 3f && w.WaterDays > 1f && w.Stored(ItemType.Wood) >= c.HouseWood;
                if (!prosperous || bedsPlanned - w.Population >= 2 || bedsPlanned >= c.PopulationCap) return;
                if (CountSites(w, BuildingKind.House) > 0 || CountSites(w, BuildingKind.Field) > 0) return;
                TryPlace(w, BuildingKind.House);
                return;
            }
            int maxSites = built == 0 ? 1 : c.MaxConcurrentHouseSites;
            if (w.FoodDays < 0.8f) maxSites = 1; // food first
            if (CountSites(w, BuildingKind.House) >= maxSites) return;
            TryPlace(w, BuildingKind.House);
        }

        private void PlanStorage(WorldState w)
        {
            if (w.StorageCapacity == 0) return;
            float fill = w.StorageUsed / (float)w.StorageCapacity;
            if (fill < 0.72f || CountSites(w, BuildingKind.Storehouse) > 0) return;
            if (w.CountBuildings(BuildingKind.House, false) == 0) return; // first house before the barn
            TryPlace(w, BuildingKind.Storehouse);
        }

        private void TryPlace(WorldState w, BuildingKind kind)
        {
            if (w.Tick < w.PlannerRetryTick[(int)kind]) return;
            var cand = SitePlanner.FindSite(w, kind);
            if (!cand.Valid)
            {
                w.PlannerRetryTick[(int)kind] = w.Tick + w.Config.SecondsToTicks(30f);
                return;
            }
            w.PlannerRetryTick[(int)kind] = w.Tick + w.Config.SecondsToTicks(8f);
            SitePlanner.PlaceSite(w, kind, cand);
        }

        private static int CountSites(WorldState w, BuildingKind k)
        {
            int n = 0;
            foreach (var b in w.Buildings) if (b.Kind == k && !b.IsComplete) n++;
            return n;
        }

        // ------------------------------------------------------------------ construction logistics

        private static int SiteOrder(BuildingKind k)
        {
            switch (k)
            {
                case BuildingKind.Field: return 0;
                case BuildingKind.FishingHut: return 1;
                case BuildingKind.House: return 2;
                case BuildingKind.Bridge: return 3;
                default: return 4;
            }
        }

        private readonly List<BuildingEntity> _sites = new List<BuildingEntity>();

        private void PlanSites(WorldState w, int campRegion)
        {
            var c = w.Config;
            _sites.Clear();
            foreach (var b in w.Buildings) if (!b.IsComplete) _sites.Add(b);
            _sites.Sort((a, b) =>
            {
                int o = SiteOrder(a.Kind).CompareTo(SiteOrder(b.Kind));
                return o != 0 ? o : a.CreatedTick.CompareTo(b.CreatedTick);
            });

            int storedWood = w.Stored(ItemType.Wood);
            int reservedWood = 0;
            foreach (var t in w.Tasks.Tasks) if (t.IsActive && t.Type == TaskType.Supply) reservedWood += t.Amount;
            int freeWood = storedWood - reservedWood;
            int siteChops = 0;
            foreach (var t in w.Tasks.Tasks) if (t.IsActive && t.Type == TaskType.ChopTree && w.FindBuilding(t.DestId) != null) siteChops++;

            // Wood arrived in storage while a site still waits on an unclaimed chop task: switch to hauling from storage.
            if (freeWood > 0)
            {
                foreach (var t in w.Tasks.Tasks)
                {
                    if (freeWood <= 0) break;
                    if (t.Type != TaskType.ChopTree || t.State != TaskState.Open || w.FindBuilding(t.DestId) == null) continue;
                    t.State = TaskState.Done;
                    siteChops--;
                }
            }

            foreach (var b in _sites)
            {
                int region = w.Nav.RegionNear(b.Kind == BuildingKind.Bridge ? b.AX : b.X, b.Kind == BuildingKind.Bridge ? b.AZ : b.Z, 4);
                int shortfall = b.WoodShortfall - w.Tasks.IncomingTo(b.Id);
                while (shortfall > 0 && freeWood > 0)
                {
                    int amt = Math.Min(c.CarryCapacity, Math.Min(shortfall, freeWood));
                    w.Tasks.Publish(w, TaskType.Supply, b.Id, b.Id, amt, 1);
                    shortfall -= amt; freeWood -= amt;
                }
                while (shortfall > 0 && siteChops < 5)
                {
                    var tree = NearestFreeTree(w, b.X, b.Z, region);
                    if (tree == null) break;
                    int amt = Math.Min(shortfall, tree.Stock.Get(ItemType.Wood));
                    w.Tasks.Publish(w, TaskType.ChopTree, tree.Id, b.Id, amt, 1, 1f);
                    shortfall -= amt; siteChops++;
                }
                if (b.WoodShortfall > 0 && !ConstructionSystem.CanWork(b) && b.WaitingReason == null)
                    b.WaitingReason = "等待木材 " + (b.WoodConsumed + b.Site.Get(ItemType.Wood)) + "/" + b.WoodNeededThrough(b.Stage);

                if (ConstructionSystem.CanWork(b) && !w.Tasks.HasActiveTaskFor(b.Id, TaskType.Build))
                {
                    int max = b.Kind == BuildingKind.Field || b.Kind == BuildingKind.Bridge || b.Kind == BuildingKind.FishingHut ? 2 : c.MaxBuildersPerSite;
                    w.Tasks.Publish(w, TaskType.Build, b.Id, EntityId.None, 0, max);
                }
            }
        }

        private TreeEntity NearestFreeTree(WorldState w, float x, float z, int region)
        {
            TreeEntity best = null; float bestD = float.MaxValue;
            foreach (var tree in w.Trees)
            {
                if (!tree.Harvestable || tree.ReservedBy.IsValid || tree.PlanCooldownUntilTick > w.Tick) continue;
                float d = w.Travel.CostTo(tree.X, tree.Z) + SitePlanner.Dist(tree.X, tree.Z, x, z) * 0.5f;
                if (d >= bestD || float.IsInfinity(d)) continue;
                if (w.Tasks.HasActiveTaskFor(tree.Id, TaskType.ChopTree)) continue;
                if (w.Nav.RegionNear(tree.X, tree.Z, 3) != region) continue;
                bestD = d; best = tree;
            }
            return best;
        }

        // ------------------------------------------------------------------ wood buffer

        private void PlanWoodBuffer(WorldState w, int campRegion)
        {
            var camp = w.Camp;
            var c = w.Config;
            int incoming = 0, active = 0;
            foreach (var t in w.Tasks.Tasks)
            {
                if (t.Type != TaskType.ChopTree || !t.IsActive) continue;
                if (w.FindBuilding(t.DestId) != null) continue;
                active++;
                incoming += t.Amount;
            }
            int outstanding = 0;
            foreach (var b in w.Buildings) if (!b.IsComplete) outstanding += Math.Max(0, b.WoodShortfall - w.Tasks.IncomingTo(b.Id));
            int target = c.WoodBufferTarget + outstanding;
            int projected = w.Stored(ItemType.Wood) + incoming;
            int room = w.StorageCapacity - w.StorageUsed - incoming;
            int quota = c.MaxOpenChopTasks - active;
            if (quota <= 0 || projected >= target || room < c.WoodPerTree) return;

            _candidates.Clear();
            foreach (var tree in w.Trees)
            {
                if (!tree.Harvestable || tree.ReservedBy.IsValid || tree.PlanCooldownUntilTick > w.Tick) continue;
                if (w.Tasks.HasActiveTaskFor(tree.Id, TaskType.ChopTree)) continue;
                _candidates.Add(tree);
            }
            _candidates.Sort((a, b) =>
            {
                float da = w.Travel.CostTo(a.X, a.Z);
                float db = w.Travel.CostTo(b.X, b.Z);
                int cmp = da.CompareTo(db);
                return cmp != 0 ? cmp : a.Id.CompareTo(b.Id);
            });

            for (int i = 0; i < _candidates.Count && quota > 0 && projected < target && room >= c.WoodPerTree; i++)
            {
                var tree = _candidates[i];
                if (float.IsInfinity(w.Travel.CostTo(tree.X, tree.Z))) break;
                var dest = NearestStorageTo(w, tree.X, tree.Z);
                int amt = tree.Stock.Get(ItemType.Wood);
                var task = w.Tasks.Publish(w, TaskType.ChopTree, tree.Id, dest.Id, amt);
                projected += amt; room -= amt; quota--;
                w.Emit(SimEventType.TaskPublished, EntityId.None, tree.Id, tree.X, tree.Z, "规划：砍伐树木补充木材（任务 " + task.Id + "）");
            }
        }

        private static StorageEntity NearestStorageTo(WorldState w, float x, float z)
        {
            StorageEntity best = w.Camp; float bestD = float.MaxValue;
            foreach (var s in w.Storages)
            {
                float d = (s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z);
                if (d < bestD && s.Inventory.Free > 0) { bestD = d; best = s; }
            }
            return best;
        }

        private void PlanPiles(WorldState w)
        {
            foreach (var p in w.Piles)
                if (!p.Inventory.IsEmpty && !w.Tasks.HasActiveTaskFor(p.Id, TaskType.HaulPile))
                    w.Tasks.Publish(w, TaskType.HaulPile, p.Id, EntityId.None);
            w.Piles.RemoveAll(p => p.Inventory.IsEmpty && !p.ReservedBy.IsValid);
        }

        // ------------------------------------------------------------------ roles

        private readonly int[] _desired = new int[6];
        private readonly int[] _have = new int[6];

        /// <summary>Keeps a stable spread of job preferences matching current needs (with hysteresis: only surplus roles move).</summary>
        private void AssignRoles(WorldState w)
        {
            int pop = w.Population;
            if (pop == 0) return;
            Array.Clear(_desired, 0, 6); Array.Clear(_have, 0, 6);
            int fields = w.CountBuildings(BuildingKind.Field, false);
            int huts = w.CountBuildings(BuildingKind.FishingHut, false);
            int sites = 0; foreach (var b in w.Buildings) if (!b.IsComplete) sites++;
            _desired[(int)JobRole.Farmer] = Math.Min(Math.Max(0, (fields + 1) / 2), pop / 3);
            _desired[(int)JobRole.Fisher] = Math.Min(huts, pop / 6);
            _desired[(int)JobRole.Builder] = sites == 0 ? 0 : Math.Min(pop / 3, 1 + sites);
            _desired[(int)JobRole.WaterCarrier] = pop >= 6 ? Math.Max(1, pop / 10) : 0;
            int rest = pop - _desired[3] - _desired[4] - _desired[2] - _desired[5];
            _desired[(int)JobRole.Woodcutter] = Math.Max(0, (int)Math.Ceiling(rest * 0.6f));
            _desired[(int)JobRole.Laborer] = Math.Max(0, pop - _desired[1] - _desired[2] - _desired[3] - _desired[4] - _desired[5]);
            foreach (var v in w.Villagers) _have[(int)v.Role]++;

            for (int role = 1; role < 6; role++)
            {
                while (_have[role] < _desired[role])
                {
                    Villager pick = null;
                    foreach (var v in w.Villagers)
                    {
                        int r = (int)v.Role;
                        if (r == role || _have[r] <= _desired[r]) continue;
                        if (pick == null || Fit(v, (JobRole)role) > Fit(pick, (JobRole)role)) pick = v;
                    }
                    if (pick == null) break;
                    _have[(int)pick.Role]--;
                    pick.Role = (JobRole)role;
                    _have[role]++;
                }
            }
            for (int role = 1; role < 6; role++)
            {
                while (_have[role] > _desired[role] + 1)
                {
                    Villager pick = null;
                    foreach (var v in w.Villagers) if ((int)v.Role == role) { pick = v; break; }
                    if (pick == null) break;
                    pick.Role = JobRole.Laborer; _have[role]--; _have[0]++;
                }
            }
        }

        private static float Fit(Villager v, JobRole role)
        {
            switch (role)
            {
                case JobRole.Builder: return v.Diligence;
                case JobRole.Farmer: return 1f - v.Curiosity;
                case JobRole.Fisher: return v.Curiosity * 0.5f + (1f - v.Sociability) * 0.5f;
                case JobRole.WaterCarrier: return v.Sociability;
                default: return v.Diligence * 0.5f + v.Curiosity * 0.5f;
            }
        }
    }
}
