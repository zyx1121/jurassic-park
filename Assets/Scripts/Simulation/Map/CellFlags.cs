using System;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// What the fixed terrain allows on a cell before anything is built on it. Walking and building are separate
    /// because the original map has open ground no camp may be raised on, and the two questions come from different
    /// systems: pathing asks Walkable, blueprint validation asks Buildable.
    /// </summary>
    [Flags]
    public enum CellFlags
    {
        /// <summary>Cliff, water or map edge: never walkable, never buildable, never breachable.</summary>
        None = 0,
        Walkable = 1,
        Buildable = 2
    }
}
