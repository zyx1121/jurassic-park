namespace JurassicPark.Simulation
{
    /// <summary>
    /// A limit on what others may count on, not a second copy of the goods: a depot holding 100 with 30 reserved still holds 100,
    /// and offers 70. Owned by a task, and it expires unless renewed, so a task that vanished without releasing cannot hold goods forever.
    /// </summary>
    public sealed class Reservation
    {
        public ReservationId Id { get; }
        public ReservationKind Kind { get; }
        public EntityId Holder { get; }
        public string Resource { get; }
        public int Amount { get; internal set; }
        public TaskId Owner { get; }
        public long ExpiresAtTick { get; internal set; }

        internal Reservation(ReservationId id, ReservationKind kind, EntityId holder, string resource, int amount, TaskId owner, long expiresAtTick)
        {
            Id = id;
            Kind = kind;
            Holder = holder;
            Resource = resource;
            Amount = amount;
            Owner = owner;
            ExpiresAtTick = expiresAtTick;
        }
    }
}
