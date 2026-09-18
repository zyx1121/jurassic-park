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

        /// <summary>Null in a world without goods, for example a pure movement test.</summary>
        public Logistics Logistics { get; }

        /// <summary>Null when nothing needs to ask who may use whose property.</summary>
        public SeatRegistry Seats { get; }

        /// <summary>Null in a world without buildings.</summary>
        public Structures Structures { get; }
        public Vitals Vitals { get; }

        /// <summary>The task system this context belongs to, once it exists. Set by TaskSystem; lets an AI system ask what a unit is doing.</summary>
        public TaskSystem Tasks { get; internal set; }

        public TaskContext(World world, GridMap map, DefinitionCatalog catalog, TaskConfig config, Logistics logistics = null, SeatRegistry seats = null, Structures structures = null, Vitals vitals = null)
        {
            World = world ?? throw new ArgumentNullException(nameof(world));
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Config = config ?? throw new ArgumentNullException(nameof(config));
            Logistics = logistics;
            Seats = seats;
            Structures = structures;
            Vitals = vitals;
        }

        /// <summary>Seconds simulated by one tick.</summary>
        public float TickSeconds => (float)World.Config.TickSeconds;
    }
}
