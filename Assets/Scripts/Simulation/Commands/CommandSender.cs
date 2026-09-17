using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// The sending side of the command protocol for one seat: stamps epoch and id, reuses an id the door did not take,
    /// and resynchronises from every answer. The GUI, a computer ally and a network client all send through one of these,
    /// so none of them can run its counter away from the authority. Where the command goes is a delegate: straight into the
    /// router on the host, onto the wire on a client.
    /// </summary>
    public sealed class CommandSender
    {
        private readonly Func<Command, SubmitOutcome> submit;
        private long nextCommandId = 1;

        public SeatId Seat { get; }
        public int Epoch { get; private set; }

        /// <param name="epoch">The seat's current controller epoch, from the registry or the bind handshake.</param>
        public CommandSender(CommandRouter router, SeatId seat, int epoch)
            : this((router ?? throw new ArgumentNullException(nameof(router))).Submit, seat, epoch)
        {
        }

        /// <param name="submit">Takes the command. Returns Queued when it is on its way; a client's transport always does, and learns of a drop from the answer instead.</param>
        public CommandSender(Func<Command, SubmitOutcome> submit, SeatId seat, int epoch, long nextCommandId = 1)
        {
            this.submit = submit ?? throw new ArgumentNullException(nameof(submit));
            Seat = seat;
            Epoch = epoch;
            this.nextCommandId = Math.Max(1, nextCommandId);
        }

        public SubmitOutcome Send(CommandKind kind, IReadOnlyList<EntityId> actors, SimVector2 targetPosition = default,
            EntityId targetEntity = default, CommandMode mode = CommandMode.Replace)
        {
            var command = new Command(nextCommandId, Epoch, Seat, kind, actors, targetPosition, targetEntity, mode);
            SubmitOutcome outcome = submit(command);
            if (outcome == SubmitOutcome.Queued) nextCommandId++;
            return outcome;
        }

        /// <summary>Feed every CommandResolved for this seat. Returns true when the sender had drifted and was corrected.</summary>
        public bool Observe(CommandResolved answer)
        {
            if (answer == null || answer.Seat != Seat || answer.Accepted || answer.IsRepeat) return false;
            if (answer.Rejection != CommandRejection.WrongEpoch && answer.Rejection != CommandRejection.InvalidCommandId &&
                answer.Rejection != CommandRejection.StaleCommandId && answer.Rejection != CommandRejection.DroppedFlood) return false;
            // Commands still unanswered from the old epoch stay refused as WrongEpoch. Never re-stamp them: the authority
            // forgets results across epochs, so a relabelled command would run a second time.
            if (answer.CurrentEpoch > Epoch) Epoch = answer.CurrentEpoch;
            if (answer.CurrentEpoch == Epoch) nextCommandId = answer.NextCommandId;
            return true;
        }
    }
}
