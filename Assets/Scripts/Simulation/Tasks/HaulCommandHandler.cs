using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>Gather, Deliver and Pickup differ only in which target is valid, which actors qualify and which task they get.</summary>
    public sealed class HaulCommandHandler : ICommandHandler
    {
        private readonly TaskSystem tasks;
        private readonly CommandKind kind;

        public HaulCommandHandler(TaskSystem tasks, CommandKind kind)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            if (kind != CommandKind.Gather && kind != CommandKind.Deliver && kind != CommandKind.Pickup)
                throw new ArgumentException($"{kind} is not a hauling command.", nameof(kind));
            this.kind = kind;
        }

        public CommandRejection Validate(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            TaskContext context = tasks.Context;
            if (context.Logistics == null || !world.TryGet(command.TargetEntity, out Entity target) || !target.IsAlive) return CommandRejection.InvalidTarget;
            if (kind == CommandKind.Gather)
            {
                if (!context.Logistics.TryGetNode(target.Id, out _)) return CommandRejection.InvalidTarget;
            }
            else if (!LogisticsQueries.IsOpenStore(context, command.Seat, target))
            {
                return context.Logistics.TryGetContainer(target.Id, out _) ? CommandRejection.NotAllowedOnTarget : CommandRejection.InvalidTarget;
            }

            bool anyAble = false;
            for (int i = 0; i < livingActors.Count; i++)
            {
                if (!Qualifies(context, livingActors[i], target)) continue;
                anyAble = true;
                if (tasks.CanAccept(livingActors[i].Id, command.Mode)) return CommandRejection.None;
            }
            return anyAble ? CommandRejection.QueueFull : CommandRejection.ActorsLackAbility;
        }

        public void Execute(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            world.TryGet(command.TargetEntity, out Entity target);
            for (int i = 0; i < livingActors.Count; i++)
            {
                if (!Qualifies(tasks.Context, livingActors[i], target)) continue;
                SimTask task = kind == CommandKind.Gather ? new GatherTask(target.Id)
                    : kind == CommandKind.Deliver ? (SimTask)new DeliverTask(target.Id) : new PickupTask(target.Id);
                tasks.Assign(livingActors[i].Id, task, command.Mode);
            }
        }

        private bool Qualifies(TaskContext context, Entity actor, Entity target) =>
            actor.Id != target.Id && (kind == CommandKind.Gather ? LogisticsQueries.CanGather(context, actor) : LogisticsQueries.CanCarry(context, actor) && IsMobile(context, actor));

        private static bool IsMobile(TaskContext context, Entity actor) =>
            context.Catalog.TryGet(actor.DefinitionId, out EntityDefinition definition) && definition.MoveSpeed > 0f;
    }
}
