namespace JurassicPark.Simulation
{
    /// <summary>
    /// Kind-specific half of command processing. The router has already checked seat, actors, liveness and ownership;
    /// the handler checks what only it knows (ability, target) and then turns the intent into tasks.
    /// </summary>
    public interface ICommandHandler
    {
        /// <summary>Must not change any state.</summary>
        CommandRejection Validate(World world, Command command);

        /// <summary>Called only after Validate returned None, in the same tick.</summary>
        void Execute(World world, Command command);
    }
}
