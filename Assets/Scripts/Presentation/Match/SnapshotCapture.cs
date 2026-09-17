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

        private static ushort IndexOf(EntityCatalogAsset catalog, string definitionId)
        {
            for (int i = 0; i < catalog.entries.Length; i++)
                if (catalog.entries[i].id == definitionId) return (ushort)i;
            return ushort.MaxValue;
        }
    }
}
