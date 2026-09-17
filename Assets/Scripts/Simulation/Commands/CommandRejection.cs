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
    }
}
