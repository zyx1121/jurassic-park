namespace JurassicPark.Simulation
{
    /// <summary>Who is currently deciding for a seat. It changes during a match: a computer ally takes over a dropped human and hands the seat back on reconnect.</summary>
    public enum SeatController
    {
        Human = 1,
        Computer = 2,
    }
}
