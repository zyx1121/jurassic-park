using System;

namespace JurassicPark.Simulation
{
    public readonly struct ReservationId : IEquatable<ReservationId>
    {
        public static readonly ReservationId None = new ReservationId(0);
        public long Value { get; }
        public ReservationId(long value) => Value = value;
        public bool IsNone => Value == 0;
        public bool Equals(ReservationId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is ReservationId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => IsNone ? "Reservation(none)" : $"Reservation({Value})";
        public static bool operator ==(ReservationId a, ReservationId b) => a.Equals(b);
        public static bool operator !=(ReservationId a, ReservationId b) => !a.Equals(b);
    }
}
