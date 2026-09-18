using System.Collections.Generic;
using JurassicPark.Simulation;

namespace JurassicPark.Presentation
{
    /// <summary>Reads the authoritative world into snapshots. Runs on the host only: once for its own screen, and the same list goes on the wire.</summary>
    public static class SnapshotCapture
    {
        /// <summary>
        /// Everything the seat sees right now: its own team's things always, anything else only in a visible cell. What the team
        /// remembers of things it saw earlier is added by <see cref="Entities"/> from a <see cref="SnapshotMemory"/>, never live.
        /// </summary>
        public static bool MaySee(SimulationRuntime runtime, SeatId forSeat, Entity entity)
        {
            Knowledge knowledge = runtime.Knowledge;
            return knowledge == null || knowledge.CanSee(forSeat, entity);
        }

        /// <summary>
        /// The seat's view of the world: live snapshots of what it sees, then, given a memory, the last-seen snapshot of every
        /// building, tree and pile it saw before and cannot see now. A remembered thing is forgotten when its cell is in sight
        /// again and it is not there any more. Units are never remembered, so a unit walking into the fog is gone from the screen.
        /// </summary>
        public static void Entities(SimulationRuntime runtime, EntityCatalogAsset catalog, List<EntitySnapshot> into, SeatId forSeat, SnapshotMemory memory = null)
        {
            into.Clear();
            Knowledge knowledge = runtime.Knowledge;
            Dictionary<EntityId, EntitySnapshot> remembered = memory != null && knowledge != null ? memory.Of(knowledge.TeamOf(forSeat)) : null;
            if (memory != null) memory.SeenThisCapture.Clear();
            IReadOnlyList<Entity> entities = runtime.World.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity entity = entities[i];
                if (!entity.IsAlive || !MaySee(runtime, forSeat, entity)) continue;
                EntitySnapshot snapshot = Capture(runtime, catalog, entity);
                into.Add(snapshot);
                if (remembered == null || entity.Kind == EntityKind.Unit) continue;
                memory.SeenThisCapture.Add(entity.Id);
                if (!knowledge.IsOwnOrAllied(forSeat, entity.Owner)) remembered[entity.Id] = snapshot;
            }
            if (remembered == null) return;
            memory.Forget.Clear();
            foreach (KeyValuePair<EntityId, EntitySnapshot> pair in remembered)
            {
                if (memory.SeenThisCapture.Contains(pair.Key)) continue;
                if (knowledge.At(forSeat, runtime.Map.CellAt(pair.Value.Position)) == Visibility.Visible) memory.Forget.Add(pair.Key);
                else into.Add(pair.Value);
            }
            for (int i = 0; i < memory.Forget.Count; i++) remembered.Remove(memory.Forget[i]);
        }

        private static EntitySnapshot Capture(SimulationRuntime runtime, EntityCatalogAsset catalog, Entity entity)
        {
            {
                var snapshot = new EntitySnapshot
                {
                    Id = entity.Id,
                    DefinitionIndex = IndexOf(catalog, entity.DefinitionId),
                    Kind = entity.Kind,
                    Owner = entity.Owner,
                    Position = entity.Position,
                };
                if (runtime.Logistics.TryGetContainer(entity.Id, out Container pack))
                {
                    snapshot.PackTotal = (ushort)System.Math.Min(pack.Total, ushort.MaxValue);
                    snapshot.PackCapacity = (ushort)System.Math.Min(pack.Capacity, ushort.MaxValue);
                }
                else if (runtime.Logistics.TryGetNode(entity.Id, out ResourceNode node))
                {
                    snapshot.NodeRemaining = node.Remaining;
                }
                snapshot.BuildProgress = 255;
                snapshot.HealthFraction = 255;
                if (runtime.Structures.TryGetSite(entity.Id, out BuildSite site))
                {
                    snapshot.IsSite = true;
                    snapshot.BuildProgress = (byte)System.Math.Round(site.Progress * 254f);
                }
                if (runtime.Vitals.TryGet(entity.Id, out int health, out int maxHealth) && maxHealth > 0)
                    snapshot.HealthFraction = (byte)System.Math.Max(1, System.Math.Ceiling(254f * health / maxHealth));
                snapshot.GateOpen = runtime.Structures.IsGateOpen(entity.Id);
                SimTask task = runtime.Tasks.CurrentOf(entity.Id);
                if (task != null)
                {
                    snapshot.Task = EntitySnapshot.CodeOf(task.Kind);
                    snapshot.TaskState = task.State;
                    snapshot.TaskReason = task.Reason;
                }
                return snapshot;
            }
        }

        public static MatchSnapshot Match(SimulationRuntime runtime, SeatId forSeat)
        {
            MatchFlow match = runtime.Match;
            if (match == null) return new MatchSnapshot { Phase = MatchPhase.Survival, TimeOfDay = 12f };
            return new MatchSnapshot
            {
                Phase = match.Phase,
                SecondsLeft = (float)match.SecondsLeft,
                TimeOfDay = match.Clock.TimeOfDay,
                ModeIndex = (byte)match.ModeIndex,
                Difficulty = (byte)match.Difficulty,
                BoardedByLocal = (ushort)System.Math.Min(match.BoardedOf(forSeat), ushort.MaxValue),
                LocalOutcome = match.OutcomeOf(forSeat),
                Helicopter = match.Helicopter,
            };
        }

        public static void Seats(SimulationRuntime runtime, List<SeatSnapshot> into)
        {
            into.Clear();
            for (int i = 0; i < runtime.Seats.Seats.Count; i++)
            {
                Seat seat = runtime.Seats.Seats[i];
                into.Add(new SeatSnapshot { Id = seat.Id, Team = seat.Team, Controller = seat.Controller });
            }
        }

        private static EntityCatalogAsset indexedCatalog;
        private static readonly Dictionary<string, ushort> indexById = new Dictionary<string, ushort>(System.StringComparer.Ordinal);

        /// <summary>The original map has 162 unit definitions; scanning them for every entity every tick would be a hundred thousand string compares a tick.</summary>
        private static ushort IndexOf(EntityCatalogAsset catalog, string definitionId)
        {
            if (!ReferenceEquals(indexedCatalog, catalog) || indexById.Count != catalog.entries.Length)
            {
                indexedCatalog = catalog;
                indexById.Clear();
                for (int i = 0; i < catalog.entries.Length; i++) indexById[catalog.entries[i].id] = (ushort)i;
            }
            return indexById.TryGetValue(definitionId, out ushort index) ? index : ushort.MaxValue;
        }
    }
}
