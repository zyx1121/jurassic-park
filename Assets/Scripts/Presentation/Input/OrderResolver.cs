using System.Collections.Generic;
using JurassicPark.Simulation;

namespace JurassicPark.Presentation
{
    /// <summary>What a right-click means. Pure, so the rule is tested without a mouse.</summary>
    public static class OrderResolver
    {
        public readonly struct Order
        {
            public CommandKind Kind { get; }
            public EntityId Target { get; }
            public SimVector2 Point { get; }

            public Order(CommandKind kind, EntityId target, SimVector2 point)
            {
                Kind = kind;
                Target = target;
                Point = point;
            }
        }

        /// <summary>
        /// On a resource node: gather. On a ground pile: pick up. On an open depot while carrying something: deliver.
        /// Anything else, including a depot with empty hands or a store that is not ours to use: walk there.
        /// </summary>
        public static Order Resolve(SimulationRuntime runtime, IReadOnlyList<EntityId> selection, Entity target, SimVector2 point)
        {
            if (target != null && target.IsAlive)
            {
                TaskContext context = runtime.Tasks.Context;
                if (runtime.Logistics.TryGetNode(target.Id, out ResourceNode node) && node.Remaining > 0)
                    return new Order(CommandKind.Gather, target.Id, target.Position);
                if (LogisticsQueries.IsOpenStore(context, runtime.LocalSeat, target))
                {
                    if (target.Kind == EntityKind.GroundPile) return new Order(CommandKind.Pickup, target.Id, target.Position);
                    if (AnyCarrying(runtime, selection)) return new Order(CommandKind.Deliver, target.Id, target.Position);
                }
                return new Order(CommandKind.Move, EntityId.None, target.Position);
            }
            return new Order(CommandKind.Move, EntityId.None, point);
        }

        private static bool AnyCarrying(SimulationRuntime runtime, IReadOnlyList<EntityId> selection)
        {
            for (int i = 0; i < selection.Count; i++)
                if (runtime.Logistics.TryGetContainer(selection[i], out Container pack) && pack.Total > 0) return true;
            return false;
        }
    }
}
