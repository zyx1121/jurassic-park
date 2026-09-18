using System;

namespace JurassicPark.Simulation
{
    public sealed class TaskConfig
    {
        /// <summary>Extra path cost of one blocker cell when nothing better is configured: about seven straight cells, so a short wall is walked around and a long one broken.</summary>
        public const int DefaultBreachCost = 74;

        /// <summary>Ticks a task waits before planning again after a failed or over-budget plan, so a stuck unit never re-runs a full search every tick.</summary>
        public int ReplanIntervalTicks { get; }

        /// <summary>How many times in a row a move may plan again (route cut, or search over budget) without reaching a waypoint before it gives up as failed. Progress resets the count.</summary>
        public int MaxReplans { get; }

        /// <summary>Tasks that may wait behind an actor's current one.</summary>
        public int MaxQueuedPerActor { get; }

        public PathOptions UnitPathOptions { get; }

        /// <summary>Path options for something willing to break through: destructible blockers are passable at a price.</summary>
        public PathOptions BreachPathOptions { get; }

        public TaskConfig(int replanIntervalTicks, int maxReplans, int maxQueuedPerActor, PathOptions unitPathOptions, PathOptions breachPathOptions = null)
        {
            if (replanIntervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(replanIntervalTicks));
            if (maxReplans < 0) throw new ArgumentOutOfRangeException(nameof(maxReplans));
            if (maxQueuedPerActor < 0) throw new ArgumentOutOfRangeException(nameof(maxQueuedPerActor));
            ReplanIntervalTicks = replanIntervalTicks;
            MaxReplans = maxReplans;
            MaxQueuedPerActor = maxQueuedPerActor;
            UnitPathOptions = unitPathOptions ?? throw new ArgumentNullException(nameof(unitPathOptions));
            BreachPathOptions = breachPathOptions ?? new PathOptions(allowBreach: true, breachCost: DefaultBreachCost);
        }
    }
}
