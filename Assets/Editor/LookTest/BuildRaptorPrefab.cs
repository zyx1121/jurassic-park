using JurassicPark.Combat;
using JurassicPark.Core;
using JurassicPark.Dinosaurs;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

namespace JurassicPark.EditorTools
{
    /// <summary>Builds Assets/Prefabs/Dinosaurs/Raptor.prefab: NavMeshAgent, Health, RaptorBrain, billboarded sprite sheets.</summary>
    public static class BuildRaptorPrefab
    {
        public const string PrefabPath = "Assets/Prefabs/Dinosaurs/Raptor.prefab";
        private const float PixelsPerUnit = 64f;
        private const int CellPixels = 128;

        [CliCommand("build_raptor_prefab", "Create the Raptor prefab, its stats and health config")]
        public static string Build()
        {
            DinosaurStats stats = LoadOrCreate<DinosaurStats>("Assets/Data/RaptorStats.asset");
            HealthConfig health = LoadOrCreate<HealthConfig>("Assets/Data/RaptorHealth.asset");
            health.maxHealth = 60f;
            EditorUtility.SetDirty(health);

            SpriteSheetSet set = LoadOrCreate<SpriteSheetSet>("Assets/Data/RaptorSprites.asset");
            set.clips = new[]
            {
                Clip("Idle", "raptor_idle", 4, 4f, true),
                Clip("Walk", "raptor_walk", 8, 12f, true),
                Clip("Bite", "raptor_bite", 6, 12f, false),
                Clip("Death", "raptor_death", 6, 8f, false),
            };
            EditorUtility.SetDirty(set);

            GameObject root = new GameObject("Raptor");
            root.tag = "Untagged";
            CapsuleCollider col = root.AddComponent<CapsuleCollider>();
            col.radius = 0.45f;
            col.height = 1.6f;
            col.center = new Vector3(0f, 0.8f, 0f);
            NavMeshAgent agent = root.AddComponent<NavMeshAgent>();
            agent.radius = 0.45f;
            agent.height = 1.6f;
            agent.baseOffset = 0f;
            Health h = root.AddComponent<Health>();
            SerializedObject hso = new SerializedObject(h);
            hso.FindProperty("config").objectReferenceValue = health;
            hso.ApplyModifiedPropertiesWithoutUndo();
            root.AddComponent<HitFlash>();

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Sprite";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            float size = CellPixels / PixelsPerUnit;
            quad.transform.localPosition = new Vector3(0f, size * 0.5f - 0.1f, 0f);
            quad.transform.localScale = new Vector3(size, size, 1f);
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "RaptorSprite" };
            mat.SetTexture("_BaseMap", set.clips[0].sheet);
            mat.SetTextureScale("_BaseMap", new Vector2(0.25f, 0.25f));
            mat.SetTextureOffset("_BaseMap", new Vector2(0f, 0.75f));
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", (float)CullMode.Off);
            AssetDatabase.CreateAsset(mat, "Assets/Materials/RaptorSprite.mat");
            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            quad.AddComponent<Billboard>();
            SpriteSheetAnimator anim = quad.AddComponent<SpriteSheetAnimator>();
            SerializedObject aso = new SerializedObject(anim);
            aso.FindProperty("set").objectReferenceValue = set;
            aso.ApplyModifiedPropertiesWithoutUndo();

            DinosaurBrain brain = root.AddComponent<DinosaurBrain>();
            SerializedObject bso = new SerializedObject(brain);
            bso.FindProperty("stats").objectReferenceValue = stats;
            bso.FindProperty("animator").objectReferenceValue = anim;
            bso.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return AssetDatabase.GetAssetPath(prefab);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(asset, path);
            }

            return asset;
        }

        private static SpriteSheetClip Clip(string name, string file, int columns, float fps, bool loop)
        {
            return new SpriteSheetClip
            {
                name = name,
                sheet = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Sprites/Dinosaurs/{file}.png"),
                columns = columns,
                rows = 4,
                framesPerSecond = fps,
                loop = loop,
            };
        }
    }
}
