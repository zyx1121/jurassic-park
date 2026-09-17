namespace JurassicPark.Simulation
{
    /// <summary>The authority's answer to one submitted command. The network layer returns it to the sender; the GUI clears its pending state on it.</summary>
    public sealed class CommandResolved : SimEvent
    {
        public SeatId Seat { get; }
        public int Epoch { get; }
        public long CommandId { get; }
        public CommandRejection Rejection { get; }

        /// <summary>True when this answers a resend of a command that was already resolved. Nothing was executed again.</summary>
        public bool IsRepeat { get; }

        /// <summary>The seat's controller epoch on the authority when this was answered. A sender refused with WrongEpoch adopts it.</summary>
        public int CurrentEpoch { get; }

        /// <summary>
        /// The next id the authority will accept from this seat in <see cref="CurrentEpoch"/>. Every answer carries it, so a sender whose
        /// counter ran ahead (dropped commands, a restart, a bug) can always resynchronise instead of being refused forever.
        /// </summary>
        public long NextCommandId { get; }

        public bool Accepted => Rejection == CommandRejection.None;

        public CommandResolved(SeatId seat, int epoch, long commandId, CommandRejection rejection, bool isRepeat, int currentEpoch, long nextCommandId)
        {
            Seat = seat;
            Epoch = epoch;
            CommandId = commandId;
            Rejection = rejection;
            IsRepeat = isRepeat;
            CurrentEpoch = currentEpoch;
            NextCommandId = nextCommandId;
        }
    }
}
