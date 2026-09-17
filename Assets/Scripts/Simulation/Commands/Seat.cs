namespace JurassicPark.Simulation
{
    /// <summary>One participant slot. Ownership of units, camps and depots hangs off the seat, never off a network client or a player name.</summary>
    public sealed class Seat
    {
        public SeatId Id { get; }
        public string DisplayName { get; }

        /// <summary>Seats on the same team may use each other's depots and building sites, but never command each other's units.</summary>
        public int Team { get; }

        public SeatController Controller { get; internal set; }

        internal Seat(SeatId id, string displayName, int team, SeatController controller)
        {
            Id = id;
            DisplayName = displayName;
            Team = team;
            Controller = controller;
        }
    }
}
