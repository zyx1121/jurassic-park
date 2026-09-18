using System;
using System.Collections.Generic;
using JurassicPark.Simulation;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// The match as the screen sees it: the latest snapshot, the one before it for interpolation, and who appeared or vanished
    /// between them. Views, selection, picking and the HUD read only this. It never reaches back into the simulation, so a
    /// client that has no simulation runs the same presentation code as the host.
    /// </summary>
    public sealed class MatchReadModel
    {
        private readonly List<EntitySnapshot> entities = new List<EntitySnapshot>();
        private readonly Dictionary<EntityId, int> indexById = new Dictionary<EntityId, int>();
        private readonly Dictionary<EntityId, SimVector2> previousPositions = new Dictionary<EntityId, SimVector2>();
        private readonly List<SeatSnapshot> seats = new List<SeatSnapshot>();
        private readonly List<EntityId> vanished = new List<EntityId>();
        private readonly HashSet<EntityId> seen = new HashSet<EntityId>();
        private readonly List<int> appeared = new List<int>();

        public EntityCatalogAsset Catalog { get; }
        public MapDefinition Map { get; }
        public SeatId LocalSeat { get; set; }
        public long Tick { get; private set; }

        /// <summary>Counts applied snapshots. Views re-read positions when it changes.</summary>
        public int Revision { get; private set; }

        /// <summary>0 to 1: how far real time has run from the latest snapshot towards the next. Set by whoever feeds the model.</summary>
        public Func<float> Fraction { get; set; } = () => 1f;

        public IReadOnlyList<EntitySnapshot> Entities => entities;

        /// <summary>The match's own state. Updated by whoever feeds the model.</summary>
        public MatchSnapshot Match { get; set; }

        /// <summary>The local team's fog. Updated by whoever feeds the model.</summary>
        public FogSnapshot Fog { get; } = new FogSnapshot();
        public IReadOnlyList<SeatSnapshot> Seats => seats;

        public event Action<EntitySnapshot> EntityAppeared;
        public event Action<EntityId> EntityVanished;

        public MatchReadModel(EntityCatalogAsset catalog, MapDefinition map)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Map = map ?? throw new ArgumentNullException(nameof(map));
        }

        public bool TryGet(EntityId id, out EntitySnapshot snapshot)
        {
            if (indexById.TryGetValue(id, out int index))
            {
                snapshot = entities[index];
                return true;
            }
            snapshot = default;
            return false;
        }

        /// <summary>Where the entity was one snapshot ago, or where it is now if it is new.</summary>
        public SimVector2 PreviousPositionOf(EntityId id, SimVector2 fallback) =>
            previousPositions.TryGetValue(id, out SimVector2 previous) ? previous : fallback;

        public EntityCatalogAsset.Entry EntryOf(in EntitySnapshot snapshot) =>
            snapshot.DefinitionIndex < Catalog.entries.Length ? Catalog.entries[snapshot.DefinitionIndex] : null;

        public string DefinitionIdOf(in EntitySnapshot snapshot) => EntryOf(snapshot)?.id ?? "?";

        public int TeamOf(SeatId seat)
        {
            for (int i = 0; i < seats.Count; i++)
                if (seats[i].Id == seat) return seats[i].Team;
            return 0;
        }

        /// <summary>True when the local seat may use a store owned by <paramref name="owner"/>: its own, a team mate's, or an unowned one. The host still has the last word.</summary>
        public bool LocalMayUsePropertyOf(SeatId owner) =>
            !LocalSeat.IsNone && (owner.IsNone || owner == LocalSeat || (TeamOf(owner) != 0 && TeamOf(owner) == TeamOf(LocalSeat)));

        public void SetSeats(IReadOnlyList<SeatSnapshot> next)
        {
            seats.Clear();
            for (int i = 0; i < next.Count; i++) seats.Add(next[i]);
        }

        /// <summary>
        /// Replaces the snapshot. A full snapshot is the whole truth: whoever is not in it is gone. Snapshots older than the one
        /// already shown are ignored, so a late packet can never move the screen backwards.
        /// </summary>
        public bool Apply(long tick, IReadOnlyList<EntitySnapshot> next)
        {
            if (Revision > 0 && tick <= Tick) return false;

            previousPositions.Clear();
            for (int i = 0; i < entities.Count; i++) previousPositions[entities[i].Id] = entities[i].Position;

            seen.Clear();
            for (int i = 0; i < next.Count; i++) seen.Add(next[i].Id);
            vanished.Clear();
            for (int i = 0; i < entities.Count; i++)
                if (!seen.Contains(entities[i].Id)) vanished.Add(entities[i].Id);

            entities.Clear();
            for (int i = 0; i < next.Count; i++) entities.Add(next[i]);
            // Appearances are reported after the list is complete, so a handler can look anything up.
            appeared.Clear();
            for (int i = 0; i < entities.Count; i++)
                if (!indexById.ContainsKey(entities[i].Id)) appeared.Add(i);
            indexById.Clear();
            for (int i = 0; i < entities.Count; i++) indexById[entities[i].Id] = i;

            Tick = tick;
            Revision++;
            for (int i = 0; i < vanished.Count; i++)
            {
                previousPositions.Remove(vanished[i]);
                EntityVanished?.Invoke(vanished[i]);
            }
            for (int i = 0; i < appeared.Count; i++) EntityAppeared?.Invoke(entities[appeared[i]]);
            return true;
        }
    }
}
