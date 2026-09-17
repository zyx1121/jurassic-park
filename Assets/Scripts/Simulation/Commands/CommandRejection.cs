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
    }
}
