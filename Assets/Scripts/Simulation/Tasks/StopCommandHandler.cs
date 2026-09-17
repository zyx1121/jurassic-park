using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>Stop ends the current task and clears the queue. It needs no plan, so it never waits on pathfinding.</summary>
    public sealed class StopCommandHandler : ICommandHandler
    {
        private readonly TaskSystem tasks;

        public StopCommandHandler(TaskSystem tasks)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        // Stopping an idle unit is a harmless no-op, not an error the player should see.
        public CommandRejection Validate(World world, Command command, IReadOnlyList<Entity> livingActors) => CommandRejection.None;

        public void Execute(World world, Command command, IReadOnlyList<Entity> livingActors)
        {
            for (int i = 0; i < livingActors.Count; i++) tasks.Stop(livingActors[i].Id, TaskReason.Stopped);
        }
    }
}
