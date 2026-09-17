namespace JurassicPark.Simulation
{
    /// <summary>Why the authority refused a command. The GUI turns this into the "cannot do that" message, so it must stay specific.</summary>
    public enum CommandRejection
    {
        None = 0,
        UnknownSeat = 1,
        NoActors = 2,
        UnknownActor = 3,
        ActorNotAlive = 4,
        NotOwner = 5,
        UnsupportedKind = 6,
        InvalidTarget = 7,

        /// <summary>The id is not newer than this seat's last command and its original result is no longer remembered.</summary>
        StaleCommandId = 8,

        /// <summary>The id is not positive, or jumps further ahead than the router allows. The authority never adopts an arbitrary id as its watermark.</summary>
        InvalidCommandId = 9,

        /// <summary>The command was issued under an earlier controller of this seat, for example by the computer ally just before the human returned.</summary>
        WrongEpoch = 10,

        /// <summary>A field holds a value outside its enum.</summary>
        Malformed = 11,

        /// <summary>None of the living actors can do what the command asks, for example moving a building.</summary>
        ActorsLackAbility = 12,

        /// <summary>A queued order, and every actor that could take it already has a full queue. Nothing was created.</summary>
        QueueFull = 13,

        /// <summary>The target exists but this seat may not use it, for example an enemy depot or another unit's pack.</summary>
        NotAllowedOnTarget = 14,

        /// <summary>The seat already had the maximum number of commands waiting, so this one was dropped at the door and its id was not consumed. Sent by the network layer, never by the router.</summary>
        DroppedFlood = 15,
    }
}
