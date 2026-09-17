using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Infrastructure
{
    internal static class InfraPathfinder
    {
        internal static readonly Vector2Int[] Directions =
        {
            Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down
        };

        internal static List<Vector2Int> Find(InfraMap map, Vector2Int start,
            List<Vector2Int> goals, Func<Vector2Int, float> cost)
        {
            if (!map.Contains(start) || goals.Count == 0)
                return null;
            var goalSet = new HashSet<Vector2Int>(goals);
            var distances = new Dictionary<Vector2Int, float> { [start] = 0 };
            var parents = new Dictionary<Vector2Int, Vector2Int>();
            var open = new MinHeap();
            open.Push(start, 0);
            while (open.Count > 0)
            {
                Entry current = open.Pop();
                if (current.Cost > distances[current.Cell])
                    continue;
                if (goalSet.Contains(current.Cell))
                {
                    var route = new List<Vector2Int> { current.Cell };
                    Vector2Int cell = current.Cell;
                    while (cell != start)
                    {
                        cell = parents[cell];
                        route.Add(cell);
                    }
                    route.Reverse();
                    return route;
                }
                foreach (Vector2Int direction in Directions)
                {
                    Vector2Int next = current.Cell + direction;
                    if (!map.Contains(next))
                        continue;
                    float stepCost = cost(next);
                    if (float.IsPositiveInfinity(stepCost))
                        continue;
                    float distance = current.Cost + stepCost;
                    if (distances.TryGetValue(next, out float previous) && previous <= distance)
                        continue;
                    distances[next] = distance;
                    parents[next] = current.Cell;
                    open.Push(next, distance);
                }
            }
            return null;
        }

        readonly struct Entry
        {
            public readonly Vector2Int Cell;
            public readonly float Cost;
            public readonly int Order;
            public Entry(Vector2Int cell, float cost, int order)
            {
                Cell = cell;
                Cost = cost;
                Order = order;
            }
        }

        sealed class MinHeap
        {
            readonly List<Entry> entries = new List<Entry>();
            int sequence;
            public int Count => entries.Count;
            static bool Before(Entry a, Entry b) =>
                a.Cost < b.Cost || (a.Cost == b.Cost && a.Order < b.Order);

            public void Push(Vector2Int cell, float cost)
            {
                var entry = new Entry(cell, cost, sequence++);
                int index = entries.Count;
                entries.Add(entry);
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Before(entry, entries[parent]))
                        break;
                    entries[index] = entries[parent];
                    index = parent;
                }
                entries[index] = entry;
            }

            public Entry Pop()
            {
                Entry result = entries[0];
                Entry last = entries[entries.Count - 1];
                entries.RemoveAt(entries.Count - 1);
                if (entries.Count == 0)
                    return result;
                int index = 0;
                while (index * 2 + 1 < entries.Count)
                {
                    int child = index * 2 + 1;
                    if (child + 1 < entries.Count && Before(entries[child + 1], entries[child]))
                        child++;
                    if (!Before(entries[child], last))
                        break;
                    entries[index] = entries[child];
                    index = child;
                }
                entries[index] = last;
                return result;
            }
        }
    }
}
