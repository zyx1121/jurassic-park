using JurassicPark.Scene;
using JurassicPark.World;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    [Category("ClassicPresentation")]
    public class ClassicPresentationTests
    {
        [Test]
        public void TacticalCameraFramesThePlayableViewportFromAbove()
        {
            var config = ScriptableObject.CreateInstance<CameraViewConfig>();
            var go = new GameObject("Tactical camera");
            try
            {
                Camera camera = go.AddComponent<Camera>();
                camera.rect = new Rect(0f, 0.3f, 1f, 0.65f);
                FollowCamera follow = go.AddComponent<FollowCamera>();
                follow.Bounds = new Rect(-100f, -100f, 200f, 200f);
                follow.Configure(config);
                Vector3 ground = new Vector3(4f, 2f, -3f);
                follow.FramePoint(ground);
                Vector3 focus = ground + Vector3.up * config.lookHeight;
                Vector3 point = camera.WorldToViewportPoint(focus);
                Assert.Greater(config.pitch, 38f, "The classic overview must be higher than the old diorama view.");
                Assert.AreEqual(config.pitch, follow.Pitch);
                Assert.AreEqual(config.distance, Vector3.Distance(go.transform.position, focus), 0.001f);
                Assert.AreEqual(config.fieldOfView, camera.fieldOfView);
                Assert.AreEqual(0.5f, point.x, 0.001f);
                Assert.AreEqual(0.5f, point.y, 0.001f, "The focus belongs in the world viewport, not behind the bottom console.");
                Assert.IsFalse(config.depthOfField, "Defensive approaches should not disappear into gameplay blur.");
            }
            finally
            {
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void PaletteReductionDoesNotBleedInvisiblePixelsIntoSilhouettes()
        {
            var source = new[]
            {
                new Color32(255, 0, 255, 0), new Color32(255, 0, 255, 0),
                new Color32(20, 100, 30, 255), new Color32(20, 100, 30, 255)
            };
            var palette = new[] { new Color32(20, 100, 30, 255), new Color32(255, 0, 255, 255) };
            Color32[] result = PixelStyle.Remap(source, 2, 2, 2, palette, out int width, out int height);
            Assert.AreEqual(1, width);
            Assert.AreEqual(1, height);
            Assert.AreEqual(20, result[0].r);
            Assert.AreEqual(100, result[0].g);
            Assert.AreEqual(30, result[0].b);
            Assert.AreEqual(128, result[0].a);
        }

        [Test]
        public void PaletteReductionHandlesPartialBlocksAndUsesOnlySharedColors()
        {
            var source = new Color32[15];
            for (int i = 0; i < source.Length; i++) source[i] = new Color32((byte)(i * 17), 80, 50, 255);
            var palette = new[] { new Color32(25, 70, 40, 255), new Color32(180, 100, 60, 255) };
            Color32[] result = PixelStyle.Remap(source, 5, 3, 2, palette, out int width, out int height);
            Assert.AreEqual(3, width);
            Assert.AreEqual(2, height);
            foreach (Color32 color in result) CollectionAssert.Contains(palette, color);
            CollectionAssert.AreEqual(result, PixelStyle.Remap(source, 5, 3, 2, palette, out _, out _));
        }

        [Test]
        public void InvalidPaletteConfigurationDoesNotSilentlyGenerateBlankArt()
        {
            Assert.Throws<System.ArgumentException>(() => PixelStyle.Remap(new Color32[1], 1, 1, 0,
                new[] { new Color32(1, 2, 3, 255) }, out _, out _));
            Assert.Throws<System.ArgumentException>(() => PixelStyle.Remap(new Color32[1], 1, 1, 1,
                new Color32[0], out _, out _));
        }

        [Test]
        public void GrassUsesVegetationLuminanceRatherThanTheOldSepiaHue()
        {
            var config = ScriptableObject.CreateInstance<WorldStyleConfig>();
            try
            {
                var source = new[] { new Color32(190, 155, 90, 255), new Color32(70, 50, 30, 255) };
                Color32[] result = PixelStyle.Remap(source, 2, 1, 1, config.palette, out _, out _, config.foliageRampColors);
                Assert.Greater(result[0].g, result[0].r);
                Assert.Greater(result[0].g, result[1].g, "Source texture detail survives the hue replacement.");
                Assert.AreEqual(255, result[0].a);
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void WorldScaleReducesCanopyWithoutShrinkingImportantResources()
        {
            var config = ScriptableObject.CreateInstance<WorldStyleConfig>();
            try
            {
                Assert.Less(config.ScaleFor(PropKind.Tree), 1f);
                Assert.Less(config.ScaleFor(PropKind.Clutter), 1f);
                Assert.AreEqual(1f, config.ScaleFor(PropKind.Stone));
                Assert.AreEqual(1f, config.ScaleFor(PropKind.Log));
                Assert.AreEqual(1f, config.ScaleFor(PropKind.Rock));
            }
            finally { Object.DestroyImmediate(config); }
        }

        [Test]
        public void CampRidgesLeaveOneClearEntranceAndBlockTheOtherSides()
        {
            var config = ScriptableObject.CreateInstance<IslandConfig>();
            var parent = new GameObject("Camp layout fixture");
            Vector3 center = new Vector3(5000f, 30f, 5000f);
            try
            {
                GameObject ridges = CampRidges.Build(center, config, parent.transform);
                Assert.AreEqual(5, ridges.GetComponentsInChildren<Collider>().Length);
                Physics.SyncTransforms();
                float approach = config.baseClearingRadius + config.campRidgeThickness + 2f;
                Vector3 atHeight = center + Vector3.up;
                Assert.IsFalse(Physics.Raycast(atHeight + Vector3.back * approach, Vector3.forward, approach),
                    "The southern entrance must remain traversable before players close it.");
                Assert.IsTrue(Physics.Raycast(atHeight + Vector3.left * approach, Vector3.right, approach));
                Assert.IsTrue(Physics.Raycast(atHeight + Vector3.right * approach, Vector3.left, approach));
                Assert.IsTrue(Physics.Raycast(atHeight + Vector3.forward * approach, Vector3.back, approach));
                Vector3 half = new Vector3(config.baseClearingRadius - 0.1f, 0.5f, config.baseClearingRadius - 0.1f);
                Assert.IsEmpty(Physics.OverlapBox(atHeight, half), "The camp interior must stay buildable.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(config);
            }
        }

        [TestCase(16)]
        [TestCase(64)]
        public void TerrainPaintSurvivesCreationRegenerationAndColdImport(int resolution)
        {
            var config = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainConfig>("Assets/Data/Terrain.asset");
            Assert.IsNotNull(config);
            Assert.IsNotNull(TerrainBuilder.PersistTerrainData);
            string name = "ClassicPaintTest_" + System.Guid.NewGuid().ToString("N");
            string path = "Assets/Data/Generated/" + name + ".asset";
            TerrainData data = null;
            try
            {
                string guid = null;
                for (int pass = 0; pass < 2; pass++)
                {
                    data = new TerrainData { heightmapResolution = 33, alphamapResolution = resolution };
                    data.terrainLayers = config.layers;
                    float grass = pass == 0 ? 0.25f : 0.75f;
                    var paint = new float[resolution, resolution, TerrainNoise.LayerCount];
                    for (int y = 0; y < resolution; y++)
                    for (int x = 0; x < resolution; x++)
                    {
                        paint[y, x, TerrainNoise.GrassB] = grass;
                        paint[y, x, TerrainNoise.GrassC] = 1f - grass;
                    }
                    data.SetAlphamaps(0, 0, paint);
                    TerrainBuilder.PersistTerrainData(data, name);
                    UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
                    var restored = UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainData>(path);
                    float[,,] actual = restored.GetAlphamaps(8, 8, 1, 1);
                    Assert.AreEqual(0f, actual[0, 0, TerrainNoise.Sand], 0.005f);
                    Assert.AreEqual(grass, actual[0, 0, TerrainNoise.GrassB], 0.005f);
                    Assert.AreEqual(1f - grass, actual[0, 0, TerrainNoise.GrassC], 0.005f);
                    foreach (Texture2D texture in restored.alphamapTextures)
                        Assert.IsTrue(UnityEditor.EditorUtility.IsPersistent(texture));
                    string current = UnityEditor.AssetDatabase.AssetPathToGUID(path);
                    if (guid != null) Assert.AreEqual(guid, current);
                    guid = current;
                }
            }
            finally
            {
                if (UnityEditor.AssetDatabase.LoadAssetAtPath<TerrainData>(path) != null)
                    UnityEditor.AssetDatabase.DeleteAsset(path);
                else if (data != null) Object.DestroyImmediate(data);
            }
        }
    }
}
