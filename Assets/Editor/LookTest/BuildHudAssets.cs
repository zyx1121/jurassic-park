using JurassicPark.UI;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    public static class BuildHudAssets
    {
        public const string ConfigPath = "Assets/Data/Hud.asset";

        [CliCommand("build_hud_assets", "Create native-resolution survival HUD settings")]
        public static string Build()
        {
            Create();
            return ConfigPath;
        }

        public static HudConfig Create()
        {
            HudConfig config = AssetDatabase.LoadAssetAtPath<HudConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<HudConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
        }
    }
}
