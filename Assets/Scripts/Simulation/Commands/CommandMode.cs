namespace JurassicPark.Simulation
{
    /// <summary>How a command relates to the work its actors already have.</summary>
    public enum CommandMode
    {
        /// <summary>End the current work and start this.</summary>
        Replace = 1,

        /// <summary>Start this after the current work.</summary>
        Queue = 2,
    }
}
