using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>A thing was hurt. Damage is applied by the authority only; views show it.</summary>
    public sealed class HealthChanged : SimEvent
    {
        public EntityId Entity { get; }
        public int Health { get; }
        public int MaxHealth { get; }
        public int Delta { get; }

        public HealthChanged(EntityId entity, int health, int maxHealth, int delta)
        {
            Entity = entity;
            Health = health;
            MaxHealth = maxHealth;
            Delta = delta;
        }
    }

    /// <summary>Hit points of everything that has them. Reaching zero removes the entity; what happens to its goods or cells is other systems' business, triggered by that removal.</summary>
    public sealed class Vitals
    {
        private sealed class Record { public int Health; public int Max; }

        private readonly World world;
        private readonly Dictionary<EntityId, Record> records = new Dictionary<EntityId, Record>();

        public Vitals(World world) => this.world = world ?? throw new ArgumentNullException(nameof(world));

        public void Attach(Entity entity, EntityDefinition definition) => Attach(entity, definition, definition.MaxHealth);

        /// <summary>Attaches with a different maximum, for a site that has only a fraction of the finished building's hit points.</summary>
        public void Attach(Entity entity, EntityDefinition definition, int maxHealth)
        {
            if (definition.MaxHealth < 1 || maxHealth < 1 || records.ContainsKey(entity.Id)) return;
            records.Add(entity.Id, new Record { Health = maxHealth, Max = maxHealth });
        }

        public bool TryGet(EntityId id, out int health, out int max)
        {
            bool found = records.TryGetValue(id, out Record record);
            health = found ? record.Health : 0;
            max = found ? record.Max : 0;
            return found;
        }

        /// <summary>Takes hit points away. Returns the damage actually dealt; the entity leaves the world when it reaches zero.</summary>
        public int Damage(EntityId id, int amount, string cause)
        {
            if (amount < 1 || !records.TryGetValue(id, out Record record) || !world.IsAlive(id)) return 0;
            int dealt = Math.Min(amount, record.Health);
            record.Health -= dealt;
            world.Raise(new HealthChanged(id, record.Health, record.Max, -dealt));
            if (record.Health == 0)
            {
                records.Remove(id);
                world.Despawn(id, cause ?? "destroyed");
            }
            return dealt;
        }

        public int Heal(EntityId id, int amount)
        {
            if (amount < 1 || !records.TryGetValue(id, out Record record) || !world.IsAlive(id)) return 0;
            int healed = Math.Min(amount, record.Max - record.Health);
            if (healed == 0) return 0;
            record.Health += healed;
            world.Raise(new HealthChanged(id, record.Health, record.Max, healed));
            return healed;
        }

        internal void Forget(EntityId id) => records.Remove(id);

        private readonly List<EntityId> gone = new List<EntityId>();

        /// <summary>Drops the records of anything that stopped existing by other means than damage.</summary>
        internal void Sweep(World tickedWorld)
        {
            gone.Clear();
            foreach (KeyValuePair<EntityId, Record> pair in records)
                if (!tickedWorld.IsAlive(pair.Key)) gone.Add(pair.Key);
            for (int i = 0; i < gone.Count; i++) records.Remove(gone[i]);
        }
    }
}
