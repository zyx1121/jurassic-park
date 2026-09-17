namespace JurassicPark.Simulation
{
    /// <summary>A seat passed between a human and a computer ally.</summary>
    public sealed class SeatControllerChanged : SimEvent
    {
        public SeatId Seat { get; }
        public SeatController Controller { get; }

        /// <summary>The epoch the new controller must stamp on its commands, with ids starting again from 1.</summary>
        public int Epoch { get; }

        public SeatControllerChanged(SeatId seat, SeatController controller, int epoch)
        {
            Seat = seat;
            Controller = controller;
            Epoch = epoch;
        }
    }
}
