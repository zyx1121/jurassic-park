using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// The answer to one path query, tied to the map version it was computed on. The version is part of the result
    /// because occupancy changes while units walk: a mover that keeps following a route computed before a wall went up
    /// would walk through it, so a result is only usable while the version still matches.
    /// </summary>
    public sealed class PathResult
    {
        private static readonly IReadOnlyList<Cell> NoCells = Array.Empty<Cell>();
        private static readonly IReadOnlyList<EntityId> NoBreaches = Array.Empty<EntityId>();

        public PathStatus Status { get; }

        /// <summary>The route from start to goal inclusive, empty unless the status is Found.</summary>
        public IReadOnlyList<Cell> Cells { get; }

        /// <summary>The destructible blockers the route crosses, each once, in the order they are met. Empty means the route needs nothing destroyed.</summary>
        public IReadOnlyList<EntityId> Breached { get; }

        /// <summary><see cref="GridMap.Version"/> when the query ran.</summary>
        public long MapVersion { get; }

        /// <summary>Total cost of the route including breach charges, zero unless the status is Found.</summary>
        public int Cost { get; }

        public bool IsFound => Status == PathStatus.Found;

        public PathResult(PathStatus status, IReadOnlyList<Cell> cells, IReadOnlyList<EntityId> breached, long mapVersion, int cost)
        {
            Status = status;
            Cells = cells ?? NoCells;
            Breached = breached ?? NoBreaches;
            MapVersion = mapVersion;
            Cost = cost;
        }

        public static PathResult Failed(PathStatus status, long mapVersion) =>
            new PathResult(status, NoCells, NoBreaches, mapVersion, 0);

        public override string ToString() => $"Path({Status}, {Cells.Count} cells, cost {Cost}, {Breached.Count} breached, v{MapVersion})";
    }
}
