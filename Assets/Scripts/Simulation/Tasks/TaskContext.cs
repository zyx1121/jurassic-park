using System;

namespace JurassicPark.Simulation
{
    /// <summary>What a task may touch while it runs: the world, the map, content data and its own settings. Passed in, so tasks hold no global state.</summary>
    public sealed class TaskContext
    {
        public World World { get; }
        public GridMap Map { get; }
        public DefinitionCatalog Catalog { get; }
        public TaskConfig Config { get; }

        public TaskContext(World world, GridMap map, DefinitionCatalog catalog, TaskConfig config)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>Seconds simulated by one tick.</summary>
        public float TickSeconds => (float)World.Config.TickSeconds;
    }
}
