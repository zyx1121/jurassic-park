using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Kind-specific half of command processing. The router has already checked the seat, the epoch and id, and that every
    /// actor exists and belongs to the seat. It passes the actors that are alive right now; always use that list, never
    /// <see cref="Command.Actors"/>, which may still name a unit that died between the click and this tick. The list is reused by the router: read it, do not keep it.
    /// </summary>
    public interface ICommandHandler
    {
        /// <summary>Must not change any state.</summary>
        CommandRejection Validate(World world, Command command, IReadOnlyList<Entity> livingActors);

        /// <summary>Called only after Validate returned None, immediately afterwards in the same tick.</summary>
        void Execute(World world, Command command, IReadOnlyList<Entity> livingActors);
    }
}
