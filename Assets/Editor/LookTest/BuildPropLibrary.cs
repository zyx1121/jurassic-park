using JurassicPark.Core;
using JurassicPark.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    /// <summary>Creates a PropVariant asset per sprite in Assets/Sprites/Props and the PropLibrary that lists them.</summary>
    public static class BuildPropLibrary
    {
        public const string LibraryPath = "Assets/Data/PropLibrary.asset";

        private struct Def
        {
            public string file; public PropKind kind; public int cell; public float radius; public bool solid; public ResourceKind res; public int amount; public float weight;
            public Def(string f, PropKind k, int c, float r, bool s, ResourceKind rs, int a, float w) { file = f; kind = k; cell = c; radius = r; solid = s; res = rs; amount = a; weight = w; }
        }

        private static readonly Def[] Defs =
        {
            new Def("tree_palm", PropKind.Tree, 192, 0.35f, true, ResourceKind.Wood, 3, 1.5f),
            new Def("tree_palmTall", PropKind.Tree, 192, 0.35f, true, ResourceKind.Wood, 4, 1f),
            new Def("tree_palmBend", PropKind.Tree, 192, 0.35f, true, ResourceKind.Wood, 3, 1f),
            new Def("tree_oak", PropKind.Tree, 192, 0.5f, true, ResourceKind.Wood, 5, 1f),
            new Def("tree_detailed", PropKind.Tree, 192, 0.5f, true, ResourceKind.Wood, 5, 1f),
            new Def("tree_fat", PropKind.Tree, 192, 0.6f, true, ResourceKind.Wood, 6, 0.7f),
            new Def("tree_thin", PropKind.Tree, 192, 0.3f, true, ResourceKind.Wood, 2, 1f),
            new Def("tree_default", PropKind.Tree, 192, 0.45f, true, ResourceKind.Wood, 4, 1f),
            new Def("tree_pineDefaultA", PropKind.Tree, 192, 0.4f, true, ResourceKind.Wood, 4, 0.6f),
            new Def("tree_plateau", PropKind.Tree, 192, 0.5f, true, ResourceKind.Wood, 4, 0.6f),
            new Def("rock_largeA", PropKind.Rock, 128, 0.9f, true, ResourceKind.Stone, 5, 1f),
            new Def("rock_largeC", PropKind.Rock, 128, 0.9f, true, ResourceKind.Stone, 5, 1f),
            new Def("rock_largeE", PropKind.Rock, 128, 0.9f, true, ResourceKind.Stone, 5, 1f),
            new Def("rock_tallB", PropKind.Rock, 128, 0.6f, true, ResourceKind.Stone, 4, 0.8f),
            new Def("rock_tallD", PropKind.Rock, 128, 0.6f, true, ResourceKind.Stone, 4, 0.8f),
            new Def("rock_smallFlatA", PropKind.Rock, 128, 0.5f, true, ResourceKind.Stone, 2, 1.2f),
            new Def("rock_smallC", PropKind.Rock, 128, 0.4f, true, ResourceKind.Stone, 2, 1.2f),
            new Def("log", PropKind.Log, 128, 0.5f, true, ResourceKind.Wood, 2, 1f),
            new Def("log_large", PropKind.Log, 128, 0.7f, true, ResourceKind.Wood, 3, 0.7f),
            new Def("stump_old", PropKind.Log, 128, 0.4f, true, ResourceKind.Wood, 1, 0.8f),
            new Def("grass_large", PropKind.Grass, 96, 0.2f, false, ResourceKind.None, 0, 1f),
            new Def("grass_leafsLarge", PropKind.Grass, 96, 0.2f, false, ResourceKind.None, 0, 1f),
            new Def("grass", PropKind.Grass, 96, 0.15f, false, ResourceKind.None, 0, 1.5f),
            new Def("plant_bush", PropKind.Bush, 96, 0.35f, false, ResourceKind.Food, 1, 1f),
            new Def("plant_bushLarge", PropKind.Bush, 96, 0.45f, false, ResourceKind.Food, 2, 0.8f),
        };

        private static Material SpriteMaterial(string path, Texture2D tex, Color tint)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }

            PropPlacer.ConfigureSpriteMaterial(m, tex, tint);
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        [CliCommand("build_prop_library", "Create PropVariant assets from Assets/Sprites/Props and the PropLibrary")]
        public static string Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Data/Props"))
            {
                AssetDatabase.CreateFolder("Assets/Data", "Props");
            }

            PropLibrary lib = AssetDatabase.LoadAssetAtPath<PropLibrary>(LibraryPath);
            if (lib == null)
            {
                lib = ScriptableObject.CreateInstance<PropLibrary>();
                AssetDatabase.CreateAsset(lib, LibraryPath);
            }

            if (!AssetDatabase.IsValidFolder("Assets/Materials/Props")) AssetDatabase.CreateFolder("Assets/Materials", "Props");
            if (!AssetDatabase.IsValidFolder("Assets/Materials/Pickups")) AssetDatabase.CreateFolder("Assets/Materials", "Pickups");
            GatherRules rulesForDepleted = AssetDatabase.LoadAssetAtPath<GatherRules>("Assets/Data/GatherRules.asset");
            if (rulesForDepleted == null)
            {
                rulesForDepleted = ScriptableObject.CreateInstance<GatherRules>();
                AssetDatabase.CreateAsset(rulesForDepleted, "Assets/Data/GatherRules.asset");
            }

            var variants = new System.Collections.Generic.List<PropVariant>();
            foreach (Def d in Defs)
            {
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Sprites/Props/{d.file}.png");
                if (tex == null)
                {
                    Debug.LogWarning($"prop sprite missing: {d.file}");
                    continue;
                }

                string path = $"Assets/Data/Props/{d.file}.asset";
                PropVariant v = AssetDatabase.LoadAssetAtPath<PropVariant>(path);
                if (v == null)
                {
                    v = ScriptableObject.CreateInstance<PropVariant>();
                    AssetDatabase.CreateAsset(v, path);
                }

                v.kind = d.kind; v.sprite = tex; v.cellPixels = d.cell; v.pixelsPerUnit = 64f; v.footprintRadius = d.radius;
                v.solid = d.solid; v.resource = d.res; v.resourceAmount = d.amount; v.weight = d.weight;
                v.tintMaterials = new Material[lib.tints.Length];
                for (int t = 0; t < lib.tints.Length; t++)
                {
                    v.tintMaterials[t] = SpriteMaterial($"Assets/Materials/Props/{d.file}_{t}.mat", tex, lib.tints[t]);
                }

                Color depletedTint = Color.gray;
                if (d.res != ResourceKind.None && rulesForDepleted != null && rulesForDepleted.TryGet(d.res, out GatherRule gr)) depletedTint = gr.depletedTint;
                v.depletedMaterial = SpriteMaterial($"Assets/Materials/Props/{d.file}_depleted.mat", tex, depletedTint);
                EditorUtility.SetDirty(v);
                variants.Add(v);
            }

            GatherRules rules = AssetDatabase.LoadAssetAtPath<GatherRules>("Assets/Data/GatherRules.asset");
            if (rules == null)
            {
                rules = ScriptableObject.CreateInstance<GatherRules>();
                AssetDatabase.CreateAsset(rules, "Assets/Data/GatherRules.asset");
            }

            lib.gatherRules = rules;
            PickupLibrary pickups = AssetDatabase.LoadAssetAtPath<PickupLibrary>("Assets/Data/PickupLibrary.asset");
            if (pickups == null)
            {
                pickups = ScriptableObject.CreateInstance<PickupLibrary>();
                AssetDatabase.CreateAsset(pickups, "Assets/Data/PickupLibrary.asset");
            }
            pickups.wood = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Items/pickup_wood.png");
            pickups.stone = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Items/pickup_stone.png");
            pickups.food = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Items/pickup_food.png");
            pickups.boatPart = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Sprites/Items/pickup_boatpart.png");
            pickups.woodMaterial = SpriteMaterial("Assets/Materials/Pickups/wood.mat", pickups.wood, Color.white);
            pickups.stoneMaterial = SpriteMaterial("Assets/Materials/Pickups/stone.mat", pickups.stone, Color.white);
            pickups.foodMaterial = SpriteMaterial("Assets/Materials/Pickups/food.mat", pickups.food, Color.white);
            pickups.boatPartMaterial = SpriteMaterial("Assets/Materials/Pickups/boatpart.mat", pickups.boatPart, Color.white);
            EditorUtility.SetDirty(pickups);
            lib.pickups = pickups;
            lib.variants = variants.ToArray();
            EditorUtility.SetDirty(lib);
            AssetDatabase.SaveAssets();
            return $"{LibraryPath} ({variants.Count} variants)";
        }
    }
}
