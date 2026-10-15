using System;
using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.World;

namespace GenesisValley.Simulation
{
    /// <summary>Authoritative world data (GDD §10). No engine types, no scene references.</summary>
    public sealed class WorldState
    {
        public readonly SimConfig Config;
        public readonly ulong Seed;
        public string WorldId;
        public string WorldName = "创世纪谷";
        public long Tick;
        /// <summary>Tick offset so tick 0 starts at <see cref="SimConfig.StartHour"/>.</summary>
        public long TickOffset;

        // Long-lived system state that must survive save/load for deterministic continuation.
        public readonly long[] PlannerRetryTick = new long[8];
        public long LastNoWaterHintTick = -100000;
        public int EcologyLastDecayDay = -1;
        public long EcologyNextSeedTick;
        public readonly IdAllocator Ids = new IdAllocator();
        public readonly DeterministicRandom Rng;
        public readonly HeightField Height;
        public readonly PassabilityGrid Nav;
        public readonly Pathfinder Paths;
        public readonly WaterMap Water;
        /// <summary>Transient walking-cost field from the camp (planner ranking).</summary>
        public readonly TravelMap Travel;
        public readonly List<TreeEntity> Trees = new List<TreeEntity>();
        public readonly List<StorageEntity> Storages = new List<StorageEntity>();
        public readonly List<Villager> Villagers = new List<Villager>();
        public readonly List<BuildingEntity> Buildings = new List<BuildingEntity>();
        public readonly List<ResourcePile> Piles = new List<ResourcePile>();
        public readonly List<RiverData> Rivers = new List<RiverData>();
        public readonly TaskBoard Tasks = new TaskBoard();
        public readonly EventLog Events = new EventLog();
        public readonly WorldStats Stats = new WorldStats();
        public readonly UndoHistory Undo = new UndoHistory();
        public long LastAuthoritativeSequence;
        public long LastFoodShortageTick = -100000;
        public long LastWaterShortageTick = -100000;
        public int LastImmigrationDay;
        public int PathBudget;

        /// <summary>Accumulated dirty height-sample rect for presentation; consumers clear it after syncing.</summary>
        public IntRect PresentationHeightDirty = IntRect.Empty;
        /// <summary>Set after load/generation so presentation rebuilds every view.</summary>
        public bool PresentationFullRefresh = true;
        /// <summary>Bumped when rivers change (presentation rebuilds water meshes).</summary>
        public int RiverVersion;

        private readonly Dictionary<EntityId, TreeEntity> _treeById = new Dictionary<EntityId, TreeEntity>();
        private readonly Dictionary<EntityId, Villager> _villagerById = new Dictionary<EntityId, Villager>();
        private readonly Dictionary<EntityId, BuildingEntity> _buildingById = new Dictionary<EntityId, BuildingEntity>();

        public WorldState(SimConfig config, ulong seed)
        {
            Config = config;
            Seed = seed;
            WorldId = "valley-" + seed.ToString("x");
            Rng = new DeterministicRandom(seed);
            Height = new HeightField(config.HeightResolution, config.WorldSize, config.VerticalRange);
            Nav = new PassabilityGrid(config.WorldSize, config.NavCellSize, config.MaxWalkableSlopeDegrees, config.NavBorderCells)
            {
                RoadWearForFullRoad = config.RoadWearForFullRoad,
                RoadSpeedBonus = config.RoadSpeedBonus,
            };
            Paths = new Pathfinder(Nav) { NodeBudget = config.PathNodeBudget };
            Water = new WaterMap(Nav) { FalloffMeters = config.WetnessFalloffMeters };
            Travel = new TravelMap(Nav);
            TickOffset = config.SecondsToTicks(config.GameHoursToSeconds(config.StartHour));
        }

        // ------------------------------------------------------------------ time

        public float SimSeconds => Tick * Config.TickSeconds;
        private float ClockSeconds => (Tick + TickOffset) * Config.TickSeconds;
        public float DayFraction => (ClockSeconds % Config.SecondsPerGameDay) / Config.SecondsPerGameDay;
        public float Hour => DayFraction * 24f;
        public int Day => (int)(ClockSeconds / Config.SecondsPerGameDay) + 1;
        public bool IsNight => Hour >= Config.SleepHour || Hour < Config.WakeHour;
        public bool IsEvening => Hour >= Config.EveningSocialHour && Hour < Config.SleepHour;
        public string ClockText { get { float h = Hour; int hh = (int)h; int mm = (int)((h - hh) * 60); return hh.ToString("00") + ":" + mm.ToString("00"); } }

        // ------------------------------------------------------------------ registry

        public StorageEntity Camp => Storages.Count > 0 ? Storages[0] : null;

        public void AddTree(TreeEntity t) { Trees.Add(t); _treeById[t.Id] = t; }
        public void RemoveTree(TreeEntity t) { Trees.Remove(t); _treeById.Remove(t.Id); }
        public void AddVillager(Villager v) { Villagers.Add(v); _villagerById[v.Id] = v; }
        public void AddStorage(StorageEntity s) { Storages.Add(s); }
        public void AddBuilding(BuildingEntity b) { Buildings.Add(b); _buildingById[b.Id] = b; }
        public void RemoveBuilding(BuildingEntity b) { Buildings.Remove(b); _buildingById.Remove(b.Id); }

        public TreeEntity FindTree(EntityId id) => id.IsValid && _treeById.TryGetValue(id, out var t) ? t : null;
        public Villager FindVillager(EntityId id) => id.IsValid && _villagerById.TryGetValue(id, out var v) ? v : null;
        public BuildingEntity FindBuilding(EntityId id) => id.IsValid && _buildingById.TryGetValue(id, out var b) ? b : null;

        public StorageEntity FindStorage(EntityId id)
        {
            if (!id.IsValid) return null;
            for (int i = 0; i < Storages.Count; i++) if (Storages[i].Id == id) return Storages[i];
            return null;
        }

        public ResourcePile FindPile(EntityId id)
        {
            if (!id.IsValid) return null;
            for (int i = 0; i < Piles.Count; i++) if (Piles[i].Id == id) return Piles[i];
            return null;
        }

        public RiverData FindRiver(int id)
        {
            foreach (var r in Rivers) if (r.Id == id) return r;
            return null;
        }

        /// <summary>Any inventory that can receive/hand out items, by id (storage, site, pile).</summary>
        public Inventory FindInventory(EntityId id)
        {
            var s = FindStorage(id); if (s != null) return s.Inventory;
            var b = FindBuilding(id); if (b != null) return b.Site;
            var p = FindPile(id); if (p != null) return p.Inventory;
            return null;
        }

        public bool TryGetPosition(EntityId id, out float x, out float z)
        {
            var s = FindStorage(id); if (s != null) { x = s.X; z = s.Z; return true; }
            var b = FindBuilding(id); if (b != null) { b.DoorPoint(out x, out z); return true; }
            var p = FindPile(id); if (p != null) { x = p.X; z = p.Z; return true; }
            var t = FindTree(id); if (t != null) { x = t.X; z = t.Z; return true; }
            var v = FindVillager(id); if (v != null) { x = v.X; z = v.Z; return true; }
            x = z = 0; return false;
        }

        // ------------------------------------------------------------------ events

        public void Emit(SimEventType type, EntityId actor, EntityId target, float x, float z, string message)
            => Emit(type, DefaultImportance(type), actor, target, x, z, message);

        public void Emit(SimEventType type, EventImportance importance, EntityId actor, EntityId target, float x, float z, string message)
        {
            Events.Add(new SimEvent { Tick = Tick, Type = type, Importance = importance, Actor = actor, Target = target, X = x, Z = z, Message = message });
            if (importance >= EventImportance.Story)
            {
                var v = FindVillager(actor);
                v?.Remember(Tick, message);
            }
        }

        public void ReachMilestone(Milestones m, EntityId actor, EntityId target, float x, float z, string message)
        {
            if ((Stats.Reached & m) != 0) return;
            Stats.Reached |= m;
            Emit(SimEventType.Milestone, EventImportance.Milestone, actor, target, x, z, message);
        }

        private static EventImportance DefaultImportance(SimEventType t)
        {
            switch (t)
            {
                case SimEventType.Milestone: return EventImportance.Milestone;
                case SimEventType.FoodShortage:
                case SimEventType.WaterShortage:
                case SimEventType.VillagerWeak:
                case SimEventType.CommandRejected:
                case SimEventType.UndoRejected:
                case SimEventType.SiteCancelled:
                    return EventImportance.Warning;
                case SimEventType.BuildingCompleted:
                case SimEventType.TreeFelled:
                case SimEventType.FieldHarvested:
                case SimEventType.VillagersArrived:
                case SimEventType.VillagerMovedIn:
                case SimEventType.SitePlanned:
                case SimEventType.RiverCreated:
                case SimEventType.ForestPlanted:
                case SimEventType.BridgePlanned:
                case SimEventType.RegionConnected:
                case SimEventType.VillagerRecovered:
                case SimEventType.FieldSown:
                case SimEventType.Hint:
                    return EventImportance.Story;
                case SimEventType.TaskPublished:
                case SimEventType.VillagerAte:
                case SimEventType.VillagerSlept:
                case SimEventType.VillagerWoke:
                    return EventImportance.Debug;
                default:
                    return EventImportance.Info;
            }
        }

        // ------------------------------------------------------------------ aggregates

        public int TotalItems(ItemType type)
        {
            long n = 0;
            foreach (var t in Trees) n += t.Stock.Get(type);
            foreach (var s in Storages) n += s.Inventory.Get(type);
            foreach (var v in Villagers) n += v.Carry.Get(type);
            foreach (var b in Buildings) if (b.Site != null) n += b.Site.Get(type);
            foreach (var p in Piles) n += p.Inventory.Get(type);
            return (int)n;
        }

        public int Stored(ItemType type)
        {
            int n = 0;
            foreach (var s in Storages) n += s.Inventory.Get(type);
            return n;
        }

        public int StorageCapacity { get { int n = 0; foreach (var s in Storages) n += s.Inventory.Capacity; return n; } }
        public int StorageUsed { get { int n = 0; foreach (var s in Storages) n += s.Inventory.Total; return n; } }

        public int Population => Villagers.Count;

        public int Beds
        {
            get { int n = 0; foreach (var b in Buildings) if (b.IsComplete && b.Kind == BuildingKind.House) n += b.Beds; return n; }
        }

        public int CountBuildings(BuildingKind k, bool includeSites)
        {
            int n = 0;
            foreach (var b in Buildings) if (b.Kind == k && (includeSites || b.IsComplete)) n++;
            return n;
        }

        public string NameOf(EntityId id)
        {
            var v = FindVillager(id);
            return v != null ? v.Name : id.ToString();
        }

        public static string KindName(BuildingKind k)
        {
            switch (k)
            {
                case BuildingKind.House: return "住宅";
                case BuildingKind.Storehouse: return "谷仓";
                case BuildingKind.Field: return "农田";
                case BuildingKind.FishingHut: return "渔点";
                case BuildingKind.Bridge: return "木桥";
                default: return k.ToString();
            }
        }

        /// <summary>Food days left at current population (planner + HUD).</summary>
        public float FoodDays => Population == 0 ? 99f : Stored(ItemType.Food) / Math.Max(0.01f, Population * Config.FoodPerAdultPerDay);
        public float WaterDays => Population == 0 ? 99f : Stored(ItemType.Water) / Math.Max(0.01f, Population * Config.WaterPerAdultPerDay);
    }
}
