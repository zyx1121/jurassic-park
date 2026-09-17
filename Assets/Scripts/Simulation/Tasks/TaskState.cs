namespace JurassicPark.Simulation
{
    /// <summary>Lifecycle of a task. Replaced, cancelled and failed are different endings and the GUI shows them differently.</summary>
    public enum TaskState
    {
        /// <summary>Waiting behind the actor's current task.</summary>
        Queued = 1,

        /// <summary>Choosing a route or a source. A query that ran out of budget keeps the task here; that is not the same as no route.</summary>
        Planning = 2,
        Running = 3,

        /// <summary>Cannot proceed right now for a named reason, and will retry at a limited rate.</summary>
        Blocked = 4,
        Completed = 5,
        Cancelled = 6,
        Failed = 7,
    }
}
