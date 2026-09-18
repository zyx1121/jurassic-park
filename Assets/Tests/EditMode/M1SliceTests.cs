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

        private static void Refresh(MatchReadModel model, SimulationRuntime runtime, SnapshotMemory memory = null)
        {
            var entities = new List<EntitySnapshot>();
            SnapshotCapture.Entities(runtime, model.Catalog, entities, runtime.LocalSeat, memory);
            model.Apply(model.Revision == 0 ? runtime.World.Tick : model.Tick + 1, entities);
        }

        private static CommandSender SenderOf(SimulationRuntime runtime)
        {
            runtime.Seats.TryGet(runtime.LocalSeat, out Seat seat);
            return new CommandSender(runtime.Router, runtime.LocalSeat, seat.ControllerEpoch);
        }

        private static SimulationRuntime BuildWithMatch() => SimulationRuntime.Build(
            Load<SimulationSettingsAsset>("Simulation"), Load<MapDefinitionAsset>("Map"), Load<EntityCatalogAsset>("Catalog"), Load<ScenarioAsset>("Scenario"), Load<MatchRulesAsset>("MatchRules"));

        /// <summary>The computer ally is part of the scenario. Tests that count wood or walls to the unit put it to sleep by handing its seat to a human nobody is playing.</summary>
        /// <summary>
        /// The trees start in the dark, and an order on something unseen is a walk, as in the original. So a scout walks toward the
        /// nearest grove until it is on the screen, then comes home, and the tests order onto that tree.
        /// </summary>
        private static Entity ScoutNearestTree(SimulationRuntime runtime, MatchReadModel model, CommandSender sender, EntityId scout, SnapshotMemory memory = null)
        {
            Entity depot = runtime.World.Entities.First(e => e.DefinitionId == "depot" && e.Owner == runtime.LocalSeat);
            Entity tree = runtime.World.Entities.Where(e => e.Kind == EntityKind.ResourceNode).OrderBy(e => SimVector2.Distance(e.Position, depot.Position)).First();
            Assert.That(model.TryGet(tree.Id, out _), Is.False, "the grove starts in the dark");
            sender.Send(CommandKind.Move, new[] { scout }, tree.Position, EntityId.None);
            for (int i = 0; i < 600 && !model.TryGet(tree.Id, out _); i++) { runtime.World.Step(); Refresh(model, runtime, memory); }
            Assert.That(model.TryGet(tree.Id, out _), Is.True, "walking toward the grove reveals it");
            runtime.World.TryGet(scout, out Entity worker);
            sender.Send(CommandKind.Move, new[] { scout }, worker.Position, EntityId.None);   // stop where it stands: the trees stay explored
            for (int i = 0; i < 20; i++) { runtime.World.Step(); Refresh(model, runtime, memory); }
            return tree;
        }

        private static SimulationRuntime WithoutAlly(SimulationRuntime runtime)
        {
            runtime.Seats.SetController(new SeatId(2), SeatController.Human);
            runtime.World.Commit();
            return runtime;
        }

        private static SimulationRuntime Build() => SimulationRuntime.Build(
            Load<SimulationSettingsAsset>("Simulation"), Load<MapDefinitionAsset>("Map"), Load<EntityCatalogAsset>("Catalog"), Load<ScenarioAsset>("Scenario"));

        [Test]
        public void TheGeneratedAssetsBuildAValidMatch()
        {
            SimulationRuntime runtime = Build();

            Assert.That(runtime.Map.Definition.Validate(), Is.Empty);
            Assert.That(runtime.Map.Definition.Camps, Has.Count.EqualTo(2), "the player's camp and the computer ally's");
            Assert.That(runtime.World.Entities.Count(e => e.DefinitionId == "survivor"), Is.EqualTo(4));
            Assert.That(runtime.World.Entities.Count(e => e.DefinitionId == "raptor"), Is.EqualTo(2));
            Assert.That(runtime.World.Entities.Count(e => e.Kind == EntityKind.ResourceNode), Is.EqualTo(17));
            Entity depot = runtime.World.Entities.First(e => e.DefinitionId == "depot" && e.Owner == runtime.LocalSeat);
            Assert.That(runtime.Map.FootprintOf(depot.Id), Has.Count.EqualTo(4), "the 2x2 depot blocks its four cells");
            Assert.That(runtime.World.PendingEventCount, Is.EqualTo(runtime.World.Entities.Count), "the setup batch is waiting for the first frame to drain it");
        }

        [Test]
        public void ThreeSurvivorsOrderedOntoATreeBringAllOfItHome()
        {
            SimulationRuntime runtime = WithoutAlly(Build());
            EntityId[] workers = runtime.World.Entities.Where(e => e.DefinitionId == "survivor" && e.Owner == runtime.LocalSeat).Select(e => e.Id).ToArray();
            Entity depot = runtime.World.Entities.First(e => e.DefinitionId == "depot" && e.Owner == runtime.LocalSeat);
            MatchReadModel model = ModelOf(runtime);
            CommandSender sender = SenderOf(runtime);
            Entity tree = ScoutNearestTree(runtime, model, sender, workers[0]);
            model.TryGet(tree.Id, out EntitySnapshot treeSnapshot);
            OrderResolver.Order order = OrderResolver.Resolve(model, workers, treeSnapshot, tree.Position);
            Assert.That(order.Kind, Is.EqualTo(CommandKind.Gather));

            sender.Send(order.Kind, workers, order.Point, order.Target);
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
            Entity depot = runtime.World.Entities.First(e => e.DefinitionId == "depot" && e.Owner == runtime.LocalSeat);
            var ground = new SimVector2(40f, 30f);
            MatchReadModel model = ModelOf(runtime);
            Entity tree = ScoutNearestTree(runtime, model, SenderOf(runtime), worker[0]);
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
            SimulationRuntime runtime = WithoutAlly(Build());
            Entity depot = runtime.World.Entities.First(e => e.DefinitionId == "depot" && e.Owner == runtime.LocalSeat);
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
            SimulationRuntime runtime = WithoutAlly(Build());
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
            SpawnTimerRule oV = rules.SpawnTimers.Single(t => t.Id == "oV");
            Assert.That(oV.Batch.Weights, Is.EqualTo(new[] { 1, 2 }), "one third allosaurs, two thirds mid-size rexes");
            Assert.That(oV.Batch.Alternatives.All(a => a.Any(u => u.Value == 1)), Is.True, "and a baby rex every time");
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
            SimulationRuntime runtime = WithoutAlly(BuildWithMatch());
            Assert.That(runtime.Match.Phase, Is.EqualTo(MatchPhase.Setup));
            for (int i = 0; i < 200; i++) runtime.World.Step();
            Assert.That(runtime.Match.Phase, Is.EqualTo(MatchPhase.Survival));
            Assert.That(runtime.Match.SecondsLeft, Is.EqualTo(1800).Within(0.01));
            int before = runtime.World.Entities.Count(e => e.Owner == new SeatId(8));
            for (int i = 0; i < 1200; i++) runtime.World.Step();
            Assert.That(runtime.World.Entities.Count(e => e.Owner == new SeatId(8)), Is.GreaterThan(before), "within two minutes on normal difficulty the timers have spawned something");
            Assert.That(runtime.World.IsFaulted, Is.False);
        }

        [Test]
        public void TheComputerAllyStocksItsDepotAndClosesTheCampOnItsOwn()
        {
            SimulationRuntime runtime = Build();
            Entity blueDepot = runtime.World.Entities.Single(e => e.DefinitionId == "depot" && e.Owner == new SeatId(2));
            runtime.Logistics.TryGetContainer(blueDepot.Id, out Container store);
            Entity redDepot = runtime.World.Entities.Single(e => e.DefinitionId == "depot" && e.Owner == runtime.LocalSeat);
            runtime.Logistics.TryGetContainer(redDepot.Id, out Container redStore);
            int before = store.AmountOf("wood"), redBefore = redStore.AmountOf("wood");
            for (int i = 0; i < 3000 && runtime.World.Entities.Count(e => e.Kind == EntityKind.Building && e.Owner == new SeatId(2) && e.DefinitionId != "depot") < 2; i++) runtime.World.Step();

            Assert.That(runtime.World.Entities.Count(e => e.Kind == EntityKind.Building && e.Owner == new SeatId(2) && e.DefinitionId != "depot"), Is.EqualTo(2), "a gate and a wall across its own camp's two entrance cells");
            Assert.That(runtime.World.Entities.Any(e => e.DefinitionId == "gate" && e.Owner == new SeatId(2)), Is.True);
            Assert.That(runtime.World.Entities.Count(e => e.Kind == EntityKind.Building && e.Owner == new SeatId(2) && runtime.Map.Definition.Camps[0].Bounds.Contains(runtime.Map.CellAt(e.Position))), Is.EqualTo(0), "nothing of the ally's stands in the player's camp");
            Assert.That(redStore.AmountOf("wood"), Is.EqualTo(redBefore), "the player's stock is untouched: the gate and the wall were paid for out of its own depot");
            for (int i = 0; i < 1500 && store.AmountOf("wood") <= before - 14; i++) runtime.World.Step();
            Assert.That(store.AmountOf("wood"), Is.GreaterThan(before - 14), "its gatherer restocks the depot after the building spend");
            Assert.That(runtime.World.IsFaulted, Is.False);
        }

        [Test]
        public void ATreeSeenOnceIsRememberedUntilTheTeamLooksAtTheEmptyCellAgain()
        {
            SimulationRuntime runtime = WithoutAlly(Build());
            var memory = new SnapshotMemory();
            MatchReadModel model = ModelOf(runtime);
            CommandSender sender = SenderOf(runtime);
            EntityId scout = runtime.World.Entities.First(e => e.DefinitionId == "survivor" && e.Owner == runtime.LocalSeat).Id;
            Entity depot = runtime.World.Entities.First(e => e.DefinitionId == "depot" && e.Owner == runtime.LocalSeat);
            Entity tree = ScoutNearestTree(runtime, model, sender, scout, memory);
            int team = runtime.Knowledge.TeamOf(runtime.LocalSeat);
            Cell treeCell = runtime.Map.CellAt(tree.Position);

            // Home again: the grove is out of sight, yet it stays on the screen as the team remembers it.
            sender.Send(CommandKind.Move, new[] { scout }, depot.Position, EntityId.None);
            for (int i = 0; i < 600 && runtime.Knowledge.At(team, treeCell) == Visibility.Visible; i++) { runtime.World.Step(); Refresh(model, runtime, memory); }
            Assert.That(runtime.Knowledge.At(team, treeCell), Is.EqualTo(Visibility.Explored));
            Assert.That(model.TryGet(tree.Id, out EntitySnapshot ghost), Is.True, "a tree seen once is remembered where it stood");
            Assert.That(ghost.Remembered, Is.True);
            Assert.That(OrderResolver.Resolve(model, new[] { scout }, ghost, ghost.Position).Kind, Is.EqualTo(CommandKind.Move), "an order on a memory is a walk to where it was");
            Assert.That(memory.Remembers(team, tree.Id), Is.True);

            // Felled in the dark: nobody saw it go, so the memory stands.
            runtime.World.Despawn(tree.Id, "test");
            runtime.World.Step();
            Refresh(model, runtime, memory);
            Assert.That(runtime.World.IsAlive(tree.Id), Is.False);
            Assert.That(model.TryGet(tree.Id, out _), Is.True, "the ghost stays until the team looks again");

            // Look again: the cell is seen empty and the ghost is gone. Without a memory nothing unseen is ever sent.
            sender.Send(CommandKind.Move, new[] { scout }, tree.Position, EntityId.None);
            for (int i = 0; i < 600 && model.TryGet(tree.Id, out _); i++) { runtime.World.Step(); Refresh(model, runtime, memory); }
            Assert.That(model.TryGet(tree.Id, out _), Is.False, "seen empty, forgotten");
            Assert.That(memory.Remembers(team, tree.Id), Is.False);
            Assert.That(memory.RememberedCountOf(team), Is.GreaterThan(0), "the grove's other trees are still remembered");
            Assert.That(runtime.World.IsFaulted, Is.False);
        }

        [Test]
        public void TheSnapshotShowsASeatOnlyWhatItsTeamCanSee()
        {
            SimulationRuntime runtime = WithoutAlly(Build());
            var catalog = Load<EntityCatalogAsset>("Catalog");
            var red = new List<EntitySnapshot>();
            SnapshotCapture.Entities(runtime, catalog, red, runtime.LocalSeat);
            var dinos = new List<EntitySnapshot>();
            SnapshotCapture.Entities(runtime, catalog, dinos, new SeatId(8));

            Assert.That(red.Count(e => e.Kind == EntityKind.Unit && e.Owner == new SeatId(8)), Is.EqualTo(0), "the raptors start far outside anyone's sight");
            Assert.That(red.Count(e => e.Kind == EntityKind.Unit && e.Owner.Value <= 2), Is.EqualTo(4), "own and allied survivors are always known");
            Assert.That(red.Count(e => e.Kind == EntityKind.ResourceNode), Is.LessThan(14), "unexplored groves are not on the screen yet");
            Assert.That(dinos.Count(e => e.Owner == new SeatId(8)), Is.EqualTo(2));
            Assert.That(dinos.Count(e => e.DefinitionId(catalog) == "depot"), Is.EqualTo(0), "the dinosaurs have not seen the camp");

            int team = runtime.Knowledge.TeamOf(runtime.LocalSeat);
            Assert.That(runtime.Knowledge.VisibleCountOf(team), Is.GreaterThan(0));
            Assert.That(runtime.Knowledge.CellsOf(team).Count(c => c == 0), Is.GreaterThan(runtime.Map.Width * runtime.Map.Height / 2), "most of the map is still dark");
        }
    }

    internal static class SnapshotTestExtensions
    {
        public static string DefinitionId(this EntitySnapshot snapshot, EntityCatalogAsset catalog) =>
            snapshot.DefinitionIndex < catalog.entries.Length ? catalog.entries[snapshot.DefinitionIndex].id : "?";
    }
}
