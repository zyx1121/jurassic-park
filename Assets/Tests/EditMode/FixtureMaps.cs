using System;
using System.Collections.Generic;
using JurassicPark.Simulation;

namespace JurassicPark.Tests.EditMode
{
    /// <summary>
    /// Hand drawn maps for the map and pathfinding tests. The main one is the smallest layout that still has the
    /// relationships the original map is built on: a camp closed in by cliffs, one short entrance, one long way round,
    /// open ground outside and a resource spot out there, so a test can show the difference between walking around and
    /// breaking through instead of asserting on an open field where every route is the same.
    /// </summary>
    public static class FixtureMaps
    {
        /// <summary>Metres per cell in the fixtures. Not one, so a test that confuses cells with metres fails.</summary>
        public const float CellSize = 2f;

        /// <summary>The camp's short entrance, the gap in the east cliff line.</summary>
        public static readonly Cell MainEntrance = new Cell(12, 5);

        /// <summary>The camp's far gate on the north cliff line, reachable only by walking around the whole camp.</summary>
        public static readonly Cell DetourGate = new Cell(7, 8);

        /// <summary>Open ground inside the camp, where a depot would stand.</summary>
        public static readonly Cell CampGround = new Cell(6, 5);

        /// <summary>A gather spot outside the camp, on ground that cannot be built on.</summary>
        public static readonly Cell ResourceSpot = new Cell(14, 1);

        private static readonly string[] CampValleyRows =
        {
            "################", // y = 11
            "#..............#", // y = 10
            "#..............#", // y = 9
            "#..####D#####..#", // y = 8, D is the far gate
            "#..#........#..#", // y = 7
            "#..#........#..#", // y = 6
            "#..#........E..#", // y = 5, E is the main entrance
            "#..#........#..#", // y = 4
            "#..#........#..#", // y = 3
            "#..##########..#", // y = 2
            "#.............,#", // y = 1, the comma is the resource spot: walkable, not buildable
            "################"  // y = 0
        };

        /// <summary>The camp valley: one camp with two entrances, three event regions, everything connected.</summary>
        public static MapDefinition CampValley()
        {
            var camps = new List<CampDefinition>
            {
                new CampDefinition(
                    "camp-valley",
                    "Valley Camp",
                    new CellBounds(4, 3, 11, 7),
                    new[] { MainEntrance, DetourGate })
            };

            var regions = new List<RegionDefinition>
            {
                new RegionDefinition("spawn-east", RegionKind.DinosaurSpawn, new CellBounds(13, 1, 14, 3)),
                new RegionDefinition("evac-north", RegionKind.Evacuation, new CellBounds(1, 9, 2, 10)),
                new RegionDefinition("supply-camp", RegionKind.Supply, new CellBounds(5, 4, 6, 6))
            };

            return FromRows(CampValleyRows, camps, regions);
        }

        public static GridMap CampValleyGrid() => new GridMap(CampValley());

        /// <summary>A map with nothing on it, for the cost and budget tests where terrain must not be the reason for the answer.</summary>
        public static MapDefinition OpenGround(int width, int height)
        {
            var rows = new string[height];
            for (int y = 0; y < height; y++) rows[y] = new string('.', width);
            return FromRows(rows, Array.Empty<CampDefinition>(), Array.Empty<RegionDefinition>());
        }

        public static GridMap OpenGrid(int width, int height) => new GridMap(OpenGround(width, height));

        public static GridMap GridFromRows(IReadOnlyList<string> rowsTopFirst) =>
            new GridMap(FromRows(rowsTopFirst, Array.Empty<CampDefinition>(), Array.Empty<RegionDefinition>()));

        /// <summary>
        /// Builds a definition from rows written top row first, the way the map reads on screen: '#' is cliff, ',' is
        /// walkable but not buildable, anything else is open buildable ground.
        /// </summary>
        public static MapDefinition FromRows(
            IReadOnlyList<string> rowsTopFirst,
            IReadOnlyList<CampDefinition> camps,
            IReadOnlyList<RegionDefinition> regions)
        {
            if (rowsTopFirst == null || rowsTopFirst.Count == 0) throw new ArgumentException("A fixture map needs rows.", nameof(rowsTopFirst));
            int height = rowsTopFirst.Count;
            int width = rowsTopFirst[0].Length;
            var flags = new CellFlags[width * height];
            for (int y = 0; y < height; y++)
            {
                string row = rowsTopFirst[height - 1 - y];
                if (row.Length != width) throw new ArgumentException($"Row {y} is {row.Length} cells wide, expected {width}.", nameof(rowsTopFirst));
                for (int x = 0; x < width; x++)
                {
                    char symbol = row[x];
                    flags[y * width + x] = symbol == '#'
                        ? CellFlags.None
                        : symbol == ','
                            ? CellFlags.Walkable
                            : CellFlags.Walkable | CellFlags.Buildable;
                }
            }

            return new MapDefinition(width, height, CellSize, flags, camps, regions);
        }
    }
}
