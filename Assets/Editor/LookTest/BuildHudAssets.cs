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
            Font body = LoadFont("SourceSans3-Regular");
            Font emphasis = LoadFont("SourceSans3-Semibold");
            Font heading = LoadFont("SourceSerif4-Semibold");
            HudConfig config = AssetDatabase.LoadAssetAtPath<HudConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<HudConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            if (config.designVersion < 2)
            {
                HudConfig defaults = ScriptableObject.CreateInstance<HudConfig>();
                EditorUtility.CopySerialized(defaults, config);
                Object.DestroyImmediate(defaults);
                config.name = "Hud";
                config.designVersion = 2;
            }
            config.bodyFont = body;
            config.emphasisFont = emphasis;
            config.headingFont = heading;
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
        }

        private static Font LoadFont(string name)
        {
            string path = $"Assets/UI/Fonts/{name}.ttf";
            Font font = AssetDatabase.LoadAssetAtPath<Font>(path);
            if (font == null)
                throw new System.InvalidOperationException($"HUD requires licensed embedded font at {path}. Import the supplied fonts before build_hud_assets.");
            return font;
        }
    }
}
