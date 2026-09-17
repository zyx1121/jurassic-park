using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Authoritative world state and the fixed-step loop that changes it.
    /// One tick is: run every system in order, then commit. Commit applies queued removals and
    /// publishes the events raised during the tick. Nothing outside the simulation assembly mutates state.
    /// </summary>
    public sealed class World
    {
        private readonly Dictionary<EntityId, Entity> byId = new Dictionary<EntityId, Entity>();
        private readonly List<Entity> ordered = new List<Entity>();
        private readonly List<Entity> pendingAdds = new List<Entity>();
        private readonly List<EntityId> pendingRemovals = new List<EntityId>();
        private readonly List<ISimSystem> systems = new List<ISimSystem>();
        private List<SimEvent> raised = new List<SimEvent>();
        private List<SimEvent> committed = new List<SimEvent>();
        private long nextEntityId = 1;
        private float accumulator;
        private bool ticking;

        public SimConfig Config { get; }
        public SimRandom Random { get; }

        /// <summary>Number of committed ticks.</summary>
        public long Tick { get; private set; }

        /// <summary>Simulated seconds elapsed, derived from the tick count so it never drifts.</summary>
        public double Time => Tick * (double)Config.TickSeconds;

        /// <summary>Entities in spawn order. Stable during a tick: spawns and removals take effect on it at commit.</summary>
        public IReadOnlyList<Entity> Entities => ordered;

        /// <summary>Events published by the most recent commit. Replaced, not appended, on every commit.</summary>
        public IReadOnlyList<SimEvent> Events => committed;

        public World(SimConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Random = new SimRandom(config.Seed);
        }

        public void AddSystem(ISimSystem system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            if (ticking) throw new InvalidOperationException("Systems cannot be added during a tick.");
            systems.Add(system);
        }

        /// <summary>Creates an entity. It resolves through TryGet at once, and joins Entities at the next commit.</summary>
        public Entity Spawn(EntityKind kind, string definitionId, SeatId owner, SimVector2 position)
        {
            if (string.IsNullOrEmpty(definitionId)) throw new ArgumentException("An entity needs a definition id.", nameof(definitionId));
            var entity = new Entity(new EntityId(nextEntityId++), kind, definitionId, owner, position);
            byId.Add(entity.Id, entity);
            pendingAdds.Add(entity);
            Raise(new EntitySpawned(entity.Id, kind, owner));
            return entity;
        }

        /// <summary>Requests removal. The entity stops being alive now and leaves the world at the next commit. Returns false if it is unknown or already leaving.</summary>
        public bool Despawn(EntityId id, string reason)
        {
            if (!byId.TryGetValue(id, out Entity entity) || !entity.IsAlive) return false;
            entity.IsAlive = false;
            pendingRemovals.Add(id);
            Raise(new EntityRemoved(id, reason ?? string.Empty));
            return true;
        }

        public bool TryGet(EntityId id, out Entity entity) => byId.TryGetValue(id, out entity);

        /// <summary>True when the id resolves to an entity that has not been asked to leave.</summary>
        public bool IsAlive(EntityId id) => byId.TryGetValue(id, out Entity entity) && entity.IsAlive;

        /// <summary>Queues an event for the next commit. Simulation code only.</summary>
        public void Raise(SimEvent simEvent)
        {
            if (simEvent == null) throw new ArgumentNullException(nameof(simEvent));
            raised.Add(simEvent);
        }

        /// <summary>Converts elapsed real time into whole fixed steps and keeps the remainder. Returns the number of ticks run.</summary>
        public int Advance(float elapsedSeconds)
        {
            if (elapsedSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            accumulator += elapsedSeconds;
            int steps = 0;
            while (accumulator >= Config.TickSeconds && steps < Config.MaxStepsPerAdvance)
            {
                accumulator -= Config.TickSeconds;
                Step();
                steps++;
            }
            // Drop time the host could not catch up on instead of spiralling.
            if (steps == Config.MaxStepsPerAdvance && accumulator > Config.TickSeconds) accumulator = 0f;
            return steps;
        }

        /// <summary>Runs exactly one tick.</summary>
        public void Step()
        {
            if (ticking) throw new InvalidOperationException("Step is not re-entrant.");
            ticking = true;
            try
            {
                for (int i = 0; i < systems.Count; i++) systems[i].Tick(this);
            }
            finally
            {
                ticking = false;
            }
            Tick++;
            Commit();
        }

        /// <summary>Applies queued structure changes and publishes raised events. Step calls it; setup code may call it once after placing the initial entities.</summary>
        public void Commit()
        {
            if (ticking) throw new InvalidOperationException("Commit cannot run inside a tick.");
            if (pendingAdds.Count > 0)
            {
                ordered.AddRange(pendingAdds);
                pendingAdds.Clear();
            }
            if (pendingRemovals.Count > 0)
            {
                for (int i = 0; i < pendingRemovals.Count; i++) byId.Remove(pendingRemovals[i]);
                ordered.RemoveAll(e => !e.IsAlive);
                pendingRemovals.Clear();
            }
            // A fresh list per commit: a consumer still holding the previous Events must never see the next tick filling up.
            committed = raised;
            raised = new List<SimEvent>();
            for (int i = 0; i < committed.Count; i++) committed[i].Tick = Tick;
        }
    }
}
