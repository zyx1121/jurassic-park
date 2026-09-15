using JurassicPark.Building;
using JurassicPark.Combat;
using JurassicPark.Core;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    /// <summary>Creates the StructureDef assets, their health configs, preview materials and the StructureLibrary.</summary>
    public static class BuildStructureLibrary
    {
        public const string LibraryPath = "Assets/Data/StructureLibrary.asset";

        [CliCommand("build_structure_library", "Create structure definitions and the structure library")]
        public static string Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Data/Structures")) AssetDatabase.CreateFolder("Assets/Data", "Structures");

            StructureLibrary lib = LoadOrCreate<StructureLibrary>(LibraryPath);
            lib.wood = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Bark.mat");
            lib.stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Stone.mat");
            lib.previewValid = Preview("PreviewValid", new Color(0.3f, 1f, 0.4f, 0.45f));
            lib.previewInvalid = Preview("PreviewInvalid", new Color(1f, 0.3f, 0.3f, 0.45f));

            lib.structures = new[]
            {
                Def("Fence", StructureKind.Fence, new Vector2Int(2, 1), 1.4f, 60f, true, false, false, Cost(ResourceKind.Wood, 3)),
                Def("Wall", StructureKind.Wall, new Vector2Int(2, 1), 2.0f, 150f, true, false, false, Cost(ResourceKind.Stone, 4)),
                Def("Gate", StructureKind.Gate, new Vector2Int(2, 1), 1.4f, 60f, true, true, false, Cost(ResourceKind.Wood, 4)),
                Def("Campfire", StructureKind.Campfire, new Vector2Int(2, 2), 1.0f, 40f, false, false, true, Cost(ResourceKind.Wood, 5), AssetDatabase.LoadAssetAtPath<GameObject>(BuildCampfirePrefab.PrefabPath)),
                Def("Torch", StructureKind.Torch, new Vector2Int(1, 1), 1.8f, 30f, false, false, true, Cost(ResourceKind.Wood, 2)),
            };
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return $"{LibraryPath} ({lib.structures.Length} structures)";
        }

        private static ResourceCost[] Cost(ResourceKind kind, int amount) => new[] { new ResourceCost { kind = kind, amount = amount } };

        private static StructureDef Def(string name, StructureKind kind, Vector2Int footprint, float height, float hp, bool solid, bool isGate, bool light, ResourceCost[] cost, GameObject prefab = null)
        {
            StructureDef def = LoadOrCreate<StructureDef>($"Assets/Data/Structures/{name}.asset");
            def.displayName = name; def.kind = kind; def.footprint = footprint; def.height = height; def.solid = solid; def.isGate = isGate; def.emitsLight = light; def.cost = cost; def.prefab = prefab;
            HealthConfig health = LoadOrCreate<HealthConfig>($"Assets/Data/Structures/{name}Health.asset");
            health.maxHealth = hp;
            health.immuneTo = kind == StructureKind.Wall ? new[] { DamageType.Bite } : new DamageType[0];
            EditorUtility.SetDirty(health);
            def.health = health;
            EditorUtility.SetDirty(def);
            return def;
        }

        private static Material Preview(string name, Color color)
        {
            string path = $"Assets/Materials/{name}.mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(m, path);
            }

            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_ZWrite", 0f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 3000;
            m.SetColor("_BaseColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (a == null)
            {
                a = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(a, path);
            }

            return a;
        }
    }
}
