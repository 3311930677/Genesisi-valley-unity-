using System.Collections.Generic;
using GenesisValley.Core;

namespace GenesisValley.Simulation
{
    /// <summary>Fixed-step driver (10 Hz, GDD §5). Rendering never decides production; speed only changes ticks per frame.</summary>
    public sealed class SimulationRunner
    {
        public WorldState World { get; private set; }
        public readonly CommandProcessor Commands;
        private readonly VillagePlanner _planner = new VillagePlanner();
        private readonly VillagerBrain _brain = new VillagerBrain();
        private readonly EcologySystem _ecology = new EcologySystem();
        private readonly PopulationSystem _population = new PopulationSystem();
        private readonly Queue<GameCommand> _pending = new Queue<GameCommand>();
        private readonly int _plannerEvery;

        /// <summary>Wall-clock milliseconds of the last Step (diagnostics panel).</summary>
        public double LastStepMs { get; private set; }
        public double WorstStepMs { get; set; }

        public SimulationRunner(WorldState world)
        {
            World = world;
            Commands = new CommandProcessor(world);
            _plannerEvery = world.Config.SecondsToTicks(world.Config.PlannerIntervalSeconds);
            world.Undo.Depth = world.Config.UndoDepth;
        }

        public void Enqueue(GameCommand cmd) => _pending.Enqueue(cmd);

        public CommandResult ApplyImmediate(GameCommand cmd) => Commands.Apply(cmd);

        public void Step()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var w = World;
            w.Tick++;
            w.PathBudget = w.Config.PathRequestsPerTick;
            while (_pending.Count > 0) Commands.Apply(_pending.Dequeue());
            if (w.Tick % _plannerEvery == 1) _planner.Evaluate(w);
            _ecology.Tick(w);
            _population.Tick(w);
            // Rotate the start index so path budget is shared fairly between villagers.
            int n = w.Villagers.Count;
            int start = n > 0 ? (int)(w.Tick % n) : 0;
            for (int i = 0; i < n; i++) _brain.Tick(w, w.Villagers[(start + i) % n]);
            LastStepMs = sw.Elapsed.TotalMilliseconds;
            if (LastStepMs > WorstStepMs) WorstStepMs = LastStepMs;
        }

        public void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++) Step();
        }
    }
}
