using System;
using System.Collections.Generic;
using JurassicPark.Simulation;

namespace JurassicPark.Net
{
    /// <summary>
    /// Which connection plays which seat. The seat a command speaks for always comes from here, never from the message, so a
    /// client cannot command anyone else's units by lying about who it is. No networking types in this class: it is the rule,
    /// tested without a socket.
    /// </summary>
    public sealed class SeatBinder
    {
        private readonly SeatRegistry seats;
        private readonly IReadOnlyList<SeatId> playable;
        private readonly SeatId hostSeat;
        private readonly Dictionary<ulong, SeatId> seatByClient = new Dictionary<ulong, SeatId>();
        private readonly Dictionary<SeatId, ulong> clientBySeat = new Dictionary<SeatId, ulong>();
        private readonly Dictionary<ulong, int> epochByClient = new Dictionary<ulong, int>();

        public SeatBinder(SeatRegistry seats, IReadOnlyList<SeatId> playableSeats, SeatId hostSeat)
        {
            this.seats = seats ?? throw new ArgumentNullException(nameof(seats));
            playable = playableSeats ?? throw new ArgumentNullException(nameof(playableSeats));
            this.hostSeat = hostSeat;
        }

        public bool TryGetSeat(ulong clientId, out SeatId seat) => seatByClient.TryGetValue(clientId, out seat);
        public bool TryGetClient(SeatId seat, out ulong clientId) => clientBySeat.TryGetValue(seat, out clientId);
        public int BoundCount => seatByClient.Count;

        /// <summary>
        /// The connection that should receive an answer: the one holding the seat under the very epoch the command was issued in.
        /// A seat can change hands within a frame, and the newcomer must never be handed the previous player's answers.
        /// </summary>
        public bool TryGetClientFor(SeatId seat, int commandEpoch, out ulong clientId) =>
            clientBySeat.TryGetValue(seat, out clientId) && epochByClient.TryGetValue(clientId, out int boundEpoch) && boundEpoch == commandEpoch;

        /// <summary>
        /// Seats the connection in the first playable seat nobody holds, in scenario order, and starts a new controller epoch for
        /// it so its command ids begin at 1 and whatever the computer ally still had in flight is refused. Returns None when the match is full.
        /// </summary>
        public SeatId Bind(ulong clientId)
        {
            if (seatByClient.TryGetValue(clientId, out SeatId already)) return already;
            for (int i = 0; i < playable.Count; i++)
            {
                SeatId candidate = playable[i];
                if (candidate == hostSeat || clientBySeat.ContainsKey(candidate) || !seats.TryGet(candidate, out Seat seat)) continue;
                // A seat the computer held changes hands; a seat already marked human still needs a fresh epoch for the new connection.
                if (seat.Controller != SeatController.Human) seats.SetController(candidate, SeatController.Human);
                else seats.BeginControllerEpoch(candidate);
                seatByClient.Add(clientId, candidate);
                clientBySeat.Add(candidate, clientId);
                epochByClient.Add(clientId, seat.ControllerEpoch);
                return candidate;
            }
            return SeatId.None;
        }

        /// <summary>The connection is gone: the computer takes the seat over, under a new epoch. Returns the seat it held, or None.</summary>
        public SeatId Unbind(ulong clientId)
        {
            if (!seatByClient.TryGetValue(clientId, out SeatId seat)) return SeatId.None;
            seatByClient.Remove(clientId);
            clientBySeat.Remove(seat);
            epochByClient.Remove(clientId);
            seats.SetController(seat, SeatController.Computer);
            return seat;
        }
    }
}
