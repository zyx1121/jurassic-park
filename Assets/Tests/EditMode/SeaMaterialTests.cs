using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class SeaMaterialTests
    {
        private static Material SeaMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Sea.mat");
            Assert.IsNotNull(material, "Run build_sea_material before testing generated sea assets.");
            Assert.AreEqual("JurassicPark/SeaWaves", material.shader.name);
            return material;
        }

        [Test]
        public void SeaShaderHasNoImportErrors()
        {
            Shader shader = Shader.Find("JurassicPark/SeaWaves");
            Assert.IsNotNull(shader);
            Assert.IsFalse(ShaderUtil.ShaderHasError(shader));
        }

        [TestCase("wave_00", "_Wave0")]
        [TestCase("wave_01", "_Wave1")]
        [TestCase("wave_02", "_Wave2")]
        [TestCase("wave_03", "_Wave3")]
        [TestCase("foam_texture", "_FoamMap")]
        public void SeaTilesRepeatWithoutFilteringOrCompression(string filename, string property)
        {
            string path = $"Assets/Sprites/Sea/{filename}.png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            Assert.IsNotNull(importer);
            Assert.AreEqual(TextureImporterType.Default, importer.textureType);
            Assert.AreEqual(FilterMode.Point, importer.filterMode);
            Assert.AreEqual(TextureWrapMode.Repeat, importer.wrapModeU);
            Assert.AreEqual(TextureWrapMode.Repeat, importer.wrapModeV);
            Assert.IsFalse(importer.mipmapEnabled);
            Assert.IsFalse(importer.crunchedCompression);
            Assert.AreEqual(TextureImporterCompression.Uncompressed, importer.textureCompression);
            Assert.IsTrue(importer.sRGBTexture);
            foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                if (!settings.overridden) continue;
                Assert.AreEqual(TextureImporterFormat.RGBA32, settings.format, platform);
                Assert.AreEqual(TextureImporterCompression.Uncompressed, settings.textureCompression, platform);
                Assert.IsFalse(settings.crunchedCompression, platform);
            }
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            Assert.IsNotNull(texture);
            Assert.AreEqual(128, texture.width);
            Assert.AreEqual(128, texture.height);
            Assert.AreSame(texture, SeaMaterial().GetTexture(property));
        }

        [TestCase("_BaseColor", "#3C6B55")]
        [TestCase("_ShelfColor", "#2D5A50")]
        [TestCase("_OffshoreColor", "#21474A")]
        [TestCase("_DeepTealColor", "#17323A")]
        [TestCase("_DeepColor", "#242136")]
        [TestCase("_AbyssColor", "#17141F")]
        [TestCase("_FoamColor", "#C2B8A1")]
        public void SeaUsesHandoffPalette(string property, string hex)
        {
            Assert.IsTrue(ColorUtility.TryParseHtmlString(hex, out Color expected));
            Material material = SeaMaterial();
            Assert.IsTrue(material.HasProperty(property));
            Color actual = material.GetColor(property);
            Assert.AreEqual(expected.r, actual.r, 0.00001f);
            Assert.AreEqual(expected.g, actual.g, 0.00001f);
            Assert.AreEqual(expected.b, actual.b, 0.00001f);
        }

        [Test]
        public void SeaUsesSteppedDepthAndPixelAnimationDefaults()
        {
            Material material = SeaMaterial();
            Assert.AreEqual(new Vector4(0.7f, 1.5f, 3f, 6f), material.GetVector("_DepthThresholds"));
            Assert.AreEqual(12f, material.GetFloat("_AbyssDepth"));
            Assert.AreEqual(0.8f, material.GetFloat("_DepthTintStrength"), 0.00001f);
            Assert.AreEqual(2f, material.GetFloat("_TileMeters"));
            Assert.AreEqual(128f, material.GetFloat("_PixelsPerTile"));
            Assert.AreEqual(5f, material.GetFloat("_FrameRate"));
            Assert.AreEqual(0.018f, material.GetFloat("_ScrollSpeed"), 0.00001f);
            Assert.AreEqual(0.006f, material.GetFloat("_CrossDrift"), 0.00001f);
            Assert.AreEqual(0.45f, material.GetFloat("_FoamWidth"), 0.00001f);
            Assert.AreEqual(1f, material.GetFloat("_Alpha"), "Do not blend a smooth seabed into the palette bands.");
            Assert.AreEqual(3000, material.renderQueue, "Sea must sample depth after opaque terrain renders.");
        }
    }
}
