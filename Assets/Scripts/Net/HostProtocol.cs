using System;
using System.Collections.Generic;
using JurassicPark.Simulation;
using Unity.Netcode;

namespace JurassicPark.Net
{
    /// <summary>
    /// The host's half of the protocol with the network taken out: what to do with a command message from a connection, and
    /// who gets which answer. It is the trust boundary, so it is a plain class that tests drive directly with bytes and ids.
    /// </summary>
    public sealed class HostProtocol
    {
        private sealed class Budget
        {
            public double Tokens;
            public double RefilledAt;
            public bool Warned;
        }

        private readonly CommandRouter router;
        private readonly SeatBinder binder;
        private readonly Action<ulong, CommandResolved> sendAnswer;
        private readonly Action<string> warn;
        private readonly double commandsPerSecond;
        private readonly double burst;
        private readonly Dictionary<ulong, Budget> budgets = new Dictionary<ulong, Budget>();

        /// <param name="commandsPerSecond">Sustained command messages a connection may send. Beyond it messages are dropped unread and unanswered, so a flood costs the host almost nothing.</param>
        public HostProtocol(CommandRouter router, SeatBinder binder, Action<ulong, CommandResolved> sendAnswer, Action<string> warn, double commandsPerSecond, double burst)
        {
            this.router = router ?? throw new ArgumentNullException(nameof(router));
            this.binder = binder ?? throw new ArgumentNullException(nameof(binder));
            this.sendAnswer = sendAnswer ?? throw new ArgumentNullException(nameof(sendAnswer));
            this.warn = warn ?? (_ => { });
            this.commandsPerSecond = commandsPerSecond;
            this.burst = burst;
        }

        public SeatBinder Binder => binder;

        public void OnClientLeft(ulong clientId)
        {
            budgets.Remove(clientId);
            binder.Unbind(clientId);
        }

        /// <summary>A command message arrived. Returns true when it was submitted to the router.</summary>
        public bool OnCommand(ulong senderClientId, ref FastBufferReader reader, double now)
        {
            // The seat comes from the binding, never from the payload. No seat, no service.
            if (!binder.TryGetSeat(senderClientId, out SeatId seat)) return false;
            if (!Spend(senderClientId, now)) return false;
            if (!NetMessages.TryReadCommand(ref reader, seat, out Command command))
            {
                warn($"dropped an unreadable command from client {senderClientId}");
                return false;
            }
            SubmitOutcome outcome = router.Submit(command);
            if (outcome == SubmitOutcome.Queued) return true;
            // A command dropped at the door raises no event, so its answer is written here. The sync point comes from the router,
            // which counts the ids still waiting in its queue: it never tells a sender to reuse an id that is about to resolve.
            router.TryGetSync(seat, out int epoch, out long nextCommandId);
            sendAnswer(senderClientId, new CommandResolved(seat, command.Epoch, command.CommandId, CommandRejection.DroppedFlood, false, epoch, nextCommandId));
            return false;
        }

        /// <summary>Returns each answer to the connection that issued the command, and to nobody else.</summary>
        public void RouteAnswers(IReadOnlyList<SimEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i] is CommandResolved answer && binder.TryGetClientFor(answer.Seat, answer.Epoch, out ulong clientId)) sendAnswer(clientId, answer);
        }

        private bool Spend(ulong clientId, double now)
        {
            if (!budgets.TryGetValue(clientId, out Budget budget))
            {
                budget = new Budget { Tokens = burst, RefilledAt = now };
                budgets.Add(clientId, budget);
            }
            budget.Tokens = Math.Min(burst, budget.Tokens + (now - budget.RefilledAt) * commandsPerSecond);
            budget.RefilledAt = now;
            if (budget.Tokens < 1.0)
            {
                if (!budget.Warned) warn($"client {clientId} is sending commands faster than {commandsPerSecond}/s; the excess is dropped unread");
                budget.Warned = true;
                return false;
            }
            budget.Tokens -= 1.0;
            return true;
        }
    }
}
