using System.Collections.Generic;

namespace GenesisValley.Core
{
    public enum SimEventType
    {
        TerrainEdited = 1,
        CommandRejected = 2,
        TaskPublished = 3,
        TreeFelled = 4,
        WoodDelivered = 5,
        PathFailed = 6,
        TaskAbandoned = 7,
        VillagerRelocated = 8,
        FoodShortage = 9,
        VillagerAte = 10,
        WorldLoaded = 11,
        Milestone = 12,
        SitePlanned = 13,
        SiteStageStarted = 14,
        BuildingCompleted = 15,
        SiteWaiting = 16,
        SiteCancelled = 17,
        FieldSown = 18,
        FieldHarvested = 19,
        FishCaught = 20,
        WaterFetched = 21,
        WaterShortage = 22,
        RiverCreated = 23,
        ForestPlanted = 24,
        VillagersArrived = 25,
        VillagerMovedIn = 26,
        VillagerSlept = 27,
        VillagerWoke = 28,
        BridgePlanned = 29,
        RegionConnected = 30,
        UndoApplied = 31,
        UndoRejected = 32,
        VillagerWeak = 33,
        VillagerRecovered = 34,
        SaplingMatured = 35,
        Hint = 36,
        Saved = 37,
    }

    /// <summary>How prominent an event is for the presentation layer (story feed vs. debug log).</summary>
    public enum EventImportance
    {
        Debug = 0,
        Info = 1,
        Story = 2,
        Milestone = 3,
        Warning = 4,
    }

    /// <summary>Fact-only story/log record (GDD §6): carries actor, target and position, never invented drama.</summary>
    public struct SimEvent
    {
        public long Tick;
        public SimEventType Type;
        public EventImportance Importance;
        public EntityId Actor;
        public EntityId Target;
        public float X, Z;
        public string Message;

        public override string ToString() => "[" + Tick + "] " + Type + " " + Message;
    }

    /// <summary>Bounded event log. Presentation drains <see cref="Pending"/>; history keeps the latest N entries.</summary>
    public sealed class EventLog
    {
        private readonly int _capacity;
        private readonly List<SimEvent> _history = new List<SimEvent>();
        private readonly List<SimEvent> _story = new List<SimEvent>();
        public readonly List<SimEvent> Pending = new List<SimEvent>();

        public EventLog(int capacity = 300) { _capacity = capacity; }

        public IReadOnlyList<SimEvent> History => _history;
        /// <summary>Story-worthy events (Story/Milestone/Warning) kept separately so chatter cannot push them out.</summary>
        public IReadOnlyList<SimEvent> Story => _story;

        public void Add(SimEvent e)
        {
            _history.Add(e);
            if (_history.Count > _capacity) _history.RemoveAt(0);
            if (e.Importance >= EventImportance.Story)
            {
                _story.Add(e);
                if (_story.Count > _capacity) _story.RemoveAt(0);
            }
            Pending.Add(e);
            if (Pending.Count > _capacity * 2) Pending.RemoveAt(0);
        }

        /// <summary>Used by persistence to restore the story feed.</summary>
        public void RestoreStory(SimEvent e)
        {
            _story.Add(e);
            if (_story.Count > _capacity) _story.RemoveAt(0);
        }

        public void Clear() { _history.Clear(); _story.Clear(); Pending.Clear(); }
    }
}
