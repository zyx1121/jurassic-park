using System;

namespace JurassicPark.Simulation
{
    /// <summary>Continuous ground-plane position in metres. The simulation assembly has no engine references, so it carries its own vector.</summary>
    public readonly struct SimVector2 : IEquatable<SimVector2>
    {
        public static readonly SimVector2 Zero = new SimVector2(0f, 0f);

        public float X { get; }
        public float Y { get; }

        public SimVector2(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float Length => (float)Math.Sqrt(X * X + Y * Y);

        public static float Distance(SimVector2 a, SimVector2 b) => (a - b).Length;

        public static SimVector2 operator +(SimVector2 a, SimVector2 b) => new SimVector2(a.X + b.X, a.Y + b.Y);
        public static SimVector2 operator -(SimVector2 a, SimVector2 b) => new SimVector2(a.X - b.X, a.Y - b.Y);
        public static SimVector2 operator *(SimVector2 a, float s) => new SimVector2(a.X * s, a.Y * s);

        public bool Equals(SimVector2 other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is SimVector2 other && Equals(other);
        public override int GetHashCode() => unchecked(X.GetHashCode() * 397 ^ Y.GetHashCode());
        public override string ToString() => $"({X:0.##}, {Y:0.##})";
    }
}
