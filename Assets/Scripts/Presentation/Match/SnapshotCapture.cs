using System.Collections.Generic;
using JurassicPark.Simulation;

namespace JurassicPark.Presentation
{
    /// <summary>Reads the authoritative world into snapshots. Runs on the host only: once for its own screen, and the same list goes on the wire.</summary>
    public static class SnapshotCapture
    {
        public static void Entities(SimulationRuntime runtime, EntityCatalogAsset catalog, List<EntitySnapshot> into)
        {
            into.Clear();
            IReadOnlyList<Entity> entities = runtime.World.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity entity = entities[i];
                if (!entity.IsAlive) continue;
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
                into.Add(snapshot);
            }
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
