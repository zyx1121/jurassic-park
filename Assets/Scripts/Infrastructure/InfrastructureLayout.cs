using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Infrastructure
{
    public enum MapSurface { Jungle, Road, Clearing, Ridge, Water }

    public sealed class InfrastructureLayout
    {
        public InfraMap Map { get; }
        public InfrastructureMapDefinition Definition { get; }
        public MapSurface[] Surfaces { get; }
        public InfrastructureMapDefinition.Camp MainCamp => Definition.camps[Definition.demonstrationCamp];
        public Vector2Int RaidSpawn => MainCamp.Gate + MainCamp.entranceDirection * 5;

        public InfrastructureLayout(InfraMap map, InfrastructureMapDefinition definition)
        {
            Map = map;
            Definition = definition;
            Surfaces = new MapSurface[map.Width * map.Height];
        }

        public MapSurface SurfaceAt(Vector2Int cell) => Surfaces[cell.x + cell.y * Map.Width];

        public void Paint(Vector2Int cell, MapSurface surface)
        {
            if (!Map.Contains(cell)) throw new InvalidOperationException($"Map feature outside boundary at {cell}.");
            Surfaces[cell.x + cell.y * Map.Width] = surface;
            InfraTileKind kind = surface == MapSurface.Water ? InfraTileKind.Water :
                surface == MapSurface.Ridge ? InfraTileKind.Cliff : InfraTileKind.Ground;
            Map.SetTile(cell, kind, surface == MapSurface.Ridge ? Definition.cliffHeight :
                surface == MapSurface.Water ? -.3f : 0f);
        }

        public void Paint(RectInt rectangle, MapSurface surface)
        {
            foreach (Vector2Int cell in rectangle.allPositionsWithin) Paint(cell, surface);
        }

        public void Road(Vector2Int start, Vector2Int end, int halfWidth)
        {
            int length = Mathf.Max(Mathf.Abs(end.x - start.x), Mathf.Abs(end.y - start.y));
            for (int i = 0; i <= length; i++)
            {
                Vector2 p = Vector2.Lerp(start, end, length == 0 ? 0f : (float)i / length);
                var center = new Vector2Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y));
                for (int y = -halfWidth; y <= halfWidth; y++)
                for (int x = -halfWidth; x <= halfWidth; x++)
                    Paint(center + new Vector2Int(x, y), MapSurface.Road);
            }
        }

        public void Validate()
        {
            var reached = new HashSet<Vector2Int>();
            var queue = new Queue<Vector2Int>();
            if (!Ground(Definition.arrival)) throw new InvalidOperationException("Arrival is not traversable.");
            queue.Enqueue(Definition.arrival);
            reached.Add(Definition.arrival);
            Vector2Int[] directions = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                foreach (Vector2Int direction in directions)
                {
                    Vector2Int next = cell + direction;
                    if (Ground(next) && reached.Add(next)) queue.Enqueue(next);
                }
            }
            foreach (InfrastructureMapDefinition.Camp camp in Definition.camps)
                foreach (Vector2Int cell in new[] { camp.center, camp.Gate, camp.Depot, camp.Source })
                    if (!reached.Contains(cell))
                        throw new InvalidOperationException($"Camp {camp.name} has no arrival route to {cell}.");
            if (!reached.Contains(RaidSpawn))
                throw new InvalidOperationException($"Demonstration raid spawn has no route: {RaidSpawn}.");
            foreach (RectInt zone in Definition.eventZones)
            {
                if (zone.width <= 0 || zone.height <= 0 ||
                    !Map.Contains(zone.min) || !Map.Contains(zone.max - Vector2Int.one))
                    throw new InvalidOperationException($"Invalid event candidate zone: {zone}.");
            }
        }

        private bool Ground(Vector2Int cell) => Map.Contains(cell) && Map.TileAt(cell) == InfraTileKind.Ground;
    }
}
