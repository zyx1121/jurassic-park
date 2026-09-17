using System;

namespace JurassicPark.Simulation
{
    public sealed class TaskConfig
    {
        /// <summary>Ticks a task waits before planning again after a failed or over-budget plan, so a stuck unit never re-runs a full search every tick.</summary>
        public int ReplanIntervalTicks { get; }

        /// <summary>How many times a move replans around a changed map before it gives up as failed.</summary>
        public int MaxReplans { get; }

        /// <summary>Tasks that may wait behind an actor's current one.</summary>
        public int MaxQueuedPerActor { get; }

        public PathOptions UnitPathOptions { get; }

        public TaskConfig(int replanIntervalTicks, int maxReplans, int maxQueuedPerActor, PathOptions unitPathOptions)
        {
            if (replanIntervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(replanIntervalTicks));
            if (maxReplans < 0) throw new ArgumentOutOfRangeException(nameof(maxReplans));
            if (maxQueuedPerActor < 0) throw new ArgumentOutOfRangeException(nameof(maxQueuedPerActor));
            ReplanIntervalTicks = replanIntervalTicks;
            MaxReplans = maxReplans;
            MaxQueuedPerActor = maxQueuedPerActor;
            UnitPathOptions = unitPathOptions ?? throw new ArgumentNullException(nameof(unitPathOptions));
        }
    }
}
