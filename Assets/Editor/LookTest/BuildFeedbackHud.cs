using JurassicPark.UI;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    public static class BuildFeedbackHud
    {
        public static GameObject Create()
        {
            HudConfig hudConfig = BuildHudAssets.Create();
            SelectionConfig selectionConfig = BuildSelectionAssets.Create();
            var root = new GameObject("HUD");
            root.AddComponent<WorldSelection>().Configure(selectionConfig);
            SurvivalHud hud = root.AddComponent<SurvivalHud>();
            var so = new SerializedObject(hud);
            so.FindProperty("config").objectReferenceValue = hudConfig;
            so.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<Minimap>();
            return root;
        }
    }
}
