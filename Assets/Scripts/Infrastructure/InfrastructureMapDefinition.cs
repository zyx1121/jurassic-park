using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Infrastructure
{
    [CreateAssetMenu(menuName = "Jurassic Park/Infrastructure/Fixed Map")]
    public sealed class InfrastructureMapDefinition : ScriptableObject
    {
        public string mapVersion = "camp-network-1";
        [Min(32)] public int width = 128;
        [Min(32)] public int height = 128;
        [Min(0.5f)] public float cellSize = 2f;
        [Min(1)] public int roadHalfWidth = 1;
        [Min(1f)] public float cliffHeight = 3f;
        public Vector2Int arrival = new Vector2Int(64, 64);
        public int demonstrationCamp = 5;
        public Camp[] camps =
        {
            new Camp("West inlet", 24, 28, 1, 0),
            new Camp("South shelf", 48, 22, 0, 1),
            new Camp("Old quarry", 78, 22, 0, 1),
            new Camp("East inlet", 103, 30, -1, 0),
            new Camp("Fern basin", 23, 58, 1, 0),
            new Camp("Arrival ridge", 48, 52, 1, 0),
            new Camp("Twin palms", 80, 49, 0, 1),
            new Camp("Eastern bluff", 105, 61, -1, 0),
            new Camp("West overlook", 25, 94, 0, -1),
            new Camp("North hollow", 49, 103, 0, -1),
            new Camp("River bend", 78, 101, 0, -1),
            new Camp("Ruins clearing", 102, 93, -1, 0)
        };
        public RectInt[] ridges =
        {
            new RectInt(30, 36, 12, 8), new RectInt(56, 27, 12, 13),
            new RectInt(87, 39, 9, 13), new RectInt(30, 71, 14, 10),
            new RectInt(60, 78, 9, 16), new RectInt(86, 72, 11, 11)
        };
        public RectInt[] lakes =
        {
            new RectInt(12, 71, 10, 12), new RectInt(68, 34, 7, 7),
            new RectInt(111, 76, 8, 11)
        };
        public RectInt[] eventZones =
        {
            new RectInt(59, 60, 10, 10), new RectInt(52, 72, 5, 5),
            new RectInt(70, 59, 5, 5), new RectInt(45, 37, 5, 5)
        };

        [Serializable]
        public sealed class Camp
        {
            public string name;
            public Vector2Int center;
            public Vector2Int entranceDirection;
            [Min(3)] public int radius = 5;

            public Camp(string name, int x, int y, int dx, int dy)
            {
                this.name = name;
                center = new Vector2Int(x, y);
                entranceDirection = new Vector2Int(dx, dy);
            }

            public Vector2Int Gate => center + entranceDirection * radius;
            public Vector2Int Depot => center - entranceDirection * 2;
            public Vector2Int Source => Gate + entranceDirection * 3 +
                new Vector2Int(-entranceDirection.y, entranceDirection.x) * 2;
        }

        public InfrastructureLayout CreateLayout()
        {
            if (width < 32 || height < 32 || cellSize <= 0 || roadHalfWidth < 0 ||
                camps == null || camps.Length == 0 || demonstrationCamp < 0 || demonstrationCamp >= camps.Length)
                throw new InvalidOperationException("Invalid fixed map dimensions, camps, or demonstration camp.");

            var layout = new InfrastructureLayout(new InfraMap(width, height, cellSize), this);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float u = (x + .5f - width * .5f) / (width * .47f);
                float v = (y + .5f - height * .5f) / (height * .47f);
                layout.Paint(new Vector2Int(x, y), u * u + v * v < 1f ? MapSurface.Jungle : MapSurface.Water);
            }
            foreach (RectInt lake in lakes) layout.Paint(lake, MapSurface.Water);
            foreach (RectInt ridge in ridges) layout.Paint(ridge, MapSurface.Ridge);
            foreach (Camp camp in camps)
            {
                if (Mathf.Abs(camp.entranceDirection.x) + Mathf.Abs(camp.entranceDirection.y) != 1 || camp.radius < 3)
                    throw new InvalidOperationException($"Camp {camp.name}: invalid entrance direction or radius.");
                layout.Road(arrival, camp.Gate + camp.entranceDirection * 3, roadHalfWidth);
                layout.Road(camp.Gate, camp.Source, roadHalfWidth);
            }
            var occupiedCamps = new HashSet<Vector2Int>();
            foreach (Camp camp in camps)
            {
                int r = camp.radius;
                for (int y = -r; y <= r; y++)
                for (int x = -r; x <= r; x++)
                {
                    Vector2Int cell = camp.center + new Vector2Int(x, y);
                    if (!occupiedCamps.Add(cell))
                        throw new InvalidOperationException($"Camp {camp.name} overlaps another camp at {cell}.");
                    bool edge = Mathf.Abs(x) == r || Mathf.Abs(y) == r;
                    layout.Paint(cell, edge ? MapSurface.Ridge : MapSurface.Clearing);
                }
                layout.Paint(camp.Gate, MapSurface.Road);
                layout.Map.Camps.Add(camp.center);
            }
            layout.Paint(arrival, MapSurface.Road);
            layout.Validate();
            return layout;
        }
    }
}
