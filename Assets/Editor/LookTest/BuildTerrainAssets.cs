using JurassicPark.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    /// <summary>Creates the five TerrainLayer assets and Assets/Data/Terrain.asset that reference them.</summary>
    public static class BuildTerrainAssets
    {
        public const string ConfigPath = "Assets/Data/Terrain.asset";
        private static readonly string[] Files = { "sand", "grass", "grass_b", "grass_c", "dirt", "rock" };

        [CliCommand("build_terrain_assets", "Create terrain layers and the terrain config")]
        public static string Build()
        {
            TerrainConfig config = AssetDatabase.LoadAssetAtPath<TerrainConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<TerrainConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            var layers = new TerrainLayer[Files.Length];
            for (int i = 0; i < Files.Length; i++)
            {
                string path = $"Assets/Settings/TerrainLayers/{Files[i]}.terrainlayer";
                if (!AssetDatabase.IsValidFolder("Assets/Settings/TerrainLayers"))
                {
                    AssetDatabase.CreateFolder("Assets/Settings", "TerrainLayers");
                }

                TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
                if (layer == null)
                {
                    layer = new TerrainLayer();
                    AssetDatabase.CreateAsset(layer, path);
                }

                layer.diffuseTexture = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Textures/Terrain/{Files[i]}.png");
                layer.tileSize = new Vector2(2f, 2f); // 128 px plates cover 2 m: still 64 px per meter
                layer.smoothness = Files[i] == "dirt" ? 0.16f : 0.08f;
                layer.metallic = 0f;
                EditorUtility.SetDirty(layer);
                layers[i] = layer;
            }

            config.layers = layers;
            config.maxHeight = 14f;
            config.seaLevel = 3.5f;
            config.featureSize = 28f;
            config.octaves = 5;
            config.maxStepPerCell = 0.55f;
            config.rockSlope = 0.75f;
            config.dirtThreshold = 0.78f;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return ConfigPath;
        }
    }
}
