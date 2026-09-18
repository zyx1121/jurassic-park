using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>Attack: every actor that can fight gets an AttackTask on the target. Only things that can be hurt and are not on the actor's team are targets.</summary>
    public sealed class AttackCommandHandler : ICommandHandler
    {
        private readonly TaskSystem tasks;

        public AttackCommandHandler(TaskSystem tasks) => this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));

        public CommandRejection Validate(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            TaskContext context = tasks.Context;
            if (context.Vitals == null) return CommandRejection.UnsupportedKind;
            if (!world.TryGet(command.TargetEntity, out Entity target) || !target.IsAlive || !context.Vitals.TryGet(target.Id, out _, out _)) return CommandRejection.InvalidTarget;
            if (target.Owner == command.Seat || (context.Seats != null && context.Seats.AreAllied(command.Seat, target.Owner))) return CommandRejection.NotAllowedOnTarget;
            bool anyFighter = false;
            for (int i = 0; i < livingActors.Count; i++)
            {
                if (!CanFight(context, livingActors[i]) || livingActors[i].Id == target.Id) continue;
                anyFighter = true;
                if (tasks.CanAccept(livingActors[i].Id, command.Mode)) return CommandRejection.None;
            }
            return anyFighter ? CommandRejection.QueueFull : CommandRejection.ActorsLackAbility;
        }

        public void Execute(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            for (int i = 0; i < livingActors.Count; i++)
                if (CanFight(tasks.Context, livingActors[i]) && livingActors[i].Id != command.TargetEntity)
                    tasks.Assign(livingActors[i].Id, new AttackTask(command.TargetEntity), command.Mode);
        }

        private static bool CanFight(TaskContext context, Entity actor) =>
            context.Catalog.TryGet(actor.DefinitionId, out EntityDefinition d) && d.CanAttack && d.MoveSpeed > 0f;
    }
}
