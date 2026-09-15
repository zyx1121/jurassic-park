using JurassicPark.Building;
using JurassicPark.Net;
using Unity.Netcode;
using Unity.Netcode.Components;
using JurassicPark.Combat;
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
        private const string SetPath = "Assets/Data/SurvivorSprites.asset";
        private const int CellPixels = 128;
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
            SpriteSheetSet set = BuildSpriteSet();

            GameObject root = new GameObject("Player");
            CharacterController cc = root.AddComponent<CharacterController>();
            cc.height = 1.8f;
            cc.radius = 0.35f;
            cc.center = new Vector3(0f, 0.9f, 0f);
            cc.slopeLimit = 45f;
            cc.stepOffset = 0.3f;

            root.tag = "Player";
            HealthConfig healthConfig = AssetDatabase.LoadAssetAtPath<HealthConfig>("Assets/Data/PlayerHealth.asset");
            if (healthConfig == null)
            {
                healthConfig = ScriptableObject.CreateInstance<HealthConfig>();
                healthConfig.maxHealth = 100f;
                healthConfig.invulnerabilityAfterHit = 0.5f;
                AssetDatabase.CreateAsset(healthConfig, "Assets/Data/PlayerHealth.asset");
            }
            Health health = root.AddComponent<Health>();
            SerializedObject healthSo = new SerializedObject(health);
            healthSo.FindProperty("config").objectReferenceValue = healthConfig;
            healthSo.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<HitFlash>();
            InventoryConfig invCfg = AssetDatabase.LoadAssetAtPath<InventoryConfig>("Assets/Data/Inventory.asset");
            if (invCfg == null)
            {
                invCfg = ScriptableObject.CreateInstance<InventoryConfig>();
                AssetDatabase.CreateAsset(invCfg, "Assets/Data/Inventory.asset");
            }
            ResourceInventory inventory = root.AddComponent<ResourceInventory>();
            SerializedObject invSo = new SerializedObject(inventory);
            invSo.FindProperty("config").objectReferenceValue = invCfg;
            invSo.ApplyModifiedPropertiesWithoutUndo();

            PlayerController pc = root.AddComponent<PlayerController>();
            SerializedObject so = new SerializedObject(pc);
            so.FindProperty("config").objectReferenceValue = config;
            so.FindProperty("controls").objectReferenceValue = controls;
            so.ApplyModifiedPropertiesWithoutUndo();

            root.AddComponent<NetworkObject>();
            NetworkTransform netTransform = root.AddComponent<NetworkTransform>();
            netTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
            netTransform.SyncScaleX = netTransform.SyncScaleY = netTransform.SyncScaleZ = false;
            netTransform.Interpolate = true;
            root.AddComponent<NetPlayer>();

            PlayerBuilder builder = root.AddComponent<PlayerBuilder>();
            SerializedObject bso = new SerializedObject(builder);
            bso.FindProperty("library").objectReferenceValue = AssetDatabase.LoadAssetAtPath<StructureLibrary>(BuildStructureLibrary.LibraryPath);
            bso.ApplyModifiedPropertiesWithoutUndo();

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Sprite";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            const float PlayerScale = 0.85f; // a smaller survivor makes the jungle loom
            float w = CellPixels / PixelsPerUnit * PlayerScale;
            float h = CellPixels / PixelsPerUnit * PlayerScale;
            quad.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
            quad.transform.localScale = new Vector3(w, h, 1f);
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = "PlayerSprite";
            mat.SetTexture("_BaseMap", set.clips[0].sheet);
            mat.SetTextureScale("_BaseMap", new Vector2(1f / set.clips[0].columns, 1f / set.clips[0].rows));
            mat.SetTextureOffset("_BaseMap", new Vector2(0f, 1f - 1f / set.clips[0].rows));
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
            SpriteSheetAnimator anim = quad.AddComponent<SpriteSheetAnimator>();
            SerializedObject animSo = new SerializedObject(anim);
            animSo.FindProperty("set").objectReferenceValue = set;
            animSo.ApplyModifiedPropertiesWithoutUndo();
            PlayerSpriteAnimator psa = root.AddComponent<PlayerSpriteAnimator>();
            SerializedObject psaSo = new SerializedObject(psa);
            psaSo.FindProperty("player").objectReferenceValue = pc;
            psaSo.FindProperty("animator").objectReferenceValue = anim;
            psaSo.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return AssetDatabase.GetAssetPath(prefab);
        }
            private static SpriteSheetSet BuildSpriteSet()
        {
            SpriteSheetSet set = AssetDatabase.LoadAssetAtPath<SpriteSheetSet>(SetPath);
            if (set == null)
            {
                set = ScriptableObject.CreateInstance<SpriteSheetSet>();
                AssetDatabase.CreateAsset(set, SetPath);
            }

            set.clips = new[]
            {
                Clip("Idle", "survivor_idle", 4, 4f),
                Clip("Walk", "survivor_walk", 8, 10f),
                Clip("Run", "survivor_run", 8, 14f),
            };
            EditorUtility.SetDirty(set);
            return set;
        }

        private static SpriteSheetClip Clip(string name, string file, int columns, float fps)
        {
            return new SpriteSheetClip
            {
                name = name,
                sheet = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Sprites/Player/{file}.png"),
                columns = columns,
                rows = 4,
                framesPerSecond = fps,
                loop = true,
            };
        }
    }
}
