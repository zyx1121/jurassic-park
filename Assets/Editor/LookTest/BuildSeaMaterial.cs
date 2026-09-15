using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    /// <summary>Builds Assets/Materials/Sea.mat from the v2 wave frames and foam band (art repo out/sea/SEA.md).</summary>
    public static class BuildSeaMaterial
    {
        public const string MaterialPath = "Assets/Materials/Sea.mat";

        [CliCommand("build_sea_material", "Create the animated sea material from Assets/Sprites/Sea")]
        public static string Build()
        {
            Material m = Create();
            return $"{MaterialPath} ({m.shader.name})";
        }

        public static Material Create()
        {
            Shader shader = Shader.Find("JurassicPark/SeaWaves");
            if (shader == null)
                throw new System.InvalidOperationException("JurassicPark/SeaWaves shader is missing.");

            var waves = new Texture2D[4];
            for (int i = 0; i < waves.Length; i++)
                waves[i] = LoadRepeatTexture($"Assets/Sprites/Sea/wave_0{i}.png");
            Texture2D foam = LoadRepeatTexture("Assets/Sprites/Sea/foam_texture.png");

            Material m = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, MaterialPath);
            }

            m.shader = shader;
            for (int i = 0; i < 4; i++)
            {
                m.SetTexture($"_Wave{i}", waves[i]);
            }

            m.SetTexture("_FoamMap", foam);
            m.SetFloat("_TileMeters", 2f);
            m.SetFloat("_PixelsPerTile", 128f);
            m.SetFloat("_FrameRate", 5f);
            m.SetFloat("_ScrollSpeed", 0.018f);
            m.SetFloat("_CrossDrift", 0.006f);
            m.SetFloat("_FoamWidth", 0.45f);
            // Opaque colour prevents the smooth terrain beneath from leaking through the stepped ramp.
            // The transparent pass still runs after opaque depth has been captured for shoreline sampling.
            m.SetFloat("_Alpha", 1f);
            m.SetColor("_Tint", Color.white);
            m.SetColor("_BaseColor", new Color32(0x3C, 0x6B, 0x55, 0xFF));
            m.SetColor("_ShelfColor", new Color32(0x2D, 0x5A, 0x50, 0xFF));
            m.SetColor("_OffshoreColor", new Color32(0x21, 0x47, 0x4A, 0xFF));
            m.SetColor("_DeepTealColor", new Color32(0x17, 0x32, 0x3A, 0xFF));
            m.SetColor("_DeepColor", new Color32(0x24, 0x21, 0x36, 0xFF));
            m.SetColor("_AbyssColor", new Color32(0x17, 0x14, 0x1F, 0xFF));
            m.SetVector("_DepthThresholds", new Vector4(0.7f, 1.5f, 3f, 6f));
            m.SetFloat("_AbyssDepth", 12f);
            m.SetFloat("_DepthTintStrength", 0.8f);
            m.SetColor("_FoamColor", new Color32(0xC2, 0xB8, 0xA1, 0xFF));
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return m;
        }

        private static Texture2D LoadRepeatTexture(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
                throw new System.InvalidOperationException($"Sea texture is missing: {path}");

            bool changed = importer.textureType != TextureImporterType.Default
                || importer.filterMode != FilterMode.Point
                || importer.wrapModeU != TextureWrapMode.Repeat
                || importer.wrapModeV != TextureWrapMode.Repeat
                || importer.mipmapEnabled || !importer.sRGBTexture
                || importer.textureCompression != TextureImporterCompression.Uncompressed
                || importer.crunchedCompression || importer.alphaIsTransparency
                || importer.npotScale != TextureImporterNPOTScale.None
                || importer.anisoLevel != 0;
            if (changed)
            {
                importer.textureType = TextureImporterType.Default;
                importer.filterMode = FilterMode.Point;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.mipmapEnabled = false;
                importer.sRGBTexture = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.crunchedCompression = false;
                importer.alphaIsTransparency = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.anisoLevel = 0;
            }

            // Explicit RGBA32 prevents platform overrides from reintroducing compression.
            foreach (string platform in new[] { "Standalone", "Android", "iPhone", "WebGL" })
            {
                TextureImporterPlatformSettings settings = importer.GetPlatformTextureSettings(platform);
                if (!settings.overridden) continue;
                if (settings.format == TextureImporterFormat.RGBA32
                    && settings.textureCompression == TextureImporterCompression.Uncompressed
                    && !settings.crunchedCompression) continue;
                settings.format = TextureImporterFormat.RGBA32;
                settings.textureCompression = TextureImporterCompression.Uncompressed;
                settings.crunchedCompression = false;
                importer.SetPlatformTextureSettings(settings);
                changed = true;
            }
            if (changed) importer.SaveAndReimport();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                throw new System.InvalidOperationException($"Sea texture failed to import: {path}");
            return texture;
        }
    }
}
