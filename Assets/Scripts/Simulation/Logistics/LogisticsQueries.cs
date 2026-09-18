using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>Questions the hauling tasks and their command handlers both ask, kept in one place so validation and execution cannot disagree.</summary>
    public static class LogisticsQueries
    {
        /// <summary>The cells a thing stands on: its blocking footprint, or the single cell under it when it does not block.</summary>
        public static IReadOnlyList<Cell> FootprintOf(TaskContext context, Entity entity)
        {
            IReadOnlyList<Cell> footprint = context.Map.FootprintOf(entity.Id);
            return footprint.Count > 0 ? footprint : new[] { context.Map.CellAt(entity.Position) };
        }

        public static bool IsBeside(TaskContext context, Entity actor, Entity target) =>
            Mover.IsBeside(context.Map, context.Map.CellAt(actor.Position), FootprintOf(context, target));

        /// <summary>True when the actor has a pack at all.</summary>
        public static bool CanCarry(TaskContext context, Entity actor) =>
            context.Logistics != null && context.Logistics.TryGetContainer(actor.Id, out _);

        public static bool CanGather(TaskContext context, Entity actor) =>
            CanCarry(context, actor) && context.Catalog.TryGet(actor.DefinitionId, out EntityDefinition definition) && definition.GatherSecondsPerUnit > 0f;

        /// <summary>
        /// True when the seat may put goods into or take goods out of the target: a ground pile, or a depot that is its own, an ally's or unowned.
        /// A unit's pack is never open to others.
        /// </summary>
        public static bool IsOpenStore(TaskContext context, SeatId seat, Entity target)
        {
            if (target == null || !target.IsAlive || context.Logistics == null || !context.Logistics.TryGetContainer(target.Id, out _)) return false;
            if (target.Kind == EntityKind.GroundPile) return true;
            if (!context.Catalog.TryGet(target.DefinitionId, out EntityDefinition definition) || !definition.IsDepot) return false;
            return context.Seats == null ? target.Owner == seat || target.Owner.IsNone : context.Seats.MayUsePropertyOf(seat, target.Owner);
        }

        /// <summary>
        /// The nearest usable depot with room, by straight-line distance, ties going to the older entity. Straight-line on purpose:
        /// it is cheap and deterministic, and an unreachable pick is reported by the walk and excluded on the next try.
        /// </summary>
        /// <summary>The nearest depot with room, the actor's own seat's first: an ally's depot takes the haul only when the seat has none with room.</summary>
        public static Entity NearestDepotWithRoom(TaskContext context, Entity actor, ICollection<EntityId> excluded)
        {
            Entity best = null;
            float bestDistance = float.MaxValue;
            bool bestIsOwn = false;
            IReadOnlyList<Entity> entities = context.World.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity candidate = entities[i];
                if (candidate.Kind != EntityKind.Building || excluded.Contains(candidate.Id)) continue;
                if (!IsOpenStore(context, actor.Owner, candidate)) continue;
                context.Logistics.TryGetContainer(candidate.Id, out Container store);
                if (store.FreeCapacity < 1) continue;
                bool own = candidate.Owner == actor.Owner;
                if (bestIsOwn && !own) continue;
                float distance = SimVector2.Distance(actor.Position, candidate.Position);
                if ((own && !bestIsOwn) || distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                    bestIsOwn = own;
                }
            }
            return best;
        }
    }
}
