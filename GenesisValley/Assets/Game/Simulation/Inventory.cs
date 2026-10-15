using System;
using GenesisValley.Core;

namespace GenesisValley.Simulation
{
    public enum ItemType
    {
        Food = 0,
        Wood = 1,
        Water = 2,
    }

    public sealed class Inventory
    {
        public const int ItemTypeCount = 3;
        public readonly EntityId Owner;
        public int Capacity;
        private readonly int[] _amounts = new int[ItemTypeCount];

        public Inventory(EntityId owner, int capacity) { Owner = owner; Capacity = capacity; }

        public int Get(ItemType t) => _amounts[(int)t];
        public int Total { get { int s = 0; for (int i = 0; i < ItemTypeCount; i++) s += _amounts[i]; return s; } }
        public int Free => Math.Max(0, Capacity - Total);
        public bool IsEmpty => Total == 0;

        /// <summary>Only InventorySystem and persistence may write amounts directly.</summary>
        internal void SetRaw(ItemType t, int amount) => _amounts[(int)t] = amount;
    }

    /// <summary>
    /// The only place where items move (GDD §6/§10). Transfers are atomic: either the whole computed amount moves or nothing.
    /// Items never appear from nowhere; only <see cref="Consume"/> removes them (eating), and it records the removal.
    /// </summary>
    public static class InventorySystem
    {
        public static int Transfer(Inventory from, Inventory to, ItemType type, int requested)
        {
            if (from == null || to == null || ReferenceEquals(from, to) || requested <= 0) return 0;
            int moved = Math.Min(requested, Math.Min(from.Get(type), to.Free));
            if (moved <= 0) return 0;
            from.SetRaw(type, from.Get(type) - moved);
            to.SetRaw(type, to.Get(type) + moved);
            return moved;
        }

        public static bool Consume(Inventory from, ItemType type, int amount, WorldStats stats)
        {
            if (amount <= 0 || from.Get(type) < amount) return false;
            from.SetRaw(type, from.Get(type) - amount);
            stats.RecordConsumed(type, amount);
            return true;
        }

        /// <summary>Initial world seeding (generator/persistence only). Counted as created so conservation can be audited.</summary>
        public static void Seed(Inventory inv, ItemType type, int amount, WorldStats stats)
        {
            if (amount < 0 || inv.Free < amount) throw new InvalidOperationException("Seed exceeds capacity");
            inv.SetRaw(type, inv.Get(type) + amount);
            stats.RecordCreated(type, amount);
        }
    }

    [Flags]
    public enum Milestones
    {
        None = 0,
        FirstWoodDelivered = 1 << 0,
        FirstHouse = 1 << 1,
        FirstBridge = 1 << 2,
        FirstHarvest = 1 << 3,
        FirstFoodSurplus = 1 << 4,
        FirstPopulationGrowth = 1 << 5,
        FirstRiver = 1 << 6,
        FirstForest = 1 << 7,
        FirstStorehouse = 1 << 8,
        FirstFish = 1 << 9,
        AllHoused = 1 << 10,
    }

    public sealed class WorldStats
    {
        public readonly long[] Created = new long[Inventory.ItemTypeCount];
        public readonly long[] Consumed = new long[Inventory.ItemTypeCount];
        public long WoodDelivered;
        public int TreesFelled;
        public int FoodHarvested;
        public int FishCaught;
        public int WaterFetched;
        public int BuildingsCompleted;
        public int Arrivals;
        public int PathFailures;
        public int Relocations;
        public Milestones Reached;

        public void RecordCreated(ItemType t, int n) => Created[(int)t] += n;
        public void RecordConsumed(ItemType t, int n) => Consumed[(int)t] += n;
    }
}
