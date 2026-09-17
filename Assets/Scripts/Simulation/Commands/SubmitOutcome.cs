namespace JurassicPark.Simulation
{
    /// <summary>
    /// What happened to a command at the door. Only Queued commands are answered later by a CommandResolved event;
    /// for the other outcomes the caller (the network layer, for a remote client) must answer the sender itself.
    /// </summary>
    public enum SubmitOutcome
    {
        Queued = 1,

        /// <summary>The seat already has the maximum number of commands waiting. The id was not consumed: the sender reuses it for its next command.</summary>
        DroppedFlood = 2,

        /// <summary>The seat is not registered. Nothing is remembered about it.</summary>
        DroppedUnknownSeat = 3,
    }
}
