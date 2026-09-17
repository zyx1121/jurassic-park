using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Authoritative world state and the fixed-step loop that changes it.
    /// One tick is: advance the tick number, run every system in order, then commit. Commit applies queued
    /// removals and publishes the events raised during the tick. Nothing outside the simulation assembly mutates state.
    /// </summary>
    public sealed class World
    {
        private readonly Dictionary<EntityId, Entity> byId = new Dictionary<EntityId, Entity>();
        private readonly List<Entity> ordered = new List<Entity>();
        private readonly IReadOnlyList<Entity> orderedView;
        private readonly List<Entity> pendingAdds = new List<Entity>();
        private readonly List<EntityId> pendingRemovals = new List<EntityId>();
        private readonly List<ISimSystem> systems = new List<ISimSystem>();
        private readonly List<SimEvent> raised = new List<SimEvent>();
        private List<SimEvent> batch = new List<SimEvent>();
        private long nextEntityId = 1;
        private double accumulator;
        private bool ticking;

        public SimConfig Config { get; }
        public SimRandom Random { get; }

        /// <summary>
        /// Number of the tick being simulated, or of the last one committed when idle. It is advanced before systems run,
        /// so a deadline computed inside a system and the events that system raises carry the same tick.
        /// </summary>
        public long Tick { get; private set; }

        /// <summary>Simulated seconds at the end of the current tick. Derived from the tick count, so it never drifts.</summary>
        public double Time => Tick / (double)Config.TicksPerSecond;

        /// <summary>Set when a system threw. The world is torn mid-tick and refuses to simulate further; the match is over.</summary>
        public bool IsFaulted { get; private set; }

        /// <summary>Entities in spawn order. Stable during a tick: spawns and removals take effect on it at commit. Read-only view.</summary>
        public IReadOnlyList<Entity> Entities => orderedView;

        /// <summary>Number of committed events waiting to be drained.</summary>
        public int PendingEventCount => batch.Count;

        public World(SimConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Random = new SimRandom(config.Seed);
            orderedView = ordered.AsReadOnly();
        }

        public void AddSystem(ISimSystem system)
        {
            if (system == null) throw new ArgumentNullException(nameof(system));
            ThrowIfFaulted();
            if (ticking) throw new InvalidOperationException("Systems cannot be added during a tick.");
            systems.Add(system);
        }

        /// <summary>Creates an entity. It resolves through TryGet at once, and joins Entities at the next commit.</summary>
        public Entity Spawn(EntityKind kind, string definitionId, SeatId owner, SimVector2 position)
        {
            ThrowIfFaulted();
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
            ThrowIfFaulted();
            if (!byId.TryGetValue(id, out Entity entity) || !entity.IsAlive) return false;
            entity.MarkRemoved();
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
            ThrowIfFaulted();
            raised.Add(simEvent);
        }

        /// <summary>
        /// Hands over every event committed since the previous drain, in order and stamped with its tick, and starts a new batch.
        /// The consumer owns the boundary: events are lost only by draining and discarding them, never by setup commits,
        /// by an Advance that runs zero ticks, or by one that runs several. The host loop drains once per frame and fans out.
        /// </summary>
        public IReadOnlyList<SimEvent> DrainEvents()
        {
            if (ticking) throw new InvalidOperationException("Events cannot be drained during a tick.");
            List<SimEvent> drained = batch;
            batch = new List<SimEvent>();
            return drained.AsReadOnly();
        }

        /// <summary>Converts elapsed real time into whole fixed steps and keeps the sub-tick remainder. Returns the number of ticks run.</summary>
        public int Advance(float elapsedSeconds)
        {
            if (!(elapsedSeconds >= 0f) || float.IsInfinity(elapsedSeconds)) throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            ThrowIfFaulted();
            double tickSeconds = Config.TickSeconds;
            accumulator += elapsedSeconds;
            int steps = 0;
            while (accumulator >= tickSeconds && steps < Config.MaxStepsPerAdvance)
            {
                accumulator -= tickSeconds;
                Step();
                steps++;
            }
            // After a stall, drop the whole ticks the host cannot catch up on but keep the phase.
            if (accumulator >= tickSeconds) accumulator %= tickSeconds;
            return steps;
        }

        /// <summary>Runs exactly one tick and appends its events to the undrained batch.</summary>
        public void Step()
        {
            if (ticking) throw new InvalidOperationException("Step is not re-entrant.");
            ThrowIfFaulted();
            ticking = true;
            Tick++;
            try
            {
                for (int i = 0; i < systems.Count; i++) systems[i].Tick(this);
            }
            catch
            {
                // Earlier systems already changed state, so this tick can be neither committed nor cleanly undone.
                IsFaulted = true;
                throw;
            }
            finally
            {
                ticking = false;
            }
            Commit();
        }

        /// <summary>Applies queued structure changes and publishes raised events. Step calls it; setup code may call it once after placing the initial entities.</summary>
        public void Commit()
        {
            if (ticking) throw new InvalidOperationException("Commit cannot run inside a tick.");
            ThrowIfFaulted();
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
            for (int i = 0; i < raised.Count; i++)
            {
                raised[i].Tick = Tick;
                batch.Add(raised[i]);
            }
            raised.Clear();
        }

        private void ThrowIfFaulted()
        {
            if (IsFaulted) throw new InvalidOperationException("The world faulted during a tick and cannot simulate further.");
        }
    }
}
