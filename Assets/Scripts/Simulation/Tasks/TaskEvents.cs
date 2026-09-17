namespace JurassicPark.Simulation
{
    /// <summary>A task changed state. One event type, because consumers (GUI, scenario checks, the network snapshot) care about the transition, not a class per state.</summary>
    public sealed class TaskStateChanged : SimEvent
    {
        public TaskId Task { get; }
        public EntityId Actor { get; }
        public string TaskKind { get; }
        public TaskState State { get; }
        public TaskReason Reason { get; }

        public TaskStateChanged(TaskId task, EntityId actor, string taskKind, TaskState state, TaskReason reason)
        {
            Task = task;
            Actor = actor;
            TaskKind = taskKind;
            State = state;
            Reason = reason;
        }
    }
}
