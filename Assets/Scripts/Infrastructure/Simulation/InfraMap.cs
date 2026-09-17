using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Infrastructure
{
    public enum InfraTileKind { Ground, Cliff, Water }

    public sealed class InfraMap
    {
        public readonly int Width;
        public readonly int Height;
        public readonly float CellSize;
        public List<Vector2Int> Camps { get; } = new List<Vector2Int>();

        readonly InfraTileKind[,] tiles;
        readonly float[,] elevations;
        internal int Revision { get; private set; }

        public InfraMap(int width, int height, float cellSize)
        {
            if (width <= 0 || height <= 0 || !Finite(cellSize) || cellSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), "Map dimensions and cell size must be positive.");
            Width = width;
            Height = height;
            CellSize = cellSize;
            tiles = new InfraTileKind[width, height];
            elevations = new float[width, height];
        }

        public bool Contains(Vector2Int cell) =>
            cell.x >= 0 && cell.y >= 0 && cell.x < Width && cell.y < Height;

        public void SetTile(Vector2Int cell, InfraTileKind kind, float elevation = 0f)
        {
            CheckCell(cell);
            if (!Enum.IsDefined(typeof(InfraTileKind), kind) || !Finite(elevation))
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (tiles[cell.x, cell.y] == kind && elevations[cell.x, cell.y] == elevation)
                return;
            tiles[cell.x, cell.y] = kind;
            elevations[cell.x, cell.y] = elevation;
            Revision++;
        }

        public InfraTileKind TileAt(Vector2Int cell)
        {
            CheckCell(cell);
            return tiles[cell.x, cell.y];
        }

        public float ElevationAt(Vector2Int cell)
        {
            CheckCell(cell);
            return elevations[cell.x, cell.y];
        }

        public Vector2 CellCenter(Vector2Int cell)
        {
            CheckCell(cell);
            return new Vector2((cell.x + .5f - Width * .5f) * CellSize,
                (cell.y + .5f - Height * .5f) * CellSize);
        }

        public Vector2Int WorldToCell(Vector2 position)
        {
            if (!Finite(position.x) || !Finite(position.y))
                throw new ArgumentOutOfRangeException(nameof(position));
            return new Vector2Int(Mathf.FloorToInt(position.x / CellSize + Width * .5f),
                Mathf.FloorToInt(position.y / CellSize + Height * .5f));
        }

        void CheckCell(Vector2Int cell)
        {
            if (!Contains(cell))
                throw new ArgumentOutOfRangeException(nameof(cell), cell, "Cell is outside the map.");
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
