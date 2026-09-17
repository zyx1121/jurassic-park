using System;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace JurassicPark.Infrastructure.SceneTests
{
    public sealed class InfrastructureMapTests
    {
        private InfrastructureMapDefinition definition;

        [SetUp]
        public void SetUp() => definition = ScriptableObject.CreateInstance<InfrastructureMapDefinition>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(definition);

        [Test]
        public void FixedMapHasTwelveDistinctCampsAndIndependentTerrainAndNavigation()
        {
            InfrastructureLayout layout = definition.CreateLayout();
            Assert.That(layout.Map.Camps.Count, Is.EqualTo(12));
            Assert.That(layout.Map.Camps.Distinct().Count(), Is.EqualTo(12));
            Assert.That(layout.Map.Width * layout.Map.CellSize, Is.EqualTo(256f));
            Assert.That(layout.Surfaces.Count(s => s == MapSurface.Water), Is.GreaterThan(0));
            Assert.That(layout.Surfaces.Count(s => s == MapSurface.Ridge), Is.GreaterThan(0));
            Assert.That(layout.Surfaces.Count(s => s == MapSurface.Clearing), Is.GreaterThan(0));
            foreach (InfrastructureMapDefinition.Camp camp in definition.camps)
            {
                Assert.That(layout.Map.TileAt(camp.Gate), Is.EqualTo(InfraTileKind.Ground));
                Assert.That(layout.Map.TileAt(camp.center - camp.entranceDirection * camp.radius), Is.EqualTo(InfraTileKind.Cliff));
            }
        }

        [Test]
        public void RepeatedMapLoadDoesNotRandomizeTerrainOrCampLocations()
        {
            InfrastructureLayout first = definition.CreateLayout();
            UnityEngine.Random.State state = UnityEngine.Random.state;
            UnityEngine.Random.InitState(991);
            try
            {
                InfrastructureLayout second = definition.CreateLayout();
                Assert.That(second.Surfaces, Is.EqualTo(first.Surfaces));
                Assert.That(second.Map.Camps, Is.EqualTo(first.Map.Camps));
            }
            finally { UnityEngine.Random.state = state; }
        }

        [Test]
        public void OverlappingCampsAreAVisibleLoadError()
        {
            definition.camps[1].center = definition.camps[0].center;
            Assert.Throws<InvalidOperationException>(() => definition.CreateLayout());
        }

        [Test]
        public void InvalidEntranceIsAVisibleLoadError()
        {
            definition.camps[0].entranceDirection = Vector2Int.zero;
            Assert.Throws<InvalidOperationException>(() => definition.CreateLayout());
        }

        [Test]
        public void EmptyEventZoneIsAVisibleLoadError()
        {
            definition.eventZones[0] = new RectInt(1, 1, 0, 0);
            Assert.Throws<InvalidOperationException>(() => definition.CreateLayout());
        }

        [Test]
        public void EveryCampRemainsReachableAfterFixedResourceAndDepotPlacement()
        {
            InfrastructureLayout layout = definition.CreateLayout();
            var world = new InfraWorld(layout.Map, new InfraRules());
            foreach (InfrastructureMapDefinition.Camp camp in definition.camps)
            {
                world.AddEntity(InfraEntityKind.Source, camp.Source, 0, 60);
                world.AddEntity(InfraEntityKind.Depot, camp.Depot);
            }
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, definition.arrival);
            long id = 0;
            foreach (InfrastructureMapDefinition.Camp camp in definition.camps)
            {
                InfraCommandResult result = world.Submit(new InfraCommand(++id, 0, worker.Id, InfraCommandKind.Move, camp.center));
                Assert.That(result.Accepted, Is.True, $"{camp.name}: {result.Reason}");
                for (int i = 0; i < 1500 && worker.TaskStatus != InfraTaskStatus.Completed; i++) world.Tick(.1f);
                Assert.That(worker.TaskStatus, Is.EqualTo(InfraTaskStatus.Completed), $"{camp.name}: {worker.Action} {worker.Reason}");
                Assert.That(Vector2.Distance(worker.Position, layout.Map.CellCenter(camp.center)), Is.LessThan(.01f));
            }
        }

        [Test]
        public void GeneratedAssetsRetainMapSpriteAndStaticFontReferences()
        {
            var config = AssetDatabase.LoadAssetAtPath<InfrastructureConfig>("Assets/Data/Infrastructure/Simulation.asset");
            Assert.That(config, Is.Not.Null, "Run build_infrastructure before scene integration tests.");
            Assert.That(config.map, Is.Not.Null);
            Assert.That(config.survivorSprites.Find("Idle").sheet, Is.Not.Null);
            Assert.That(config.dinosaurSprites.Find("Walk").sheet, Is.Not.Null);
            Assert.That(config.font.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Static));
            Assert.That(AssetDatabase.Contains(config.font.material), Is.True);
            Assert.That(AssetDatabase.Contains(config.font.atlasTexture), Is.True);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Data/Generated/InfrastructureTerrain.asset");
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.vertexCount, Is.GreaterThan(config.map.width * config.map.height));
            Assert.That(AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Infrastructure.unity"), Is.Not.Null);
        }
    }
}
