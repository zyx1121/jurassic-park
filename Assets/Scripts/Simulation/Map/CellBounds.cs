using System;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Inclusive rectangle of cells. Camps and event regions in the original map are rectangles, and both ends belong
    /// to the area: a single cell region has Min equal to Max instead of an empty rectangle nobody can stand in.
    /// </summary>
    public readonly struct CellBounds : IEquatable<CellBounds>
    {
        public Cell Min { get; }
        public Cell Max { get; }

        public CellBounds(Cell min, Cell max)
        {
            if (max.X < min.X || max.Y < min.Y) throw new ArgumentException("Cell bounds are inclusive, so Max cannot be below Min.", nameof(max));
            Min = min;
            Max = max;
        }

        public CellBounds(int minX, int minY, int maxX, int maxY)
            : this(new Cell(minX, minY), new Cell(maxX, maxY))
        {
        }

        public int MinX => Min.X;
        public int MinY => Min.Y;
        public int MaxX => Max.X;
        public int MaxY => Max.Y;

        public int Width => Max.X - Min.X + 1;
        public int Height => Max.Y - Min.Y + 1;

        public bool Contains(Cell cell) => cell.X >= Min.X && cell.X <= Max.X && cell.Y >= Min.Y && cell.Y <= Max.Y;

        public bool Equals(CellBounds other) => Min.Equals(other.Min) && Max.Equals(other.Max);
        public override bool Equals(object obj) => obj is CellBounds other && Equals(other);
        public override int GetHashCode() => unchecked(Min.GetHashCode() * 397 ^ Max.GetHashCode());
        public override string ToString() => $"[{Min.X},{Min.Y}..{Max.X},{Max.Y}]";
    }
}
