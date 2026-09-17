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

        private static SimulationRuntime Build() => SimulationRuntime.Build(
            Load<SimulationSettingsAsset>("Simulation"), Load<MapDefinitionAsset>("Map"), Load<EntityCatalogAsset>("Catalog"), Load<ScenarioAsset>("Scenario"));

        [Test]
        public void TheGeneratedAssetsBuildAValidMatch()
        {
            SimulationRuntime runtime = Build();

            Assert.That(runtime.Map.Definition.Validate(), Is.Empty);
            Assert.That(runtime.Map.Definition.Camps, Has.Count.EqualTo(1));
            Assert.That(runtime.World.Entities.Count(e => e.DefinitionId == "survivor"), Is.EqualTo(4));
            Assert.That(runtime.World.Entities.Count(e => e.Kind == EntityKind.ResourceNode), Is.EqualTo(14));
            Entity depot = runtime.World.Entities.Single(e => e.DefinitionId == "depot");
            Assert.That(runtime.Map.FootprintOf(depot.Id), Has.Count.EqualTo(4), "the 2x2 depot blocks its four cells");
            Assert.That(runtime.World.PendingEventCount, Is.EqualTo(19), "the setup batch is waiting for the first frame to drain it");
        }

        [Test]
        public void ThreeSurvivorsOrderedOntoATreeBringAllOfItHome()
        {
            SimulationRuntime runtime = Build();
            EntityId[] workers = runtime.World.Entities.Where(e => e.DefinitionId == "survivor" && e.Owner == runtime.LocalSeat).Select(e => e.Id).ToArray();
            Entity tree = runtime.World.Entities.First(e => e.Kind == EntityKind.ResourceNode);
            Entity depot = runtime.World.Entities.Single(e => e.DefinitionId == "depot");
            OrderResolver.Order order = OrderResolver.Resolve(runtime, workers, tree, tree.Position);
            Assert.That(order.Kind, Is.EqualTo(CommandKind.Gather));

            runtime.LocalSender.Send(order.Kind, workers, order.Point, order.Target);
            for (int i = 0; i < 1200 && (i < 10 || workers.Any(w => runtime.Tasks.CurrentOf(w) != null)); i++) runtime.World.Step();

            runtime.Logistics.TryGetContainer(depot.Id, out Container store);
            Assert.That(store.AmountOf("wood"), Is.EqualTo(40));
            Assert.That(runtime.Logistics.LiveReservationCount, Is.EqualTo(0));
            Assert.That(runtime.World.IsFaulted, Is.False);
        }

        [Test]
        public void ARightClickMeansWhatIsUnderIt()
        {
            SimulationRuntime runtime = Build();
            EntityId[] worker = { runtime.World.Entities.First(e => e.DefinitionId == "survivor" && e.Owner == runtime.LocalSeat).Id };
            Entity tree = runtime.World.Entities.First(e => e.Kind == EntityKind.ResourceNode);
            Entity depot = runtime.World.Entities.Single(e => e.DefinitionId == "depot");
            var ground = new SimVector2(40f, 30f);

            Assert.That(OrderResolver.Resolve(runtime, worker, null, ground).Kind, Is.EqualTo(CommandKind.Move));
            Assert.That(OrderResolver.Resolve(runtime, worker, tree, ground).Kind, Is.EqualTo(CommandKind.Gather));
            Assert.That(OrderResolver.Resolve(runtime, worker, depot, ground).Kind, Is.EqualTo(CommandKind.Move), "empty hands: just walk to the depot");

            runtime.Logistics.Gather(tree.Id, worker[0], 3, null, null);
            OrderResolver.Order deliver = OrderResolver.Resolve(runtime, worker, depot, ground);
            Assert.That(deliver.Kind, Is.EqualTo(CommandKind.Deliver));
            Assert.That(deliver.Target, Is.EqualTo(depot.Id));
        }

        [Test]
        public void PickingTakesTheNearestLivingEntityWithinReachAndRespectsTheFilter()
        {
            SimulationRuntime runtime = Build();
            Entity tree = runtime.World.Entities.First(e => e.Kind == EntityKind.ResourceNode);

            Assert.That(OrderResolver.Pick(runtime.World, tree.Position + new SimVector2(0.5f, 0f), 1.4f), Is.SameAs(tree));
            Assert.That(OrderResolver.Pick(runtime.World, tree.Position, 1.4f, e => e.Kind == EntityKind.Unit), Is.Null);
            Assert.That(OrderResolver.Pick(runtime.World, new SimVector2(90f, 5f), 1.4f), Is.Null);
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
        public void TheTerrainIsOneMeshWithOneSubmesh()
        {
            SimulationRuntime runtime = Build();
            Mesh mesh = TerrainMeshBuilder.Build(runtime.Map.Definition, Color.green, Color.yellow, Color.gray, 2f);

            Assert.That(mesh.subMeshCount, Is.EqualTo(1), "one draw call for the whole terrain");
            Assert.That(mesh.vertexCount, Is.GreaterThanOrEqualTo(48 * 32 * 4));
            Assert.That(mesh.vertexCount, Is.LessThan(48 * 32 * 4 * 2), "hidden cliff faces are not emitted");
            Assert.That(mesh.bounds.size.x, Is.EqualTo(96f).Within(0.01f));
            Object.DestroyImmediate(mesh);
        }
    }
}
