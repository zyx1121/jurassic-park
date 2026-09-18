using System;
using System.Collections.Generic;

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

        /// <summary>Cells it stands on, anchored at its placement cell. 1x1 for a unit.</summary>
        public int FootprintWidth { get; }
        public int FootprintHeight { get; }

        /// <summary>Whether it takes its cells out of the walkable grid, and whether something that wants through may break it.</summary>
        public bool Blocks { get; }
        public bool Destructible { get; }

        /// <summary>Hit points. Zero for things that cannot be hurt.</summary>
        public int MaxHealth { get; }

        /// <summary>Materials a builder must bring before this can be built, and how many seconds of one builder's work it takes. Null for anything not built.</summary>
        public IReadOnlyDictionary<string, int> BuildCost { get; }
        public float BuildWorkSeconds { get; }

        /// <summary>A gate can be opened, which frees its cells, and closed, which blocks them again.</summary>
        public bool IsGate { get; }

        /// <summary>Damage per hit and seconds between hits. Zero damage for anything that cannot attack.</summary>
        public int AttackDamage { get; }
        public float AttackSeconds { get; }

        /// <summary>How far, in metres, it notices enemies on its own. Zero for anything that only acts on orders.</summary>
        public float PerceptionRadius { get; }

        /// <summary>Whether it will break through destructible blockers when nothing else leads to its target.</summary>
        public bool CanBreach { get; }

        public bool IsBuildable => BuildCost != null;
        public bool CanAttack => AttackDamage > 0;

        public EntityDefinition(string id, float moveSpeed, int storageCapacity = 0, bool isDepot = false,
            float gatherSecondsPerUnit = 0f, string nodeResource = null, int nodeAmount = 0,
            int footprintWidth = 1, int footprintHeight = 1, bool blocks = false, bool destructible = true, int maxHealth = 0,
            IReadOnlyDictionary<string, int> buildCost = null, float buildWorkSeconds = 0f, bool isGate = false,
            int attackDamage = 0, float attackSeconds = 1f, float perceptionRadius = 0f, bool canBreach = false)
        {
            if (attackDamage < 0 || !(attackSeconds > 0f) || !(perceptionRadius >= 0f)) throw new ArgumentOutOfRangeException(nameof(attackDamage));
            AttackDamage = attackDamage;
            AttackSeconds = attackSeconds;
            PerceptionRadius = perceptionRadius;
            CanBreach = canBreach;
            if (footprintWidth < 1 || footprintHeight < 1) throw new ArgumentOutOfRangeException(nameof(footprintWidth));
            if (maxHealth < 0) throw new ArgumentOutOfRangeException(nameof(maxHealth));
            if (buildCost != null && !(buildWorkSeconds > 0f)) throw new ArgumentOutOfRangeException(nameof(buildWorkSeconds), "A buildable thing needs work time.");
            FootprintWidth = footprintWidth;
            FootprintHeight = footprintHeight;
            Blocks = blocks;
            Destructible = destructible;
            MaxHealth = maxHealth;
            BuildCost = buildCost;
            BuildWorkSeconds = buildWorkSeconds;
            IsGate = isGate;
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
