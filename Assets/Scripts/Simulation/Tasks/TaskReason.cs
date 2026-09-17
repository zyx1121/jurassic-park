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
        TargetGone = 11,
        NodeDepleted = 12,
        NoDepotAvailable = 13,
        NothingToCarry = 14,
        NotAllowed = 15,
        ActorCannotCarry = 16,
        PackFull = 17,
        SourceEmpty = 18,
        Delivered = 19,
        PickedUp = 20,

        /// <summary>Some goods were handed over and the rest stayed in the pack because the store filled up.</summary>
        DeliveredPartly = 21,
    }
}
