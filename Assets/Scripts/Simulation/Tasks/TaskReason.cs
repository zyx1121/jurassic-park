namespace JurassicPark.Simulation
{
    /// <summary>Why a task is blocked or how it ended. Shown to the player, so it stays specific.</summary>
    public enum TaskReason
    {
        None = 0,
        Arrived = 1,
        Stopped = 2,
        ReplacedByNewCommand = 3,
        ActorRemoved = 4,
        NoRoute = 5,
        ActorCannotMove = 6,
        TargetOutOfBounds = 7,
        PathSearchBudgetExceeded = 8,
        RouteBlocked = 9,
        PreviousTaskDidNotComplete = 10,
    }
}
