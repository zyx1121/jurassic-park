namespace JurassicPark.Simulation
{
    /// <summary>What a command asks for. Values are wire format: never renumber, only append.</summary>
    public enum CommandKind
    {
        Stop = 1,
        Move = 2,
    }
}
