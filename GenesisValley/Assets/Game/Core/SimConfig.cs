using System.Globalization;
using System.Text;

namespace GenesisValley.Core
{
    public enum Difficulty
    {
        Easy = 0,
        Standard = 1,
    }

    /// <summary>
    /// Initial tuning values from GDD §4/§5/§11/§12/§13. Plain C# so the simulation can run without the engine.
    /// These are starting proposals that must be measured in playtests, not a balanced final set.
    /// </summary>
    public sealed class SimConfig
    {
        public Difficulty Difficulty = Difficulty.Easy;

        // Time (GDD §5)
        public int TicksPerSecond = 10;
        public float SecondsPerGameDay = 360f;
        public float StartHour = 8f;
        public float SleepHour = 21.5f;
        public float WakeHour = 5.5f;
        public float EveningSocialHour = 19f;

        // World (GDD §11.1, §12)
        public float WorldSize = 500f;
        public int HeightResolution = 513;
        public float VerticalRange = 100f;
        public float NavCellSize = 2f;
        public float MaxWalkableSlopeDegrees = 32f;
        public int NavBorderCells = 3;
        public int PathRequestsPerTick = 6;
        public int PathNodeBudget = 40000;

        // Economy (GDD §5)
        public int StartingVillagers = 10;
        public int PopulationCap = 50;
        public int CampFood = 40;
        public int CampWood = 24;
        public int CampWater = 16;
        public int CampCapacity = 100;
        public int WoodPerTree = 8;
        public float FoodPerAdultPerDay = 2f;
        public float WaterPerAdultPerDay = 2f;

        // Buildings (GDD §5, §13)
        public int HouseWood = 20;
        public int HouseBeds = 4;
        public float HouseWork = 150f;
        public int StorehouseWood = 30;
        public int StorehouseCapacity = 200;
        public float StorehouseWork = 180f;
        public int FieldWood = 10;
        public float FieldWork = 40f;
        public float FieldGrowDays = 0.7f;
        public int FieldHarvestFood = 12;
        public float SowSeconds = 18f;
        public float HarvestSecondsPerLoad = 7f;
        public int FishingHutWood = 8;
        public float FishingHutWork = 60f;
        public float FishSecondsPerFood = 36f;
        public int BridgeWoodPerMeter = 4;
        public float BridgeMaxSpan = 12f;
        public float BridgeWorkPerMeter = 10f;
        public float BuildSecondsPerStep = 4f;
        public int MaxBuildersPerSite = 3;

        // Villagers (GDD §6)
        public int CarryCapacity = 4;
        public float WalkSpeed = 2.2f;
        public float ChopSecondsPerLoad = 6f;
        public float DeliverSeconds = 1f;
        public float EatSeconds = 3f;
        public float DrinkSeconds = 2f;
        public float DrawWaterSeconds = 5f;
        public int AiEvaluateEveryTicks = 5;
        public float InteractRange = 1.6f;
        public float HungerToEat = 1f;
        public float ThirstToDrink = 1f;
        public float EnergyDrainPerDay = 1.15f;
        public float SleepHoursToRest = 7f;
        public float StarvingThreshold = 2.5f;
        public float HealthLossPerDay = 0.6f;
        public float HealthRegenPerDay = 0.4f;
        public float EasyModeMinHealth = 0.25f;
        public float TaskSwitchHysteresis = 12f;
        public float MinTaskSeconds = 6f;

        // Planner (GDD §6)
        public float PlannerIntervalSeconds = 5f;
        public int MaxOpenChopTasks = 6;
        public int WoodBufferTarget = 40;
        public int MaxTaskFailures = 3;
        public float TaskFailCooldownSeconds = 30f;
        public float FoodReserveDays = 2.5f;
        public float WaterReserveDays = 1.2f;
        public int MaxConcurrentHouseSites = 2;
        public float SiteSearchMinRadius = 12f;
        public float SiteSearchMaxRadius = 70f;
        public float FieldSearchMaxRadius = 90f;
        public float SiteMaxSlopeDegrees = 12f;
        public float SiteMaxHeightRange = 1.6f;
        public float ImmigrationFoodDays = 2f;
        public int ImmigrantsPerArrival = 2;

        // Tools (GDD §4)
        public float BrushDefaultRadius = 8f;
        public float BrushMinRadius = 3f;
        public float BrushMaxRadius = 25f;
        public float BrushMaxDeltaPerCommand = 1.0f;
        public float BrushMaxDeltaPerStroke = 8f;
        public float FlattenDefaultRadius = 10f;
        public float CampProtectionRadius = 7f;
        public float CampProtectionBlend = 5f;
        public float BuildingProtectionBlend = 4f;
        public float RiverMinWidth = 3f;
        public float RiverMaxWidth = 8f;
        public float RiverDefaultWidth = 5f;
        public float RiverDepth = 1.4f;
        public float RiverMaxCut = 7f;
        public float RiverMaxLength = 420f;
        public float RiverSampleSpacing = 1.5f;
        public float ForestDefaultRadius = 12f;
        public float ForestMinSpacing = 3.4f;
        public int ForestMaxPerStamp = 14;
        public float SaplingGrowDays = 1.0f;
        public int SummonMaxPerBatch = 10;
        public int UndoDepth = 24;

        // Ecology / roads (GDD §11.3, §13)
        public float WetnessFalloffMeters = 14f;
        public float RoadWearPerStep = 1f;
        public float RoadWearForFullRoad = 60f;
        public float RoadDailyDecay = 0.92f;
        public float RoadSpeedBonus = 0.35f;
        public float StumpRotDays = 3f;
        public float NaturalSeedingPerDay = 3f;

        public float TickSeconds => 1f / TicksPerSecond;
        public int SecondsToTicks(float seconds) => (int)System.Math.Ceiling(seconds * TicksPerSecond - 1e-4);
        public int TicksPerDay => SecondsToTicks(SecondsPerGameDay);
        public float GameHoursToSeconds(float hours) => hours / 24f * SecondsPerGameDay;

        public static SimConfig CreateDefault() => new SimConfig();

        /// <summary>Stable FNV-1a hash of every tuning value; stored in save headers to detect config drift.</summary>
        public ulong ComputeHash()
        {
            var sb = new StringBuilder();
            foreach (var f in typeof(SimConfig).GetFields())
            {
                object v = f.GetValue(this);
                sb.Append(f.Name).Append('=').Append(System.Convert.ToString(v, CultureInfo.InvariantCulture)).Append(';');
            }
            return Hashing.Fnv1a64(Encoding.UTF8.GetBytes(sb.ToString()));
        }
    }

    public static class Hashing
    {
        public static ulong Fnv1a64(byte[] data)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < data.Length; i++) { h ^= data[i]; h *= 1099511628211UL; }
            return h;
        }
    }
}
