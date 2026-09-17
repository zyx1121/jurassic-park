namespace JurassicPark.Simulation
{
    /// <summary>The authority's answer to one submitted command. The network layer returns it to the sender; the GUI clears its pending state on it.</summary>
    public sealed class CommandResolved : SimEvent
    {
        public SeatId Seat { get; }
        public long CommandId { get; }
        public CommandRejection Rejection { get; }

        /// <summary>True when this answers a resend of a command that was already resolved. Nothing was executed again.</summary>
        public bool IsRepeat { get; }

        public bool Accepted => Rejection == CommandRejection.None;

        public CommandResolved(SeatId seat, long commandId, CommandRejection rejection, bool isRepeat)
        {
            Seat = seat;
            CommandId = commandId;
            Rejection = rejection;
            IsRepeat = isRepeat;
        }
    }
}
