using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>ChooseMatch: Argument is modeIndex * 10 + difficulty. Any living unit of the seat may carry the request; the match decides whether it is still time.</summary>
    public sealed class ChooseMatchCommandHandler : ICommandHandler
    {
        private readonly MatchFlow match;
        public ChooseMatchCommandHandler(MatchFlow match) => this.match = match ?? throw new ArgumentNullException(nameof(match));

        public CommandRejection Validate(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            if (match.Phase != MatchPhase.Setup) return CommandRejection.InvalidTarget;
            int mode = command.Argument / 10, difficulty = command.Argument % 10;
            return mode >= 0 && mode < match.Rules.Modes.Count && difficulty >= 1 && difficulty <= match.Rules.DifficultyCount ? CommandRejection.None : CommandRejection.Malformed;
        }

        public void Execute(World world, Command command, IReadOnlyList<Entity> livingActors) =>
            match.TryChoose(command.Argument / 10, command.Argument % 10, command.Seat);
    }

    /// <summary>Board: every living actor that can walk goes to the helicopter.</summary>
    public sealed class BoardCommandHandler : ICommandHandler
    {
        private readonly TaskSystem tasks;
        private readonly MatchFlow match;

        public BoardCommandHandler(TaskSystem tasks, MatchFlow match)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            this.match = match ?? throw new ArgumentNullException(nameof(match));
        }

        public CommandRejection Validate(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            if (match.Phase != MatchPhase.Evacuation || match.Helicopter.IsNone) return CommandRejection.InvalidTarget;
            bool anyWalker = false;
            for (int i = 0; i < livingActors.Count; i++)
            {
                if (!CanWalk(livingActors[i])) continue;
                anyWalker = true;
                if (tasks.CanAccept(livingActors[i].Id, command.Mode)) return CommandRejection.None;
            }
            return anyWalker ? CommandRejection.QueueFull : CommandRejection.ActorsLackAbility;
        }

        public void Execute(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            for (int i = 0; i < livingActors.Count; i++)
                if (CanWalk(livingActors[i])) tasks.Assign(livingActors[i].Id, new BoardTask(match), command.Mode);
        }

        private bool CanWalk(Entity actor) => actor.Kind == EntityKind.Unit && tasks.Context.Catalog.TryGet(actor.DefinitionId, out EntityDefinition d) && d.MoveSpeed > 0f;
    }
}
