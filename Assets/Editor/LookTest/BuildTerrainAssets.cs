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
        private static readonly string[] Files = { "sand", "grass", "grass_b", "dirt", "rock" };

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
                layer.tileSize = new Vector2(1f, 1f); // 64 px per meter, same density as the 64 PPU sprites
                layer.smoothness = 0f;
                layer.metallic = 0f;
                EditorUtility.SetDirty(layer);
                layers[i] = layer;
            }

            config.layers = layers;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return ConfigPath;
        }
    }
}
