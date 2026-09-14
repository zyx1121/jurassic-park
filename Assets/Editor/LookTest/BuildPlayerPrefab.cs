using JurassicPark.Core;
using JurassicPark.Player;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace JurassicPark.EditorTools
{
    /// <summary>
    /// Builds Assets/Data/PlayerMovement.asset and Assets/Prefabs/Player/Player.prefab:
    /// a CharacterController driven by PlayerController with a billboarded sprite child.
    /// Run with: unity command build_player_prefab
    /// </summary>
    public static class BuildPlayerPrefab
    {
        public const string PrefabPath = "Assets/Prefabs/Player/Player.prefab";
        private const string ConfigPath = "Assets/Data/PlayerMovement.asset";
        private const string ControlsPath = "Assets/Input/PlayerControls.inputactions";
        private const string SpritePath = "Assets/Sprites/Player/survivor_placeholder.png";
        private const float PixelsPerUnit = 64f;

        [CliCommand("build_player_prefab", "Create the player movement config and the Player prefab")]
        public static string Build()
        {
            PlayerMovementConfig config = AssetDatabase.LoadAssetAtPath<PlayerMovementConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<PlayerMovementConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }

            InputActionAsset controls = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ControlsPath);
            Texture2D sprite = AssetDatabase.LoadAssetAtPath<Texture2D>(SpritePath);

            GameObject root = new GameObject("Player");
            CharacterController cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 45f;
            cc.stepOffset = 0.3f;

            PlayerController pc = root.AddComponent<PlayerController>();
            SerializedObject so = new SerializedObject(pc);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("controls").objectReferenceValue = controls;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Sprite";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            float w = sprite.width / PixelsPerUnit;
            float h = sprite.height / PixelsPerUnit;
            quad.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            quad.transform.localScale = new Vector3(w, h, 1f);
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = "PlayerSprite";
            mat.SetTexture("_BaseMap", sprite);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", (float)CullMode.Off);
            AssetDatabase.CreateAsset(mat, "Assets/Materials/PlayerSprite.mat");
            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            quad.AddComponent<Billboard>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return AssetDatabase.GetAssetPath(prefab);
        }
    }
}
