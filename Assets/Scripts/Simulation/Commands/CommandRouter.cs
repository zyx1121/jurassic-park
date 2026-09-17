using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// The single door into the simulation. Submit only queues; commands are validated and executed at the start of the
    /// next tick, in arrival order, so input never mutates state between ticks and commands caused by events run at the
    /// next boundary instead of re-entering. Register it as the first system.
    /// CommandResolved is raised into the shared event batch; returning it to the right client only is the network layer's job.
    /// </summary>
    public sealed class CommandRouter : ISimSystem
    {
        private sealed class SeatLedger
        {
            public int Epoch;
            public long LastCommandId;
            public int Pending;
            public readonly Dictionary<long, CommandRejection> Results = new Dictionary<long, CommandRejection>();
            public readonly Queue<long> ResultOrder = new Queue<long>();

            public void StartEpoch(int epoch)
            {
                Epoch = epoch;
                LastCommandId = 0;
                Results.Clear();
                ResultOrder.Clear();
            }
        }

        private readonly SeatRegistry seats;
        private readonly CommandRouterConfig config;
        private readonly Dictionary<CommandKind, ICommandHandler> handlers = new Dictionary<CommandKind, ICommandHandler>();
        private readonly Dictionary<SeatId, SeatLedger> ledgers = new Dictionary<SeatId, SeatLedger>();
        private readonly Queue<Command> pending = new Queue<Command>();
        private readonly List<Entity> livingActors = new List<Entity>();

        public CommandRouter(SeatRegistry seats, CommandRouterConfig config)
        {
            this.seats = seats ?? throw new ArgumentNullException(nameof(seats));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public int PendingCount => pending.Count;

        /// <summary>
        /// What a sender for this seat must use next: the seat's current controller epoch and the next id the router will accept.
        /// The network layer puts it in the bind handshake and in the answer it writes itself when Submit drops a command.
        /// </summary>
        public bool TryGetSync(SeatId seatId, out int epoch, out long nextCommandId)
        {
            epoch = 0;
            nextCommandId = 0;
            if (!seats.TryGet(seatId, out Seat seat)) return false;
            epoch = seat.ControllerEpoch;
            long last = ledgers.TryGetValue(seatId, out SeatLedger ledger) && ledger.Epoch == epoch ? ledger.LastCommandId : 0;
            nextCommandId = NextIdAfter(seatId, epoch, last);
            return true;
        }

        /// <summary>
        /// The id a sender should use next: one past the last resolved id AND past every plausible id still waiting in the queue.
        /// Counting only resolved ids would tell a sender to reuse ids that are about to resolve, and the reused ones would then be
        /// answered as repeats of those and silently never run.
        /// </summary>
        private long NextIdAfter(SeatId seatId, int epoch, long lastResolved)
        {
            long highest = lastResolved;
            long ceiling = lastResolved + config.MaxCommandIdGap + config.MaxPendingPerSeat;
            foreach (Command queued in pending)
            {
                if (queued.Seat != seatId || queued.Epoch != epoch) continue;
                // An absurd queued id is going to be refused; it must not drag the sync point up with it.
                if (queued.CommandId > highest && queued.CommandId <= ceiling) highest = queued.CommandId;
            }
            return highest + 1;
        }

        public void Register(CommandKind kind, ICommandHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (handlers.ContainsKey(kind)) throw new ArgumentException($"{kind} already has a handler.", nameof(kind));
            handlers.Add(kind, handler);
        }

        /// <summary>Queues a command for the next tick. See <see cref="SubmitOutcome"/> for who answers the sender.</summary>
        public SubmitOutcome Submit(Command command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            // Unknown seats get no ledger, so they can neither grow memory nor claim their own share of the queue.
            if (!seats.TryGet(command.Seat, out _)) return SubmitOutcome.DroppedUnknownSeat;
            SeatLedger ledger = LedgerFor(command.Seat);
            if (ledger.Pending >= config.MaxPendingPerSeat) return SubmitOutcome.DroppedFlood;
            ledger.Pending++;
            pending.Enqueue(command);
            return SubmitOutcome.Queued;
        }

        public void Tick(World world)
        {
            // Only what was queued before this tick: a handler that submits follow-up commands gets them run next tick.
            int count = pending.Count;
            for (int i = 0; i < count; i++)
            {
                Command command = pending.Dequeue();
                SeatLedger ledger = LedgerFor(command.Seat);
                ledger.Pending--;
                Resolve(world, command, ledger);
            }
        }

        private void Resolve(World world, Command command, SeatLedger ledger)
        {
            if (!seats.TryGet(command.Seat, out Seat seat))
            {
                // Submit turns unknown seats away, so this is unreachable today; answer rather than fault the match if that ever changes.
                world.Raise(new CommandResolved(command.Seat, command.Epoch, command.CommandId, CommandRejection.UnknownSeat, false, 0, 0));
                return;
            }
            // Ids restart with every controller, so a returning human's id 41 is never mistaken for the computer's id 41.
            if (ledger.Epoch != seat.ControllerEpoch) ledger.StartEpoch(seat.ControllerEpoch);
            if (command.Epoch != seat.ControllerEpoch)
            {
                Answer(world, command, ledger, CommandRejection.WrongEpoch, false);
                return;
            }

            if (command.CommandId <= 0)
            {
                Answer(world, command, ledger, CommandRejection.InvalidCommandId, false);
                return;
            }
            if (command.CommandId <= ledger.LastCommandId)
            {
                bool remembered = ledger.Results.TryGetValue(command.CommandId, out CommandRejection original);
                Answer(world, command, ledger, remembered ? original : CommandRejection.StaleCommandId, remembered);
                return;
            }
            // Never adopt an arbitrary id as the watermark. The answer carries NextCommandId, so a sender that ran ahead resynchronises.
            if (command.CommandId - ledger.LastCommandId > config.MaxCommandIdGap)
            {
                Answer(world, command, ledger, CommandRejection.InvalidCommandId, false);
                return;
            }

            CommandRejection rejection = Validate(world, command, out ICommandHandler handler);
            if (rejection == CommandRejection.None) handler.Execute(world, command, livingActors);
            livingActors.Clear();

            ledger.LastCommandId = command.CommandId;
            ledger.Results[command.CommandId] = rejection;
            ledger.ResultOrder.Enqueue(command.CommandId);
            while (ledger.ResultOrder.Count > config.RememberedResultsPerSeat) ledger.Results.Remove(ledger.ResultOrder.Dequeue());
            Answer(world, command, ledger, rejection, false);
        }

        private void Answer(World world, Command command, SeatLedger ledger, CommandRejection rejection, bool isRepeat) =>
            world.Raise(new CommandResolved(command.Seat, command.Epoch, command.CommandId, rejection, isRepeat, ledger.Epoch,
                NextIdAfter(command.Seat, ledger.Epoch, ledger.LastCommandId)));

        private CommandRejection Validate(World world, Command command, out ICommandHandler handler)
        {
            handler = null;
            livingActors.Clear();
            if (!Enum.IsDefined(typeof(CommandKind), command.Kind) || !Enum.IsDefined(typeof(CommandMode), command.Mode))
                return CommandRejection.Malformed;
            if (command.Actors.Count == 0) return CommandRejection.NoActors;
            for (int i = 0; i < command.Actors.Count; i++)
            {
                // An id that never resolved, or someone else's unit, makes the whole command malformed. Allies may share depots, never units.
                if (!world.TryGet(command.Actors[i], out Entity actor)) return CommandRejection.UnknownActor;
                if (actor.Owner != command.Seat) return CommandRejection.NotOwner;
                // A removed entity stays resolvable until commit; it must not reach the handler.
                if (actor.IsAlive && !livingActors.Contains(actor)) livingActors.Add(actor);
            }
            // A unit that died between the click and the tick must not void the order for the rest of the selection.
            if (livingActors.Count == 0) return CommandRejection.ActorNotAlive;
            if (!handlers.TryGetValue(command.Kind, out handler)) return CommandRejection.UnsupportedKind;
            return handler.Validate(world, command, livingActors);
        }

        private SeatLedger LedgerFor(SeatId seat)
        {
            if (!ledgers.TryGetValue(seat, out SeatLedger ledger))
            {
                ledger = new SeatLedger();
                ledgers.Add(seat, ledger);
            }
            return ledger;
        }
    }
}
