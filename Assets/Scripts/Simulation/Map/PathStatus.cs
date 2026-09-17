namespace JurassicPark.Simulation
{
    /// <summary>
    /// How a path query ended. "Not answered yet" and "proved impossible" are different outcomes: a task that treats a
    /// budget cut off as no route would cancel work the world can still do, and one that treats no route as pending
    /// would wait forever.
    /// </summary>
    public enum PathStatus
    {
        /// <summary>
        /// A complete route from start to goal. There is deliberately no partial status: a route that only gets closer
        /// is not an arrival, and returning one would let a task walk its unit somewhere and call the work done.
        /// </summary>
        Found = 0,

        /// <summary>The reachable area was searched out and the goal was not in it.</summary>
        NoRoute = 1,

        /// <summary>Start or goal is outside the map or on terrain nothing can ever stand on, so the request itself was wrong.</summary>
        InvalidEndpoint = 2,

        /// <summary>The search hit its expansion budget. The answer is unknown, so retry with a larger budget or reconsider the task.</summary>
        BudgetExceeded = 3
    }
}
