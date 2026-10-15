using System;
using System.Collections.Generic;
using GenesisValley.Core;
using GenesisValley.World;

namespace GenesisValley.Simulation
{
    public enum TreeSpecies
    {
        Common = 0,
        Pine = 1,
        Twisted = 2,
    }

    /// <summary>Harvestable tree resource node (GDD §11.3). Decorative grass is NOT an entity.</summary>
    public sealed class TreeEntity
    {
        public EntityId Id;
        public TreeSpecies Species;
        public int Variant;
        public float X, Z;
        public float RotationDegrees;
        public float Scale = 1f;
        public Inventory Stock;
        /// <summary>0..1; saplings (&lt;1) cannot be chopped. Wood is created when the tree matures.</summary>
        public float Growth = 1f;
        public long FelledTick = -1;
        /// <summary>Forest-brush transaction that planted this tree (0 = generated / natural).</summary>
        public int PlantedByTransaction;
        /// <summary>Villager currently holding the chop reservation (only one at a time).</summary>
        public EntityId ReservedBy;
        public int PathFailures;
        public long PlanCooldownUntilTick;

        public bool IsSapling => Growth < 1f;
        public bool IsStump => !IsSapling && Stock.IsEmpty;
        public bool Harvestable => !IsSapling && !Stock.IsEmpty;
    }

    public enum StorageKind
    {
        Camp = 0,
        Warehouse = 1,
    }

    public sealed class StorageEntity
    {
        public EntityId Id;
        public StorageKind Kind;
        public EntityId BuildingId;
        public float X, Z;
        public Inventory Inventory;
    }

    /// <summary>Loose items on the ground (e.g. refunded materials of a cancelled site). Haulers move them to storage.</summary>
    public sealed class ResourcePile
    {
        public EntityId Id;
        public float X, Z;
        public Inventory Inventory;
        public EntityId ReservedBy;
    }

    public enum BuildingKind
    {
        House = 0,
        Storehouse = 1,
        Field = 2,
        FishingHut = 3,
        Bridge = 4,
    }

    public enum BuildingState
    {
        Site = 0,
        Complete = 1,
    }

    public enum ConstructionStage
    {
        Foundation = 0,
        Walls = 1,
        Roof = 2,
        Done = 3,
    }

    public enum CropState
    {
        Fallow = 0,
        Growing = 1,
        Ripe = 2,
    }

    /// <summary>
    /// A planned or finished structure (GDD §13). Site → foundation → walls → roof → complete.
    /// Materials are delivered into <see cref="Site"/> and consumed at the start of each stage, so cancelling
    /// can only refund what was never consumed.
    /// </summary>
    public sealed class BuildingEntity
    {
        public EntityId Id;
        public BuildingKind Kind;
        public int Variant;
        public float X, Z;
        /// <summary>Facing in degrees (0 = +Z). The door / front is on the facing side.</summary>
        public float Yaw;
        /// <summary>Footprint width (local X) and depth (local Z) in metres.</summary>
        public float SizeX, SizeZ;
        public float PadHeight;
        public BuildingState State;
        public int WoodRequired;
        public int WoodConsumed;
        public float WorkRequired;
        public float WorkDone;
        public Inventory Site;
        public long CreatedTick;
        public long CompletedTick = -1;
        public EntityId StorageId;
        public readonly List<EntityId> Residents = new List<EntityId>();
        public int Beds;

        // Field
        public CropState Crop;
        public float CropGrowth;
        public int CropFood;
        public int Harvests;

        // Fishing hut: shore interaction point
        public float SpotX, SpotZ;

        // Bridge: deck end points + heights
        public float AX, AZ, BX, BZ, AY, BY;

        /// <summary>Not saved: last reason the site could not progress (shown in the inspector).</summary>
        public string WaitingReason;

        public bool IsComplete => State == BuildingState.Complete;
        public float Progress => WorkRequired <= 0 ? 1f : Math.Min(1f, WorkDone / WorkRequired);

        public ConstructionStage Stage
        {
            get
            {
                if (IsComplete) return ConstructionStage.Done;
                float p = Progress;
                return p < StageStart(ConstructionStage.Walls) ? ConstructionStage.Foundation
                    : p < StageStart(ConstructionStage.Roof) ? ConstructionStage.Walls : ConstructionStage.Roof;
            }
        }

        public static float StageStart(ConstructionStage s) => s == ConstructionStage.Foundation ? 0f : s == ConstructionStage.Walls ? 0.3f : s == ConstructionStage.Roof ? 0.8f : 1f;

        /// <summary>Wood that must be consumed by the end of a stage (30% / 80% / 100%).</summary>
        public int WoodNeededThrough(ConstructionStage s)
        {
            if (s == ConstructionStage.Foundation) return (int)Math.Round(WoodRequired * 0.3);
            if (s == ConstructionStage.Walls) return (int)Math.Round(WoodRequired * 0.8);
            return WoodRequired;
        }

        /// <summary>Wood still missing on site (not yet delivered nor consumed).</summary>
        public int WoodShortfall => Math.Max(0, WoodRequired - WoodConsumed - (Site != null ? Site.Get(ItemType.Wood) : 0));

        public float FacingX => (float)Math.Sin(Yaw * Math.PI / 180.0);
        public float FacingZ => (float)Math.Cos(Yaw * Math.PI / 180.0);

        /// <summary>World point just outside the front (door / work spot).</summary>
        public void DoorPoint(out float x, out float z)
        {
            if (Kind == BuildingKind.Bridge) { x = AX; z = AZ; return; }
            if (Kind == BuildingKind.Field) { x = X; z = Z; return; }
            float d = SizeZ * 0.5f + 1.4f;
            x = X + FacingX * d; z = Z + FacingZ * d;
        }

        /// <summary>Point-in-footprint test (oriented rectangle) with optional margin.</summary>
        public bool Contains(float wx, float wz, float margin)
        {
            ToLocal(wx, wz, out float lx, out float lz);
            return Math.Abs(lx) <= SizeX * 0.5f + margin && Math.Abs(lz) <= SizeZ * 0.5f + margin;
        }

        /// <summary>Signed-ish distance outside the footprint (0 inside).</summary>
        public float DistanceOutside(float wx, float wz)
        {
            ToLocal(wx, wz, out float lx, out float lz);
            float ox = Math.Max(0, Math.Abs(lx) - SizeX * 0.5f), oz = Math.Max(0, Math.Abs(lz) - SizeZ * 0.5f);
            return (float)Math.Sqrt(ox * ox + oz * oz);
        }

        public void ToLocal(float wx, float wz, out float lx, out float lz)
        {
            float dx = wx - X, dz = wz - Z;
            float fx = FacingX, fz = FacingZ;
            // local +Z = facing, local +X = right of facing
            lz = dx * fx + dz * fz;
            lx = dx * fz - dz * fx;
        }

        public void ToWorld(float lx, float lz, out float wx, out float wz)
        {
            float fx = FacingX, fz = FacingZ;
            wx = X + lz * fx + lx * fz;
            wz = Z + lz * fz - lx * fx;
        }

        public float BoundingRadius => 0.5f * (float)Math.Sqrt(SizeX * SizeX + SizeZ * SizeZ);

        public bool BlocksNavigation => Kind != BuildingKind.Field && Kind != BuildingKind.Bridge;
    }

    public enum VillagerState
    {
        Idle = 0,
        Walking = 1,
        Working = 2,
        Sleeping = 3,
    }

    /// <summary>What the villager is doing (drives animation + inspector text).</summary>
    public enum VillagerAction
    {
        None = 0,
        Chop = 1,
        Build = 2,
        Sow = 3,
        Harvest = 4,
        Fish = 5,
        DrawWater = 6,
        PickUp = 7,
        DropOff = 8,
        Eat = 9,
        Drink = 10,
        Sleep = 11,
        Sit = 12,
        Wander = 13,
        Wait = 14,
    }

    /// <summary>Why the villager is walking.</summary>
    public enum WalkPurpose
    {
        None = 0,
        Task = 1,
        DropOff = 2,
        PickUp = 3,
        Food = 4,
        Water = 5,
        Bed = 6,
        Social = 7,
        Wander = 8,
        Arrive = 9,
    }

    /// <summary>Work preference (GDD §6: a preference, never a permanent lock).</summary>
    public enum JobRole
    {
        Laborer = 0,
        Woodcutter = 1,
        Builder = 2,
        Farmer = 3,
        Fisher = 4,
        WaterCarrier = 5,
    }

    public struct PersonalEvent
    {
        public long Tick;
        public string Text;
    }

    public sealed class Villager
    {
        public EntityId Id;
        public string Name;
        public int Gender;          // 0 = male, 1 = female (appearance only)
        public int AppearanceSeed;  // outfit/hair/skin variation
        public float X, Z;
        public float FacingDegrees;

        // Needs (GDD §6)
        public float Hunger;      // food units owed; eats at >= 1
        public float Thirst;      // water units owed; drinks at >= 1
        public float Energy = 1f; // 1 rested .. 0 exhausted
        public float Health = 1f;
        public bool Weak;

        // Personality
        public float Diligence = 0.5f, Curiosity = 0.5f, Sociability = 0.5f;
        public JobRole Role;

        public Inventory Carry;
        public VillagerState State;
        public VillagerAction Action;
        public WalkPurpose Purpose;
        public EntityId TaskId;
        /// <summary>Entity the current step is about (tree, building, storage, pile).</summary>
        public EntityId TargetId;
        /// <summary>Where the carried items go.</summary>
        public EntityId DropId;
        public int ActionTicksLeft;
        public int IdleTicksLeft;
        public float GoalX, GoalZ;
        public EntityId HomeId;
        public EntityId CampId;
        public long TaskStartedTick;
        public long ArrivedTick;
        public float WorkedSecondsToday;
        public readonly List<PersonalEvent> Recent = new List<PersonalEvent>();

        // Transient (not saved): rebuilt after load.
        public readonly List<GridCell> Path = new List<GridCell>();
        public int PathIndex;
        public bool NeedsRepath;
        public int LastCellIndex = -1;

        public bool IsMoving => State == VillagerState.Walking;
        /// <summary>True while sleeping inside the house (presentation hides the model).</summary>
        public bool InsideHome;

        public void Remember(long tick, string text)
        {
            Recent.Add(new PersonalEvent { Tick = tick, Text = text });
            if (Recent.Count > 8) Recent.RemoveAt(0);
        }
    }
}
