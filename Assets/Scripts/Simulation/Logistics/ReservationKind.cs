namespace JurassicPark.Simulation
{
    public enum ReservationKind
    {
        /// <summary>Goods in a container promised to someone who will come and take them.</summary>
        Withdrawal = 1,

        /// <summary>Free room in a container promised to someone who will come and put goods in.</summary>
        Deposit = 2,

        /// <summary>Ungathered stock in a resource node promised to a gatherer.</summary>
        NodeStock = 3,
    }
}
