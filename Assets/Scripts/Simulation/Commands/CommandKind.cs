namespace JurassicPark.Simulation
{
    /// <summary>What a command asks for. Values are wire format: never renumber, only append.</summary>
    public enum CommandKind
    {
        Stop = 1,
        Move = 2,

        /// <summary>Gather from the target node and keep carrying to a depot until the node runs out.</summary>
        Gather = 3,

        /// <summary>Put everything carried into the target container.</summary>
        Deliver = 4,

        /// <summary>Take goods from the target pile or depot, up to what the pack holds.</summary>
        Pickup = 5,
    }
}
