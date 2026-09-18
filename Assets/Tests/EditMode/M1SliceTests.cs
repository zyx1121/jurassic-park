using System.Collections.Generic;
using System.Linq;
using JurassicPark.Presentation;
using JurassicPark.Simulation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Tests.EditMode
{
    /// <summary>Runs the generated M1 data, the same assets the scene loads, without a scene.</summary>
    public sealed class M1SliceTests
    {
        private static T Load<T>(string name) where T : Object => AssetDatabase.LoadAssetAtPath<T>($"Assets/Data/M1/{name}.asset");

        private static MatchReadModel ModelOf(SimulationRuntime runtime)
        {
            var catalog = Load<EntityCatalogAsset>("Catalog");
            var model = new MatchReadModel(catalog, runtime.Map.Definition) { LocalSeat = runtime.LocalSeat };
            var seats = new List<SeatSnapshot>();
            SnapshotCapture.Seats(runtime, seats);
            model.SetSeats(seats);
            Refresh(model, runtime);
            return model;
        }

        private static void Refresh(MatchReadModel model, SimulationRuntime runtime)
        {
            var entities = new List<EntitySnapshot>();
            SnapshotCapture.Entities(runtime, model.Catalog, entities);
            model.Apply(model.Revision == 0 ? runtime.World.Tick : model.Tick + 1, entities);
        }

        private static CommandSender SenderOf(SimulationRuntime runtime)
        {
            runtime.Seats.TryGet(runtime.LocalSeat, out Seat seat);
            return new CommandSender(runtime.Router, runtime.LocalSeat, seat.ControllerEpoch);
        }

        private static SimulationRuntime BuildWithMatch() => SimulationRuntime.Build(
            Load<SimulationSettingsAsset>("Simulation"), Load<MapDefinitionAsset>("Map"), Load<EntityCatalogAsset>("Catalog"), Load<ScenarioAsset>("Scenario"), Load<MatchRulesAsset>("MatchRules"));

        private static SimulationRuntime Build() => SimulationRuntime.Build(
            Load<SimulationSettingsAsset>("Simulation"), Load<MapDefinitionAsset>("Map"), Load<EntityCatalogAsset>("Catalog"), Load<ScenarioAsset>("Scenario"));

        [Test]
        public void TheGeneratedAssetsBuildAValidMatch()
        {
            SimulationRuntime runtime = Build();

            Assert.That(runtime.Map.Definition.Validate(), Is.Empty);
            Assert.That(runtime.Map.Definition.Camps, Has.Count.EqualTo(1));
            Assert.That(runtime.World.Entities.Count(e => e.DefinitionId == "survivor"), Is.EqualTo(4));
            Assert.That(runtime.World.Entities.Count(e => e.DefinitionId == "raptor"), Is.EqualTo(2));
            Assert.That(runtime.World.Entities.Count(e => e.Kind == EntityKind.ResourceNode), Is.EqualTo(14));
            Entity depot = runtime.World.Entities.Single(e => e.DefinitionId == "depot");
            Assert.That(runtime.Map.FootprintOf(depot.Id), Has.Count.EqualTo(4), "the 2x2 depot blocks its four cells");
            Assert.That(runtime.World.PendingEventCount, Is.EqualTo(21), "the setup batch is waiting for the first frame to drain it");
        }

        [Test]
        public void ThreeSurvivorsOrderedOntoATreeBringAllOfItHome()
        {
            SimulationRuntime runtime = Build();
            EntityId[] workers = runtime.World.Entities.Where(e => e.DefinitionId == "survivor" && e.Owner == runtime.LocalSeat).Select(e => e.Id).ToArray();
            Entity tree = runtime.World.Entities.First(e => e.Kind == EntityKind.ResourceNode);
            Entity depot = runtime.World.Entities.Single(e => e.DefinitionId == "depot");
            MatchReadModel model = ModelOf(runtime);
            model.TryGet(tree.Id, out EntitySnapshot treeSnapshot);
            OrderResolver.Order order = OrderResolver.Resolve(model, workers, treeSnapshot, tree.Position);
            Assert.That(order.Kind, Is.EqualTo(CommandKind.Gather));

            SenderOf(runtime).Send(order.Kind, workers, order.Point, order.Target);
            for (int i = 0; i < 1200 && (i < 10 || workers.Any(w => runtime.Tasks.CurrentOf(w) != null)); i++) runtime.World.Step();

            runtime.Logistics.TryGetContainer(depot.Id, out Container store);
            Assert.That(store.AmountOf("wood"), Is.EqualTo(60 + 40), "the starting stock plus the whole tree");
            Assert.That(runtime.Logistics.LiveReservationCount, Is.EqualTo(0));
            Assert.That(runtime.World.IsFaulted, Is.False);
        }

        [Test]
        public void AnOrderOnATargetResolvesToTheRightCommand()
        {
            SimulationRuntime runtime = Build();
            EntityId[] worker = { runtime.World.Entities.First(e => e.DefinitionId == "survivor" && e.Owner == runtime.LocalSeat).Id };
            Entity tree = runtime.World.Entities.First(e => e.Kind == EntityKind.ResourceNode);
            Entity depot = runtime.World.Entities.Single(e => e.DefinitionId == "depot");
            var ground = new SimVector2(40f, 30f);
            MatchReadModel model = ModelOf(runtime);
            EntitySnapshot Snap(Entity e) { model.TryGet(e.Id, out EntitySnapshot snapshot); return snapshot; }

            Assert.That(OrderResolver.Resolve(model, worker, null, ground).Kind, Is.EqualTo(CommandKind.Move));
            Assert.That(OrderResolver.Resolve(model, worker, Snap(tree), ground).Kind, Is.EqualTo(CommandKind.Gather));
            Assert.That(OrderResolver.Resolve(model, worker, Snap(depot), ground).Kind, Is.EqualTo(CommandKind.Move), "empty hands: just walk to the depot");

            runtime.Logistics.Gather(tree.Id, worker[0], 3, null, null);
            Refresh(model, runtime);
            OrderResolver.Order deliver = OrderResolver.Resolve(model, worker, Snap(depot), ground);
            Assert.That(deliver.Kind, Is.EqualTo(CommandKind.Deliver));
            Assert.That(deliver.Target, Is.EqualTo(depot.Id));
        }

        [Test]
        public void AScenarioThatPutsSomethingOnACliffIsRefusedWithTheReason()
        {
            var scenario = Object.Instantiate(Load<ScenarioAsset>("Scenario"));
            scenario.placements = scenario.placements.Append(new ScenarioAsset.Placement { definitionId = "survivor", ownerSeat = 1, cellX = 0, cellY = 0 }).ToArray();

            var error = Assert.Throws<System.InvalidOperationException>(() => SimulationRuntime.Build(
                Load<SimulationSettingsAsset>("Simulation"), Load<MapDefinitionAsset>("Map"), Load<EntityCatalogAsset>("Catalog"), scenario));
            Assert.That(error.Message, Does.Contain("not free walkable ground"));
            Object.DestroyImmediate(scenario);
        }

        [Test]
        public void ADuplicateCatalogIdIsReportedAsACatalogProblem()
        {
            var catalog = Object.Instantiate(Load<EntityCatalogAsset>("Catalog"));
            catalog.entries = catalog.entries.Append(catalog.entries[0]).ToArray();

            var error = Assert.Throws<System.InvalidOperationException>(() => SimulationRuntime.Build(
                Load<SimulationSettingsAsset>("Simulation"), Load<MapDefinitionAsset>("Map"), catalog, Load<ScenarioAsset>("Scenario")));
            Assert.That(error.Message, Does.Contain("appears more than once"));
            Object.DestroyImmediate(catalog);
        }

        [Test]
        public void TheTerrainIsOneMeshWithOneSubmesh()
        {
            SimulationRuntime runtime = Build();
            Mesh mesh = TerrainMeshBuilder.Build(runtime.Map.Definition, Color.green, Color.yellow, Color.gray, 2f);

            Assert.That(mesh.subMeshCount, Is.EqualTo(1), "one draw call for the whole terrain");
            // One quad per cell, plus one side quad per cliff face that looks onto walkable ground, and none for hidden faces.
            MapDefinition map = runtime.Map.Definition;
            int visibleSides = 0;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    if (map.IsStaticWalkable(new Cell(x, y))) continue;
                    foreach ((int dx, int dy) in new[] { (0, -1), (0, 1), (-1, 0), (1, 0) })
                        if (map.IsStaticWalkable(new Cell(x + dx, y + dy))) visibleSides++;
                }
            Assert.That(visibleSides, Is.GreaterThan(0));
            Assert.That(mesh.vertexCount, Is.EqualTo((48 * 32 + visibleSides) * 4), "hidden cliff faces are not emitted");
            Assert.That(mesh.bounds.size.x, Is.EqualTo(96f).Within(0.01f));
            Object.DestroyImmediate(mesh);
        }

        [Test]
        public void ABuildOrderOnTheEntrancePutsUpAWallFromTheDepotsStartingStock()
        {
            SimulationRuntime runtime = Build();
            Entity depot = runtime.World.Entities.Single(e => e.DefinitionId == "depot");
            runtime.Logistics.TryGetContainer(depot.Id, out Container store);
            Assert.That(store.AmountOf("wood"), Is.EqualTo(60), "the scenario seeds the depot");
            EntityId[] workers = runtime.World.Entities.Where(e => e.DefinitionId == "survivor" && e.Owner == runtime.LocalSeat).Select(e => e.Id).ToArray();
            var entrance = new Cell(21, 15);
            Assert.That(runtime.Map.IsWalkable(entrance), Is.True);

            SenderOf(runtime).Send(CommandKind.Build, workers, runtime.Map.CenterOf(entrance), argument: runtime.Catalog.IndexOf("wall"));
            runtime.World.Step();
            Assert.That(runtime.World.DrainEvents().OfType<CommandResolved>().Single().Accepted, Is.True);
            Assert.That(runtime.Map.IsWalkable(entrance), Is.False, "the site blocks at once");
            for (int i = 0; i < 1500 && runtime.Structures.SiteCount > 0; i++) runtime.World.Step();

            Assert.That(runtime.Structures.SiteCount, Is.EqualTo(0), "built");
            Assert.That(store.AmountOf("wood"), Is.EqualTo(54));
            Assert.That(runtime.Map.IsDestructibleBlocker(entrance), Is.True);
            Assert.That(runtime.Logistics.LiveReservationCount, Is.EqualTo(0));
        }

        [Test]
        public void AWorkerLuredIntoARaptorsSightIsHuntedDownWithoutAnyOrderToTheRaptor()
        {
            SimulationRuntime runtime = Build();
            EntityId[] workers = runtime.World.Entities.Where(e => e.DefinitionId == "survivor" && e.Owner == runtime.LocalSeat).Select(e => e.Id).ToArray();
            int survivorsBefore = runtime.World.Entities.Count(e => e.DefinitionId == "survivor");

            // Raptors start far east with 28 m of perception; nobody is in reach at first.
            for (int i = 0; i < 50; i++) runtime.World.Step();
            Assert.That(runtime.World.Entities.Count(e => e.DefinitionId == "survivor"), Is.EqualTo(survivorsBefore));

            // Lure: a worker walks out to the eastern grove, into a raptor's sight.
            Entity bait = runtime.World.Entities.First(e => e.Id == workers[0]);
            SenderOf(runtime).Send(CommandKind.Move, new[] { bait.Id }, runtime.Map.CenterOf(new Cell(40, 20)));
            for (int i = 0; i < 1200 && runtime.World.IsAlive(bait.Id); i++) runtime.World.Step();

            Assert.That(runtime.World.IsAlive(bait.Id), Is.False, "the bait was hunted down by a raptor acting on its own");
            Assert.That(runtime.World.IsFaulted, Is.False);
        }

        [Test]
        public void TheGeneratedMatchRulesAreTheOriginals()
        {
            var rules = Load<MatchRulesAsset>("MatchRules").ToRules();
            Assert.That(rules.SelectionWindowSeconds, Is.EqualTo(20f));
            Assert.That(rules.Modes.Select(m => m.SurvivalSeconds), Is.EqualTo(new[] { 1800f, 2700f, 3600f }));
            Assert.That(rules.HelicopterWindowSeconds, Is.EqualTo(300f));
            Assert.That((rules.StartTimeOfDay, rules.FreezeTimeOfDayAtEvacuation, rules.DayLengthSeconds), Is.EqualTo((17.5f, 3f, 480f)));
            Assert.That(rules.SpawnTimers, Has.Count.EqualTo(31));
            Assert.That(rules.DifficultyCount, Is.EqualTo(6));
            var catalog = Load<EntityCatalogAsset>("Catalog");
            foreach (SpawnTimerRule timer in rules.SpawnTimers)
                foreach (var alternative in timer.Batch.Alternatives)
                    foreach (var unit in alternative)
                        Assert.That(catalog.TryGet(unit.Key, out _), Is.True, $"timer {timer.Id} spawns '{unit.Key}', which is not in the catalog");
        }

        [Test]
        public void WithTheOriginalRulesTheMatchWaitsTwentySecondsThenTheTimersStartFilling()
        {
            SimulationRuntime runtime = BuildWithMatch();
            Assert.That(runtime.Match.Phase, Is.EqualTo(MatchPhase.Setup));
            for (int i = 0; i < 200; i++) runtime.World.Step();
            Assert.That(runtime.Match.Phase, Is.EqualTo(MatchPhase.Survival));
            Assert.That(runtime.Match.SecondsLeft, Is.EqualTo(1800).Within(0.01));
            int before = runtime.World.Entities.Count(e => e.Owner == new SeatId(8));
            for (int i = 0; i < 1200; i++) runtime.World.Step();
            Assert.That(runtime.World.Entities.Count(e => e.Owner == new SeatId(8)), Is.GreaterThan(before), "within two minutes on normal difficulty the timers have spawned something");
            Assert.That(runtime.World.IsFaulted, Is.False);
        }
    }
}
