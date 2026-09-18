using System.Collections.Generic;
using JurassicPark.Simulation;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Presentation
{
    /// <summary>What a right-click means. Pure, reads only the read model, so the rule is the same on a host and on a client and is tested without a mouse.</summary>
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
        /// On a resource node with stock: gather. On a ground pile: pick up. On a building site of ours or an ally's: help build it.
        /// On a gate we may use: open or close it. On a depot the local seat may use, while carrying something: deliver.
        /// Anything else, including a depot with empty hands or a store that is not ours to use: walk there.
        /// This only chooses what to ask for; the authority decides whether it is allowed.
        /// </summary>
        public static Order Resolve(MatchReadModel model, IReadOnlyList<EntityId> selection, EntitySnapshot? target, SimVector2 point)
        {
            if (!target.HasValue) return new Order(CommandKind.Move, EntityId.None, point);
            EntitySnapshot t = target.Value;
            // A remembered thing may be long gone: as in the original, the order is a walk to where it was last seen.
            if (t.Remembered) return new Order(CommandKind.Move, EntityId.None, t.Position);
            if (t.Kind == EntityKind.ResourceNode && t.NodeRemaining > 0) return new Order(CommandKind.Gather, t.Id, t.Position);
            if (t.Kind == EntityKind.GroundPile) return new Order(CommandKind.Pickup, t.Id, t.Position);
            if (!model.Match.Helicopter.IsNone && t.Id == model.Match.Helicopter) return new Order(CommandKind.Board, t.Id, t.Position);
            EntityCatalogAsset.Entry entry = model.EntryOf(t);
            bool ours = model.LocalMayUsePropertyOf(t.Owner);
            if (t.IsSite && ours) return new Order(CommandKind.Build, t.Id, t.Position);
            if (entry != null && entry.isGate && ours) return new Order(CommandKind.ToggleGate, t.Id, t.Position);
            if (entry != null && entry.isDepot && ours && AnyCarrying(model, selection))
                return new Order(CommandKind.Deliver, t.Id, t.Position);
            return new Order(CommandKind.Move, EntityId.None, t.Position);
        }

        private static bool AnyCarrying(MatchReadModel model, IReadOnlyList<EntityId> selection)
        {
            for (int i = 0; i < selection.Count; i++)
                if (model.TryGet(selection[i], out EntitySnapshot unit) && unit.PackTotal > 0) return true;
            return false;
        }
    }
}
