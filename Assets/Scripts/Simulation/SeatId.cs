using System;

namespace JurassicPark.Simulation
{
    /// <summary>Identifies one seat in a match. A seat is driven by a human or by a computer ally and owns its units, camp and depots.</summary>
    public readonly struct SeatId : IEquatable<SeatId>
    {
        /// <summary>Entities that belong to no seat, such as terrain resources and wild dinosaurs.</summary>
        public static readonly SeatId None = new SeatId(0);

        public int Value { get; }

        public SeatId(int value)
        {
            if (value < 0) throw new ArgumentOutOfRangeException(nameof(value), "Seat ids are zero (none) or positive.");
            Value = value;
        }

        public bool IsNone => Value == 0;

        public bool Equals(SeatId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is SeatId other && Equals(other);
        public override int GetHashCode() => Value;
        public override string ToString() => IsNone ? "Seat(none)" : $"Seat({Value})";

        public static bool operator ==(SeatId a, SeatId b) => a.Equals(b);
        public static bool operator !=(SeatId a, SeatId b) => !a.Equals(b);
    }
}
