using System;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// The working memory of one path search, reused across queries. A map sized allocation and a clearing pass per
    /// query cost more than most searches do, and a unit asking for a three cell route must not pay for the whole map,
    /// so the arrays live as long as the <see cref="GridMap"/> and each query only bumps a generation number: a cell
    /// whose stamp is older than the current generation is simply unvisited.
    ///
    /// It holds one search at a time. The simulation runs single threaded on the host, so every query goes through the
    /// same buffer in turn; a second thread or a search started inside another search would read the first one's state.
    /// </summary>
    internal sealed class PathScratch
    {
        private int[] costs = Array.Empty<int>();
        private int[] cameFrom = Array.Empty<int>();
        private int[] visitStamps = Array.Empty<int>();
        private int[] closedStamps = Array.Empty<int>();
        private int[] heapCells = new int[64];
        private int[] heapTotals = new int[64];
        private int[] heapRemainders = new int[64];
        private int generation;
        private int heapCount;

        /// <summary>Starts a new search over a map of this many cells. Nothing is cleared: the generation bump invalidates every stamp at once.</summary>
        public void Begin(int cellCount)
        {
            if (costs.Length < cellCount)
            {
                Array.Resize(ref costs, cellCount);
                Array.Resize(ref cameFrom, cellCount);
                Array.Resize(ref visitStamps, cellCount);
                Array.Resize(ref closedStamps, cellCount);
            }

            if (generation == int.MaxValue)
            {
                // Wrapping would make ancient stamps look current, so start the numbering over on fresh arrays.
                visitStamps = new int[costs.Length];
                closedStamps = new int[costs.Length];
                generation = 0;
            }

            generation++;
            heapCount = 0;
        }

        public bool IsVisited(int cell) => visitStamps[cell] == generation;

        public bool IsClosed(int cell) => closedStamps[cell] == generation;

        public void Close(int cell) => closedStamps[cell] = generation;

        public int CostOf(int cell) => costs[cell];

        public int CameFrom(int cell) => cameFrom[cell];

        public void Visit(int cell, int cost, int from)
        {
            visitStamps[cell] = generation;
            costs[cell] = cost;
            cameFrom[cell] = from;
        }

        public int OpenCount => heapCount;

        /// <summary>
        /// Queues a cell by total cost, then by the remaining estimate, then by cell index. The last two keys are what
        /// make ties resolve the same way on every run instead of following the order cells happened to be reached in.
        /// </summary>
        public void Push(int cell, int total, int remaining)
        {
            if (heapCount == heapCells.Length)
            {
                Array.Resize(ref heapCells, heapCells.Length * 2);
                Array.Resize(ref heapTotals, heapTotals.Length * 2);
                Array.Resize(ref heapRemainders, heapRemainders.Length * 2);
            }

            int child = heapCount++;
            heapCells[child] = cell;
            heapTotals[child] = total;
            heapRemainders[child] = remaining;
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
            int top = heapCells[0];
            heapCount--;
            if (heapCount > 0)
            {
                heapCells[0] = heapCells[heapCount];
                heapTotals[0] = heapTotals[heapCount];
                heapRemainders[0] = heapRemainders[heapCount];
                int parent = 0;
                while (true)
                {
                    int left = parent * 2 + 1;
                    if (left >= heapCount) break;
                    int best = left;
                    int right = left + 1;
                    if (right < heapCount && IsBefore(right, left)) best = right;
                    if (!IsBefore(best, parent)) break;
                    Swap(best, parent);
                    parent = best;
                }
            }

            return top;
        }

        private bool IsBefore(int a, int b)
        {
            if (heapTotals[a] != heapTotals[b]) return heapTotals[a] < heapTotals[b];
            if (heapRemainders[a] != heapRemainders[b]) return heapRemainders[a] < heapRemainders[b];
            return heapCells[a] < heapCells[b];
        }

        private void Swap(int a, int b)
        {
            (heapCells[a], heapCells[b]) = (heapCells[b], heapCells[a]);
            (heapTotals[a], heapTotals[b]) = (heapTotals[b], heapTotals[a]);
            (heapRemainders[a], heapRemainders[b]) = (heapRemainders[b], heapRemainders[a]);
        }
    }
}
