using System;

namespace JurassicPark.Simulation
{
    /// <summary>Content data for one kind of thing, keyed by the definition id entities carry. Filled from the original map's object data, never from literals in code.</summary>
    public sealed class EntityDefinition
    {
        public string Id { get; }

        /// <summary>Ground speed in metres per second. Zero for anything that cannot move.</summary>
        public float MoveSpeed { get; }

        /// <summary>Units of goods it can hold: a unit's pack or a depot's store. Zero for none.</summary>
        public int StorageCapacity { get; }

        /// <summary>Whether other seats' units may put goods in and take them out. True for a depot, false for a unit's own pack.</summary>
        public bool IsDepot { get; }

        /// <summary>Seconds of work to gather one unit from a node. Zero for something that cannot gather.</summary>
        public float GatherSecondsPerUnit { get; }

        /// <summary>For a resource node: what it yields, and how much it starts with. Null for anything else.</summary>
        public string NodeResource { get; }
        public int NodeAmount { get; }

        public EntityDefinition(string id, float moveSpeed, int storageCapacity = 0, bool isDepot = false,
            float gatherSecondsPerUnit = 0f, string nodeResource = null, int nodeAmount = 0)
        {
            if (storageCapacity < 0) throw new ArgumentOutOfRangeException(nameof(storageCapacity));
            if (!(gatherSecondsPerUnit >= 0f) || float.IsInfinity(gatherSecondsPerUnit)) throw new ArgumentOutOfRangeException(nameof(gatherSecondsPerUnit));
            if (nodeAmount < 0) throw new ArgumentOutOfRangeException(nameof(nodeAmount));
            StorageCapacity = storageCapacity;
            IsDepot = isDepot;
            GatherSecondsPerUnit = gatherSecondsPerUnit;
            NodeResource = nodeResource;
            NodeAmount = nodeAmount;
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A definition needs an id.", nameof(id));
            if (!(moveSpeed >= 0f) || float.IsInfinity(moveSpeed)) throw new ArgumentOutOfRangeException(nameof(moveSpeed));
            Id = id;
            MoveSpeed = moveSpeed;
        }
    }
}
