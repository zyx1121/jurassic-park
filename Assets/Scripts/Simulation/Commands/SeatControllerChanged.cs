namespace JurassicPark.Simulation
{
    /// <summary>A seat passed between a human and a computer ally.</summary>
    public sealed class SeatControllerChanged : SimEvent
    {
        public SeatId Seat { get; }
        public SeatController Controller { get; }

        public SeatControllerChanged(SeatId seat, SeatController controller)
        {
            Seat = seat;
            Controller = controller;
        }
    }
}
