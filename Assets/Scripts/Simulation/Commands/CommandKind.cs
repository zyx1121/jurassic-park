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

        /// <summary>Place a building site of the catalog definition in Argument at the target cell; the actors haul its materials and build it.</summary>
        Build = 6,

        /// <summary>Remove the target building or site the seat owns. Undelivered and delivered-but-unused materials fall to the ground; built-in ones are gone.</summary>
        Demolish = 7,

        /// <summary>Open a closed gate or close an open one. Closing fails while something stands in it.</summary>
        ToggleGate = 8,

        /// <summary>Attack the target entity, breaking through what is in the way if the actor can.</summary>
        Attack = 9,
    }
}
