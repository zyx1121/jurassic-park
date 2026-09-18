using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>Build: place a site and put the actors to work on it.</summary>
    public sealed class BuildCommandHandler : ICommandHandler
    {
        private readonly TaskSystem tasks;

        public BuildCommandHandler(TaskSystem tasks) => this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));

        public CommandRejection Validate(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            TaskContext context = tasks.Context;
            if (context.Structures == null) return CommandRejection.UnsupportedKind;
            if (!context.Catalog.TryGetByIndex(command.Argument, out EntityDefinition definition) || !definition.IsBuildable) return CommandRejection.NotBuildable;
            Cell anchor = context.Map.CellAt(command.TargetPosition);
            CommandRejection placement = context.Structures.CheckPlacement(definition, anchor, out _);
            if (placement != CommandRejection.None) return placement;
            bool anyBuilder = false;
            for (int i = 0; i < livingActors.Count; i++)
            {
                if (!CanBuild(context, livingActors[i])) continue;
                anyBuilder = true;
                if (tasks.CanAccept(livingActors[i].Id, command.Mode)) return CommandRejection.None;
            }
            return anyBuilder ? CommandRejection.QueueFull : CommandRejection.ActorsLackAbility;
        }

        public void Execute(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            TaskContext context = tasks.Context;
            context.Catalog.TryGetByIndex(command.Argument, out EntityDefinition definition);
            Entity site = context.Structures.PlaceSite(definition, command.Seat, context.Map.CellAt(command.TargetPosition));
            if (site == null) return;
            for (int i = 0; i < livingActors.Count; i++)
                if (CanBuild(context, livingActors[i])) tasks.Assign(livingActors[i].Id, new BuildTask(site.Id), command.Mode);
        }

        private static bool CanBuild(TaskContext context, Entity actor) =>
            LogisticsQueries.CanCarry(context, actor) && context.Catalog.TryGet(actor.DefinitionId, out EntityDefinition d) && d.MoveSpeed > 0f;
    }

    /// <summary>Demolish and ToggleGate act at once on the target; the actors are only who asked.</summary>
    public sealed class StructureCommandHandler : ICommandHandler
    {
        private readonly TaskSystem tasks;
        private readonly CommandKind kind;

        public StructureCommandHandler(TaskSystem tasks, CommandKind kind)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            if (kind != CommandKind.Demolish && kind != CommandKind.ToggleGate) throw new ArgumentException($"{kind} is not a structure command.", nameof(kind));
            this.kind = kind;
        }

        public CommandRejection Validate(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            Structures structures = tasks.Context.Structures;
            if (structures == null) return CommandRejection.UnsupportedKind;
            if (!world.TryGet(command.TargetEntity, out Entity target) || !target.IsAlive || target.Kind != EntityKind.Building) return CommandRejection.InvalidTarget;
            if (kind == CommandKind.Demolish) return target.Owner == command.Seat ? CommandRejection.None : CommandRejection.NotAllowedOnTarget;
            return structures.CheckToggle(target.Id, command.Seat);
        }

        public void Execute(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            Structures structures = tasks.Context.Structures;
            if (kind == CommandKind.Demolish) structures.Demolish(command.TargetEntity, command.Seat);
            else structures.ToggleGate(command.TargetEntity, command.Seat, out _);
        }
    }
}
