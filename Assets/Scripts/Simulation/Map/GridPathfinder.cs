using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Eight directional A* over a <see cref="GridMap"/>, with integer costs so two routes that look equal really are
    /// equal and the same query always returns the same cells. Destructible blockers are the one thing that is not
    /// simply passable or solid: a breach is a route priced above going around, so a dinosaur breaks a wall when that
    /// is genuinely the cheaper way in, never because the wall happens to be nearby.
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
            if (options == null) throw new ArgumentNullException(nameof(options));
            long version = map.Version;

            // Terrain decides whether an endpoint is even askable; occupancy only decides whether it is reachable.
            if (!IsUsableEndpoint(map, start) || !IsUsableEndpoint(map, goal)) return PathResult.Failed(PathStatus.InvalidEndpoint, version);

            int startIndex = map.IndexOf(start);
            int goalIndex = map.IndexOf(goal);
            if (startIndex == goalIndex) return BuildResult(map, new[] { start }, version, 0);

            int cellCount = map.Width * map.Height;
            var cost = new int[cellCount];
            var cameFrom = new int[cellCount];
            var closed = new bool[cellCount];
            var reached = new bool[cellCount];
            for (int i = 0; i < cellCount; i++) cameFrom[i] = -1;

            var open = new OpenSet(cellCount);
            cost[startIndex] = 0;
            reached[startIndex] = true;
            open.Push(startIndex, Heuristic(start, goal, options), 0);

            int expanded = 0;
            while (open.Count > 0)
            {
                int currentIndex = open.Pop();
                if (closed[currentIndex]) continue; // an outdated entry for a cell already settled
                if (currentIndex == goalIndex) return BuildResult(map, Reconstruct(map, cameFrom, goalIndex), version, cost[goalIndex]);
                if (expanded == options.MaxExpandedNodes) return PathResult.Failed(PathStatus.BudgetExceeded, version);

                closed[currentIndex] = true;
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
                    if (closed[nextIndex]) continue;
                    int candidate = cost[currentIndex] + (diagonal ? options.DiagonalCost : options.StraightCost) + breachCost;
                    if (reached[nextIndex] && candidate >= cost[nextIndex]) continue;

                    reached[nextIndex] = true;
                    cost[nextIndex] = candidate;
                    cameFrom[nextIndex] = currentIndex;
                    int remaining = Heuristic(next, goal, options);
                    open.Push(nextIndex, candidate + remaining, remaining);
                }
            }

            return PathResult.Failed(PathStatus.NoRoute, version);
        }

        /// <summary>
        /// The cheapest reachable cell next to a footprint, which is where a unit stands to gather, deliver, build or
        /// attack. It is never a cell inside the footprint, because standing inside a rock or a building is what makes
        /// units teleport through walls, and the returned path is the route to that exact cell rather than to the
        /// target's centre.
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
            path = PathResult.Failed(PathStatus.NoRoute, map.Version);

            List<Cell> candidates = CollectApproachCells(map, footprint);
            PathResult best = null;
            int bestIndex = int.MaxValue;
            bool pending = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                Cell candidate = candidates[i];
                PathResult result = FindPath(map, from, candidate, options);
                if (result.Status == PathStatus.BudgetExceeded) pending = true;
                if (!result.IsFound) continue;
                int candidateIndex = map.IndexOf(candidate);
                // Cheapest wins; the lower cell index breaks ties so two equal sides never swap between calls.
                if (best != null && (result.Cost > best.Cost || (result.Cost == best.Cost && candidateIndex >= bestIndex))) continue;
                best = result;
                bestIndex = candidateIndex;
                approach = candidate;
            }

            if (best == null)
            {
                // A budget cut off leaves the question open, so it must not be reported as an enclosed target.
                path = PathResult.Failed(pending ? PathStatus.BudgetExceeded : PathStatus.NoRoute, map.Version);
                approach = default;
                return false;
            }

            path = best;
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

        /// <summary>Octile distance: the cost of the same move on an empty map, so it never overestimates and A* stays optimal.</summary>
        private static int Heuristic(Cell from, Cell to, PathOptions options)
        {
            int dx = Math.Abs(from.X - to.X);
            int dy = Math.Abs(from.Y - to.Y);
            int diagonal = Math.Min(dx, dy);
            return options.DiagonalCost * diagonal + options.StraightCost * (Math.Max(dx, dy) - diagonal);
        }

        private static Cell[] Reconstruct(GridMap map, int[] cameFrom, int goalIndex)
        {
            int length = 0;
            for (int index = goalIndex; index >= 0; index = cameFrom[index]) length++;
            var cells = new Cell[length];
            int position = length - 1;
            for (int index = goalIndex; index >= 0; index = cameFrom[index])
            {
                cells[position--] = new Cell(index % map.Width, index / map.Width);
            }

            return cells;
        }

        /// <summary>Wraps the route and lists the destructible blockers it crosses, each once, in travel order. The start cell is never a breach: the unit is already standing there.</summary>
        private static PathResult BuildResult(GridMap map, IReadOnlyList<Cell> cells, long version, int cost)
        {
            var breached = new List<EntityId>();
            for (int i = 1; i < cells.Count; i++)
            {
                if (!map.IsDestructibleBlocker(cells[i])) continue;
                EntityId blocker = map.BlockerAt(cells[i]);
                if (!breached.Contains(blocker)) breached.Add(blocker);
            }

            return new PathResult(PathStatus.Found, new List<Cell>(cells).AsReadOnly(), breached.AsReadOnly(), version, cost);
        }

        /// <summary>
        /// Binary heap of cell indices ordered by total cost, then by the remaining estimate, then by index. The last
        /// two keys are what make ties resolve the same way on every run instead of following allocation order.
        /// </summary>
        private sealed class OpenSet
        {
            private int[] cells;
            private int[] totals;
            private int[] remainders;

            public OpenSet(int capacity)
            {
                int size = Math.Max(capacity, 4);
                cells = new int[size];
                totals = new int[size];
                remainders = new int[size];
            }

            public int Count { get; private set; }

            public void Push(int cell, int total, int remaining)
            {
                if (Count == cells.Length) Grow();
                int child = Count++;
                cells[child] = cell;
                totals[child] = total;
                remainders[child] = remaining;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (!IsBefore(child, parent)) break;
                    Swap(child, parent);
                    child = parent;
                }
            }

            public int Pop()
            {
                int top = cells[0];
                Count--;
                if (Count > 0)
                {
                    cells[0] = cells[Count];
                    totals[0] = totals[Count];
                    remainders[0] = remainders[Count];
                    int parent = 0;
                    while (true)
                    {
                        int left = parent * 2 + 1;
                        if (left >= Count) break;
                        int best = left;
                        int right = left + 1;
                        if (right < Count && IsBefore(right, left)) best = right;
                        if (!IsBefore(best, parent)) break;
                        Swap(best, parent);
                        parent = best;
                    }
                }

                return top;
            }

            private bool IsBefore(int a, int b)
            {
                if (totals[a] != totals[b]) return totals[a] < totals[b];
                if (remainders[a] != remainders[b]) return remainders[a] < remainders[b];
                return cells[a] < cells[b];
            }

            private void Swap(int a, int b)
            {
                (cells[a], cells[b]) = (cells[b], cells[a]);
                (totals[a], totals[b]) = (totals[b], totals[a]);
                (remainders[a], remainders[b]) = (remainders[b], remainders[a]);
            }

            private void Grow()
            {
                Array.Resize(ref cells, cells.Length * 2);
                Array.Resize(ref totals, totals.Length * 2);
                Array.Resize(ref remainders, remainders.Length * 2);
            }
        }
    }
}
