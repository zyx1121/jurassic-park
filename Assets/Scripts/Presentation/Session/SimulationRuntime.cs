using System;
using System.Collections.Generic;
using JurassicPark.Simulation;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// A running match: the world and every system around it, built from assets. No MonoBehaviour, so an EditMode test can build
    /// and step exactly what the scene runs.
    /// </summary>
    public sealed class SimulationRuntime
    {
        public World World { get; private set; }
        public GridMap Map { get; private set; }
        public DefinitionCatalog Catalog { get; private set; }
        public SeatRegistry Seats { get; private set; }
        public CommandRouter Router { get; private set; }
        public TaskSystem Tasks { get; private set; }
        public Logistics Logistics { get; private set; }
        public Vitals Vitals { get; private set; }
        public Structures Structures { get; private set; }
        public SeatId LocalSeat { get; private set; }

        /// <summary>Seats a player may sit in, in scenario order. The host's own seat is among them.</summary>
        public IReadOnlyList<SeatId> PlayableSeats { get; private set; }

        /// <summary>Builds the match, or throws with every problem found, so an illegal map never pretends to be ready.</summary>
        public static SimulationRuntime Build(SimulationSettingsAsset settings, MapDefinitionAsset mapAsset, EntityCatalogAsset catalogAsset, ScenarioAsset scenario)
        {
            if (settings == null || mapAsset == null || catalogAsset == null || scenario == null)
                throw new ArgumentNullException(nameof(settings), "Settings, map, catalog and scenario are all required.");

            var problems = new List<string>();
            if (!mapAsset.TryToDefinition(out MapDefinition mapDefinition, out IReadOnlyList<string> conversion)) problems.AddRange(conversion);
            if (mapDefinition != null) problems.AddRange(mapDefinition.Validate());
            if (problems.Count > 0) throw new InvalidOperationException("The map is not usable:\n- " + string.Join("\n- ", problems));

            var seen = new HashSet<string>();
            for (int i = 0; i < catalogAsset.entries.Length; i++)
            {
                string id = catalogAsset.entries[i].id;
                if (string.IsNullOrEmpty(id)) problems.Add($"Catalog entry #{i} has no id.");
                else if (!seen.Add(id)) problems.Add($"Catalog id '{id}' appears more than once.");
            }
            if (problems.Count > 0) throw new InvalidOperationException("The catalog is not usable:\n- " + string.Join("\n- ", problems));

            var runtime = new SimulationRuntime
            {
                World = new World(settings.ToSimConfig()),
                Map = new GridMap(mapDefinition),
                Catalog = catalogAsset.ToCatalog(),
            };
            runtime.Logistics = new Logistics(runtime.World, settings.ToLogisticsConfig());
            runtime.Seats = new SeatRegistry(runtime.World);
            for (int i = 0; i < scenario.seats.Length; i++)
            {
                ScenarioAsset.SeatEntry seat = scenario.seats[i];
                runtime.Seats.Add(new SeatId(seat.id), seat.displayName, seat.team, seat.controller);
            }
            runtime.Vitals = new Vitals(runtime.World);
            runtime.Structures = new Structures(runtime.World, runtime.Map, runtime.Logistics, runtime.Vitals, runtime.Seats);
            runtime.Tasks = new TaskSystem(new TaskContext(runtime.World, runtime.Map, runtime.Catalog, settings.ToTaskConfig(), runtime.Logistics, runtime.Seats, runtime.Structures, runtime.Vitals));
            runtime.Router = new CommandRouter(runtime.Seats, settings.ToRouterConfig());
            runtime.Router.Register(CommandKind.Move, new MoveCommandHandler(runtime.Tasks));
            runtime.Router.Register(CommandKind.Stop, new StopCommandHandler(runtime.Tasks));
            runtime.Router.Register(CommandKind.Gather, new HaulCommandHandler(runtime.Tasks, CommandKind.Gather));
            runtime.Router.Register(CommandKind.Deliver, new HaulCommandHandler(runtime.Tasks, CommandKind.Deliver));
            runtime.Router.Register(CommandKind.Pickup, new HaulCommandHandler(runtime.Tasks, CommandKind.Pickup));
            runtime.Router.Register(CommandKind.Build, new BuildCommandHandler(runtime.Tasks));
            runtime.Router.Register(CommandKind.Demolish, new StructureCommandHandler(runtime.Tasks, CommandKind.Demolish));
            runtime.Router.Register(CommandKind.ToggleGate, new StructureCommandHandler(runtime.Tasks, CommandKind.ToggleGate));
            runtime.Router.Register(CommandKind.Attack, new AttackCommandHandler(runtime.Tasks));
            // Order is the contract: input, then work, then the books.
            runtime.World.AddSystem(runtime.Router);
            runtime.World.AddSystem(runtime.Tasks);
            runtime.World.AddSystem(runtime.Logistics);
            runtime.World.AddSystem(runtime.Structures);
            // Instinct submits commands, which the router takes at the next tick boundary, so its place in the order does not matter for correctness.
            runtime.World.AddSystem(new Predators(runtime.Tasks.Context, runtime.Router, settings.predatorScanIntervalTicks));

            var playable = new List<SeatId>();
            for (int i = 0; i < scenario.seats.Length; i++)
                if (scenario.seats[i].playable) playable.Add(new SeatId(scenario.seats[i].id));
            runtime.PlayableSeats = playable;
            runtime.LocalSeat = new SeatId(scenario.localSeat);
            if (!runtime.Seats.TryGet(runtime.LocalSeat, out _)) throw new InvalidOperationException($"Local seat {scenario.localSeat} is not one of the scenario's seats.");

            for (int i = 0; i < scenario.placements.Length; i++) runtime.Place(catalogAsset, scenario.placements[i], problems);
            if (problems.Count > 0) throw new InvalidOperationException("The scenario is not usable:\n- " + string.Join("\n- ", problems));
            runtime.World.Commit();
            return runtime;
        }

        private void Place(EntityCatalogAsset catalogAsset, ScenarioAsset.Placement placement, List<string> problems)
        {
            if (!catalogAsset.TryGet(placement.definitionId, out EntityCatalogAsset.Entry entry))
            {
                problems.Add($"Placement uses unknown definition '{placement.definitionId}'.");
                return;
            }
            var anchor = new Cell(placement.cellX, placement.cellY);
            var footprint = new List<Cell>(entry.footprintWidth * entry.footprintHeight);
            for (int y = 0; y < entry.footprintHeight; y++)
                for (int x = 0; x < entry.footprintWidth; x++)
                    footprint.Add(new Cell(anchor.X + x, anchor.Y + y));
            for (int i = 0; i < footprint.Count; i++)
            {
                if (Map.IsWalkable(footprint[i])) continue;
                problems.Add($"'{entry.id}' at {anchor} stands on {footprint[i]}, which is not free walkable ground.");
                return;
            }

            // The entity sits at the middle of its footprint, so a 2x2 depot is drawn over the four cells it blocks.
            SimVector2 first = Map.CenterOf(footprint[0]), last = Map.CenterOf(footprint[footprint.Count - 1]);
            Entity entity = World.Spawn(entry.kind, entry.id, new SeatId(placement.ownerSeat), (first + last) * 0.5f);
            Catalog.TryGet(entry.id, out EntityDefinition definition);
            Logistics.Attach(entity, definition);
            if (entry.blocks && !Map.TryOccupy(footprint, entity.Id, entry.destructible))
                problems.Add($"'{entry.id}' at {anchor} could not claim its footprint.");
            if (entity.Kind == EntityKind.Building) Structures.AttachBuilt(entity, definition, footprint);
            else if (entry.blocks) Structures.TrackBlocker(entity.Id);
            Vitals.Attach(entity, definition);
            for (int i = 0; i < placement.stock.Length; i++)
                if (Logistics.Seed(entity.Id, placement.stock[i].resource, placement.stock[i].amount) < placement.stock[i].amount)
                    problems.Add($"'{entry.id}' at {anchor} cannot hold its starting stock of {placement.stock[i].amount} {placement.stock[i].resource}.");
        }
    }
}
