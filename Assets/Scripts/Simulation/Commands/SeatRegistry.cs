using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>The seats of a match, fixed at setup. Only the controller of a seat changes afterwards.</summary>
    public sealed class SeatRegistry
    {
        private readonly World world;
        private readonly Dictionary<SeatId, Seat> byId = new Dictionary<SeatId, Seat>();
        private readonly List<Seat> ordered = new List<Seat>();
        private readonly IReadOnlyList<Seat> orderedView;

        public IReadOnlyList<Seat> Seats => orderedView;

        public SeatRegistry(World world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            orderedView = ordered.AsReadOnly();
        }

        public Seat Add(SeatId id, string displayName, int team, SeatController controller)
        {
            if (id.IsNone) throw new ArgumentException("The unowned seat cannot be registered.", nameof(id));
            if (byId.ContainsKey(id)) throw new ArgumentException($"{id} is already registered.", nameof(id));
            var seat = new Seat(id, displayName ?? id.ToString(), team, controller);
            byId.Add(id, seat);
            ordered.Add(seat);
            return seat;
        }

        public bool TryGet(SeatId id, out Seat seat) => byId.TryGetValue(id, out seat);

        /// <summary>True for two different registered seats on the same team. A seat is not its own ally: ownership checks stay explicit.</summary>
        public bool AreAllied(SeatId a, SeatId b) =>
            a != b && byId.TryGetValue(a, out Seat seatA) && byId.TryGetValue(b, out Seat seatB) && seatA.Team == seatB.Team;

        /// <summary>
        /// True when the seat may use a container or site owned by <paramref name="owner"/>: its own, an ally's, or an unowned one.
        /// Resource nodes and ground piles belong to no seat and are open to every registered seat, as in the original map.
        /// </summary>
        public bool MayUse(SeatId seat, SeatId owner) =>
            byId.ContainsKey(seat) && (owner.IsNone || seat == owner || AreAllied(seat, owner));

        /// <summary>Hands the seat to a human or a computer ally. Returns false when nothing changed or the seat is unknown.</summary>
        public bool SetController(SeatId id, SeatController controller)
        {
            if (!byId.TryGetValue(id, out Seat seat) || seat.Controller == controller) return false;
            seat.Controller = controller;
            seat.ControllerEpoch++;
            world.Raise(new SeatControllerChanged(id, controller, seat.ControllerEpoch));
            return true;
        }
    }
}
