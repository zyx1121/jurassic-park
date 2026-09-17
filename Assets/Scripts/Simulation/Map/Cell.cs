using System;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Integer coordinate of one pathing cell. The map keeps terrain, occupancy and pathing on this grid while units
    /// still move on continuous <see cref="SimVector2"/> positions, so a cell is an index into the map, not a position.
    /// </summary>
    public readonly struct Cell : IEquatable<Cell>
    {
        public static readonly Cell Zero = new Cell(0, 0);

        public int X { get; }
        public int Y { get; }

        public Cell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(Cell other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is Cell other && Equals(other);
        public override int GetHashCode() => unchecked(X * 397 ^ Y);
        public override string ToString() => $"Cell({X}, {Y})";

        public static bool operator ==(Cell a, Cell b) => a.Equals(b);
        public static bool operator !=(Cell a, Cell b) => !a.Equals(b);
    }
}
