using JurassicPark.UI;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    public static class BuildSelectionAssets
    {
        public const string ConfigPath = "Assets/Data/Selection.asset";

        [CliCommand("build_selection_assets", "Create contextual interaction picking settings")]
        public static string Build()
        {
            Create();
            return ConfigPath;
        }

        public static SelectionConfig Create()
        {
            SelectionConfig config = AssetDatabase.LoadAssetAtPath<SelectionConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<SelectionConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            return config;
        }
    }
}
