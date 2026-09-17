namespace JurassicPark.Simulation
{
    /// <summary>
    /// A piece of ongoing work for one actor: it keeps the goal, the progress and the outcome across ticks.
    /// The task system owns the lifecycle; a subclass only decides what one tick of work does and how to let go.
    /// </summary>
    public abstract class SimTask
    {
        public TaskId Id { get; internal set; }
        public EntityId Actor { get; internal set; }
        public TaskState State { get; private set; } = TaskState.Queued;
        public TaskReason Reason { get; private set; }

        /// <summary>Short stable name for events and the GUI, for example "move".</summary>
        public abstract string Kind { get; }

        public bool IsFinished => State == TaskState.Completed || State == TaskState.Cancelled || State == TaskState.Failed;

        /// <summary>One tick of work. Call <see cref="Enter"/> to change state; return without changing it to continue next tick.</summary>
        protected internal abstract void Tick(TaskContext context, Entity actor);

        /// <summary>Give back whatever the task holds (reservations, operating positions). Called exactly once, on any ending, and must be safe if nothing was taken.</summary>
        protected internal virtual void Release(TaskContext context)
        {
        }

        /// <summary>Moves to a new state and publishes it. Does nothing once finished, so an ending can never be overwritten.</summary>
        protected internal void Enter(TaskContext context, TaskState state, TaskReason reason = TaskReason.None)
        {
            if (IsFinished || (state == State && reason == Reason)) return;
            State = state;
            Reason = reason;
            context.World.Raise(new TaskStateChanged(Id, Actor, Kind, state, reason));
        }
    }
}
