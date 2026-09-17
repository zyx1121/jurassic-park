using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// The single door into the simulation. Submit only queues; commands are validated and executed at the start of the
    /// next tick, in arrival order, so input never mutates state between ticks and commands caused by events run at the
    /// next boundary instead of re-entering. Register it as the first system.
    /// </summary>
    public sealed class CommandRouter : ISimSystem
    {
        private sealed class SeatLedger
        {
            public long LastCommandId;
            public int Pending;
            public readonly Dictionary<long, CommandRejection> Results = new Dictionary<long, CommandRejection>();
            public readonly Queue<long> ResultOrder = new Queue<long>();
        }

        private readonly SeatRegistry seats;
        private readonly CommandRouterConfig config;
        private readonly Dictionary<CommandKind, ICommandHandler> handlers = new Dictionary<CommandKind, ICommandHandler>();
        private readonly Dictionary<SeatId, SeatLedger> ledgers = new Dictionary<SeatId, SeatLedger>();
        private readonly Queue<Command> pending = new Queue<Command>();

        public CommandRouter(SeatRegistry seats, CommandRouterConfig config)
        {
            this.seats = seats ?? throw new ArgumentNullException(nameof(seats));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public int PendingCount => pending.Count;

        public void Register(CommandKind kind, ICommandHandler handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (handlers.ContainsKey(kind)) throw new ArgumentException($"{kind} already has a handler.", nameof(kind));
            handlers.Add(kind, handler);
        }

        /// <summary>
        /// Queues a command for the next tick. Returns false only when the seat is flooding and the command was dropped unanswered;
        /// every queued command is answered by exactly one CommandResolved event.
        /// </summary>
        public bool Submit(Command command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            SeatLedger ledger = LedgerFor(command.Seat);
            if (ledger.Pending >= config.MaxPendingPerSeat) return false;
            ledger.Pending++;
            pending.Enqueue(command);
            return true;
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
            if (!seats.TryGet(command.Seat, out _))
            {
                world.Raise(new CommandResolved(command.Seat, command.CommandId, CommandRejection.UnknownSeat, false));
                return;
            }
            if (command.CommandId <= ledger.LastCommandId)
            {
                bool remembered = ledger.Results.TryGetValue(command.CommandId, out CommandRejection original);
                world.Raise(new CommandResolved(command.Seat, command.CommandId,
                    remembered ? original : CommandRejection.StaleCommandId, remembered));
                return;
            }

            CommandRejection rejection = Validate(world, command, out ICommandHandler handler);
            if (rejection == CommandRejection.None) handler.Execute(world, command);

            ledger.LastCommandId = command.CommandId;
            ledger.Results[command.CommandId] = rejection;
            ledger.ResultOrder.Enqueue(command.CommandId);
            while (ledger.ResultOrder.Count > config.RememberedResultsPerSeat) ledger.Results.Remove(ledger.ResultOrder.Dequeue());
            world.Raise(new CommandResolved(command.Seat, command.CommandId, rejection, false));
        }

        private CommandRejection Validate(World world, Command command, out ICommandHandler handler)
        {
            handler = null;
            if (command.Actors.Count == 0) return CommandRejection.NoActors;
            int alive = 0;
            for (int i = 0; i < command.Actors.Count; i++)
            {
                // An id that never resolved, or someone else's unit, makes the whole command malformed. Allies may share depots, never units.
                if (!world.TryGet(command.Actors[i], out Entity actor)) return CommandRejection.UnknownActor;
                if (actor.Owner != command.Seat) return CommandRejection.NotOwner;
                if (actor.IsAlive) alive++;
            }
            // A unit that died between the click and the tick must not void the order for the rest of the selection.
            // Handlers act on the living actors only.
            if (alive == 0) return CommandRejection.ActorNotAlive;
            if (!handlers.TryGetValue(command.Kind, out handler)) return CommandRejection.UnsupportedKind;
            return handler.Validate(world, command);
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
