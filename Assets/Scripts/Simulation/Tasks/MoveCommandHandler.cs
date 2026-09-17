using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>Turns a Move command into one MoveTask per living actor.</summary>
    public sealed class MoveCommandHandler : ICommandHandler
    {
        private readonly TaskSystem tasks;

        public MoveCommandHandler(TaskSystem tasks)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public CommandRejection Validate(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            GridMap map = tasks.Context.Map;
            if (!map.InBounds(map.CellAt(command.TargetPosition))) return CommandRejection.InvalidTarget;
            // Refuse only when nobody in the selection can move; a depot caught in a box select must not void the order.
            for (int i = 0; i < livingActors.Count; i++)
                if (CanMove(livingActors[i])) return CommandRejection.None;
            return CommandRejection.ActorsLackAbility;
        }

        public void Execute(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            for (int i = 0; i < livingActors.Count; i++)
                if (CanMove(livingActors[i])) tasks.Assign(livingActors[i].Id, new MoveTask(command.TargetPosition), command.Mode);
        }

        private bool CanMove(Entity actor) =>
            tasks.Context.Catalog.TryGet(actor.DefinitionId, out EntityDefinition definition) && definition.MoveSpeed > 0f;
    }
}
