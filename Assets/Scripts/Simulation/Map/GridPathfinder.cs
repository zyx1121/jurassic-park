using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Eight directional A* over a <see cref="GridMap"/>, with integer costs so two routes that look equal really are
    /// equal and the same query always returns the same cells. Destructible blockers are the one thing that is not
    /// simply passable or solid: a breach is a route priced above going around, so a dinosaur breaks a wall when that
    /// is genuinely the cheaper way in, never because the wall happens to be nearby.
    ///
    /// Every query runs on the map's own <see cref="PathScratch"/>, so the search allocates nothing per cell. That
    /// makes it single threaded by construction: the host runs one query at a time, and searches never nest.
    /// </summary>
    public static class GridPathfinder
    {
        /// <summary>
        /// The cheapest route from start to goal, or why there is none. Blockers are read from the map as it is now and
        /// the result carries that version, so the caller can tell a stale route from a current one.
        /// </summary>
        public static PathResult FindPath(GridMap map, Cell start, Cell goal, PathOptions options)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            return FindPathToAny(map, start, new[] { goal }, options);
        }

        /// <summary>
        /// The cheapest route from start to whichever of these goals is cheapest to reach, found by one search rather
        /// than one search per goal: asking about sixteen cells around a building must not cost sixteen sweeps of the
        /// map. Ties go to the lower cell index, so the answer does not depend on the order the goals were listed.
        /// An empty goal list is <see cref="PathStatus.NoRoute"/>; goals that terrain rules out are skipped, and if
        /// that leaves none the query itself was wrong, which is <see cref="PathStatus.InvalidEndpoint"/>.
        /// </summary>
        public static PathResult FindPathToAny(GridMap map, Cell start, IReadOnlyList<Cell> goals, PathOptions options)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (goals == null) throw new ArgumentNullException(nameof(goals));
            if (options == null) throw new ArgumentNullException(nameof(options));
            long version = map.Version;

            // Terrain decides whether an endpoint is even askable; occupancy only decides whether it is reachable.
            if (!IsUsableEndpoint(map, start)) return PathResult.Failed(PathStatus.InvalidEndpoint, version);
            if (goals.Count == 0) return PathResult.Failed(PathStatus.NoRoute, version);

            int cellCount = map.Width * map.Height;
            var goalIndices = new List<int>(goals.Count);
            var goalCells = new List<Cell>(goals.Count);
            int startIndex = map.IndexOf(start);
            for (int i = 0; i < goals.Count; i++)
            {
                Cell goal = goals[i];
                if (!IsUsableEndpoint(map, goal)) continue;
                int index = map.IndexOf(goal);
                if (index == startIndex) return BuildResult(map, new[] { start }, version, 0, 0);
                if (goalIndices.Contains(index)) continue;
                goalIndices.Add(index);
                goalCells.Add(goal);
            }

            if (goalIndices.Count == 0) return PathResult.Failed(PathStatus.InvalidEndpoint, version);

            int budget = options.MaxExpandedNodes > 0 ? options.MaxExpandedNodes : cellCount;
            PathScratch scratch = map.Scratch;
            scratch.Begin(cellCount);
            scratch.Visit(startIndex, 0, -1);
            scratch.Push(startIndex, Heuristic(start, goalCells, options), 0);

            int expanded = 0;
            while (scratch.OpenCount > 0)
            {
                int currentIndex = scratch.Pop();
                if (scratch.IsClosed(currentIndex)) continue; // an outdated entry for a cell already settled
                if (goalIndices.Contains(currentIndex))
                {
                    return BuildResult(map, Reconstruct(map, scratch, currentIndex), version, scratch.CostOf(currentIndex), expanded);
                }

                if (expanded == budget) return PathResult.Failed(PathStatus.BudgetExceeded, version, expanded);

                scratch.Close(currentIndex);
                expanded++;
                var current = new Cell(currentIndex % map.Width, currentIndex / map.Width);
                for (int d = 0; d < GridDirections.Count; d++)
                {
                    int dx = GridDirections.X[d];
                    int dy = GridDirections.Y[d];
                    var next = new Cell(current.X + dx, current.Y + dy);
                    if (!CanEnter(map, next, options, out int breachCost)) continue;
                    bool diagonal = GridDirections.IsDiagonal(d);
                    if (diagonal && !CanTurnCorner(map, current, dx, dy)) continue;

                    int nextIndex = map.IndexOf(next);
                    if (scratch.IsClosed(nextIndex)) continue;
                    int candidate = scratch.CostOf(currentIndex) + (diagonal ? options.DiagonalCost : options.StraightCost) + breachCost;
                    if (scratch.IsVisited(nextIndex) && candidate >= scratch.CostOf(nextIndex)) continue;

                    scratch.Visit(nextIndex, candidate, currentIndex);
                    int remaining = Heuristic(next, goalCells, options);
                    scratch.Push(nextIndex, candidate + remaining, remaining);
                }
            }

            return PathResult.Failed(PathStatus.NoRoute, version, expanded);
        }

        /// <summary>
        /// The cheapest reachable cell next to a footprint, which is where a unit stands to gather, deliver, build or
        /// attack. It is never a cell inside the footprint, because standing inside a rock or a building is what makes
        /// units teleport through walls, and the returned path is the route to that exact cell rather than to the
        /// target's centre. One search answers for every side at once.
        /// </summary>
        public static bool TryFindApproachCell(
            GridMap map,
            Cell from,
            IReadOnlyList<Cell> footprint,
            PathOptions options,
            out Cell approach,
            out PathResult path)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            if (options == null) throw new ArgumentNullException(nameof(options));

            approach = default;
            List<Cell> candidates = CollectApproachCells(map, footprint);
            path = FindPathToAny(map, from, candidates, options);
            // A failure keeps its own status: a budget cut off leaves the question open and must not read as an
            // enclosed target, while an empty candidate list really is one.
            if (!path.IsFound) return false;

            approach = path.Cells[path.Cells.Count - 1];
            return true;
        }

        /// <summary>Walkable cells touching the footprint in the eight neighbourhood, in row major order, never a footprint cell itself.</summary>
        private static List<Cell> CollectApproachCells(GridMap map, IReadOnlyList<Cell> footprint)
        {
            var inFootprint = new HashSet<int>();
            for (int i = 0; i < footprint.Count; i++)
            {
                if (map.InBounds(footprint[i])) inFootprint.Add(map.IndexOf(footprint[i]));
            }

            var seen = new HashSet<int>();
            var candidates = new List<Cell>();
            for (int i = 0; i < footprint.Count; i++)
            {
                Cell cell = footprint[i];
                for (int d = 0; d < GridDirections.Count; d++)
                {
                    var neighbour = new Cell(cell.X + GridDirections.X[d], cell.Y + GridDirections.Y[d]);
                    if (!map.InBounds(neighbour) || !map.IsWalkable(neighbour)) continue;
                    int index = map.IndexOf(neighbour);
                    if (inFootprint.Contains(index) || !seen.Add(index)) continue;
                    candidates.Add(neighbour);
                }
            }

            candidates.Sort((a, b) => map.IndexOf(a).CompareTo(map.IndexOf(b)));
            return candidates;
        }

        private static bool IsUsableEndpoint(GridMap map, Cell cell) => map.InBounds(cell) && map.IsStaticWalkable(cell);

        /// <summary>Terrain must allow the cell and nothing may hold it, unless it is a destructible blocker the query is allowed to break through.</summary>
        private static bool CanEnter(GridMap map, Cell cell, PathOptions options, out int breachCost)
        {
            breachCost = 0;
            if (!map.InBounds(cell) || !map.IsStaticWalkable(cell)) return false;
            if (map.BlockerAt(cell).IsNone) return true;
            if (!options.AllowBreach || !map.IsDestructibleBlocker(cell)) return false;
            breachCost = options.BreachCost;
            return true;
        }

        /// <summary>
        /// A diagonal step needs both cells beside the corner open right now. Requiring them free rather than merely
        /// breachable stops a unit slipping between two buildings it never paid to destroy.
        /// </summary>
        private static bool CanTurnCorner(GridMap map, Cell from, int dx, int dy) =>
            map.IsWalkable(new Cell(from.X + dx, from.Y)) && map.IsWalkable(new Cell(from.X, from.Y + dy));

        /// <summary>
        /// Octile distance to the nearest goal: the cost of that move on an empty map, so it never overestimates and
        /// A* stays optimal. Taking the minimum over the goals keeps it admissible for a search that may end at any of
        /// them, and consistent, so a settled cell never has to be reopened.
        /// </summary>
        private static int Heuristic(Cell from, List<Cell> goals, PathOptions options)
        {
            int best = int.MaxValue;
            for (int i = 0; i < goals.Count; i++)
            {
                int dx = Math.Abs(from.X - goals[i].X);
                int dy = Math.Abs(from.Y - goals[i].Y);
                int diagonal = Math.Min(dx, dy);
                int estimate = options.DiagonalCost * diagonal + options.StraightCost * (Math.Max(dx, dy) - diagonal);
                if (estimate < best) best = estimate;
            }

            return best;
        }

        private static Cell[] Reconstruct(GridMap map, PathScratch scratch, int goalIndex)
        {
            int length = 0;
            for (int index = goalIndex; index >= 0; index = scratch.CameFrom(index)) length++;
            var cells = new Cell[length];
            int position = length - 1;
            for (int index = goalIndex; index >= 0; index = scratch.CameFrom(index))
            {
                cells[position--] = new Cell(index % map.Width, index / map.Width);
            }

            return cells;
        }

        /// <summary>Wraps the route and lists the destructible blockers it crosses, each once, in travel order. The start cell is never a breach: the unit is already standing there.</summary>
        private static PathResult BuildResult(GridMap map, IReadOnlyList<Cell> cells, long version, int cost, int expanded)
        {
            var breached = new List<EntityId>();
            for (int i = 1; i < cells.Count; i++)
            {
                if (!map.IsDestructibleBlocker(cells[i])) continue;
                EntityId blocker = map.BlockerAt(cells[i]);
                if (!breached.Contains(blocker)) breached.Add(blocker);
            }

            return new PathResult(PathStatus.Found, new List<Cell>(cells).AsReadOnly(), breached.AsReadOnly(), version, cost, expanded);
        }
    }
}
