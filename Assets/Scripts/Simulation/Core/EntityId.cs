using System;

namespace JurassicPark.Simulation
{
    /// <summary>Stable identity of a simulated entity. Ids are issued once per world and never reused, so a stale reference can never resolve to a newer entity.</summary>
    public readonly struct EntityId : IEquatable<EntityId>
    {
        public static readonly EntityId None = new EntityId(0);

        public long Value { get; }

        public EntityId(long value) => Value = value;

        public bool IsNone => Value == 0;

        public bool Equals(EntityId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is EntityId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => IsNone ? "Entity(none)" : $"Entity({Value})";

        public static bool operator ==(EntityId a, EntityId b) => a.Equals(b);
        public static bool operator !=(EntityId a, EntityId b) => !a.Equals(b);
    }
}
