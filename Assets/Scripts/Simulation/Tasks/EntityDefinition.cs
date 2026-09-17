using System;

namespace JurassicPark.Simulation
{
    /// <summary>Content data for one kind of thing, keyed by the definition id entities carry. Filled from the original map's object data, never from literals in code.</summary>
    public sealed class EntityDefinition
    {
        public string Id { get; }

        /// <summary>Ground speed in metres per second. Zero for anything that cannot move.</summary>
        public float MoveSpeed { get; }

        public EntityDefinition(string id, float moveSpeed)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A definition needs an id.", nameof(id));
            if (!(moveSpeed >= 0f) || float.IsInfinity(moveSpeed)) throw new ArgumentOutOfRangeException(nameof(moveSpeed));
            Id = id;
            MoveSpeed = moveSpeed;
        }
    }
}
