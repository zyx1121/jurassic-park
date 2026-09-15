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
            public string file; public PropKind kind; public int cell; public float radius; public bool solid; public ResourceKind res; public int amount; public float weight; public float scale; public float jitter;
            public Def(string f, PropKind k, int c, float r, bool s, ResourceKind rs, int a, float w, float sc, float j) { file = f; kind = k; cell = c; radius = r; solid = s; res = rs; amount = a; weight = w; scale = sc; jitter = j; }
        }

        // Cells and placement scales follow docs/ART_DIRECTION.md. Until the v2 art lands, the v1 rocks
        // double as boulders (scaled up) and stones (scaled down) so the tiers already exist in the world.
        private static readonly Def[] Defs =
        {
            new Def("tree_canopy_a", PropKind.Tree, 192, 0.55f, true, ResourceKind.Wood, 5, 1.2f, 2.1f, 0.12f),
            new Def("tree_canopy_b", PropKind.Tree, 192, 0.55f, true, ResourceKind.Wood, 5, 1.0f, 2.2f, 0.12f),
            new Def("palm_weathered", PropKind.Tree, 192, 0.35f, true, ResourceKind.Wood, 4, 1.4f, 2.3f, 0.12f),
            new Def("rock_basalt_a", PropKind.Boulder, 128, 1.1f, true, ResourceKind.None, 0, 1.0f, 2.4f, 0.25f),
            new Def("rock_basalt_b", PropKind.Boulder, 128, 1.3f, true, ResourceKind.None, 0, 1.0f, 2.6f, 0.25f),
            new Def("rock_lichen", PropKind.Boulder, 128, 1.0f, true, ResourceKind.None, 0, 0.8f, 2.2f, 0.25f),
            new Def("rock_basalt_a", PropKind.Rock, 128, 0.7f, true, ResourceKind.Stone, 4, 1.0f, 0.9f, 0.2f),
            new Def("rock_lichen", PropKind.Rock, 128, 0.65f, true, ResourceKind.Stone, 4, 1.0f, 0.85f, 0.2f),
            new Def("rock_rust", PropKind.Rock, 128, 0.7f, true, ResourceKind.Stone, 4, 1.0f, 0.9f, 0.2f),
            new Def("rock_basalt_b", PropKind.Stone, 128, 0.25f, true, ResourceKind.Stone, 1, 1.0f, 0.36f, 0.25f),
            new Def("rock_rust", PropKind.Stone, 128, 0.22f, true, ResourceKind.Stone, 1, 1.0f, 0.32f, 0.25f),
            new Def("rock_lichen", PropKind.Stone, 128, 0.22f, true, ResourceKind.Stone, 1, 0.8f, 0.3f, 0.25f),
            new Def("fallen_log", PropKind.Log, 128, 0.7f, true, ResourceKind.Wood, 2, 1.0f, 1.0f, 0.2f),
            new Def("mossy_stump", PropKind.Log, 128, 0.45f, true, ResourceKind.Wood, 1, 0.8f, 0.95f, 0.2f),
            new Def("grass_tuft", PropKind.Grass, 96, 0.2f, false, ResourceKind.None, 0, 1.5f, 0.9f, 0.25f),
            new Def("fern_tuft", PropKind.Grass, 96, 0.25f, false, ResourceKind.None, 0, 1.2f, 1.0f, 0.25f),
            new Def("fern_tuft", PropKind.Clutter, 96, 0.3f, false, ResourceKind.None, 0, 1.0f, 1.4f, 0.25f),
            new Def("grass_tuft", PropKind.Clutter, 96, 0.2f, false, ResourceKind.None, 0, 0.8f, 0.6f, 0.3f),
            new Def("bush_tangled", PropKind.Clutter, 96, 0.4f, false, ResourceKind.None, 0, 0.6f, 1.3f, 0.2f),
            new Def("bush_tangled", PropKind.Bush, 96, 0.4f, false, ResourceKind.Food, 1, 1.0f, 1.0f, 0.16f),
            new Def("bush_wide", PropKind.Bush, 96, 0.45f, false, ResourceKind.Food, 2, 0.8f, 1.05f, 0.16f),
        };

        /// <summary>Translucent (35 percent) variant shown while the prop stands between the camera and the player.</summary>
        private static Material FadeMaterial(string path, Texture2D tex)
        {
            Shader shader = Shader.Find("JurassicPark/SpriteSeeThrough");
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(shader);
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = shader;
            m.SetTexture("_BaseMap", tex);
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Cutoff", 0.5f);
            m.SetFloat("_Fade", 0f);
            m.SetFloat("_HoleAlpha", 0.12f);
            m.SetFloat("_HoleRadius", 0.22f);
            m.SetFloat("_HoleSoftness", 0.18f);
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

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

            lib.tints = new[] { Color.white }; // the art direction forbids broad random tints
            lib.scaleRange = new Vector2(1f, 1f);
            var variants = new System.Collections.Generic.List<PropVariant>();
            foreach (Def d in Defs)
            {
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"Assets/Sprites/Props/{d.file}.png");
                if (tex == null)
                {
                    Debug.LogWarning($"prop sprite missing: {d.file}");
                    continue;
                }

                string path = $"Assets/Data/Props/{d.file}_{d.kind}.asset";
                PropVariant v = AssetDatabase.LoadAssetAtPath<PropVariant>(path);
                if (v == null)
                {
                    v = ScriptableObject.CreateInstance<PropVariant>();
                    AssetDatabase.CreateAsset(v, path);
                }

                v.kind = d.kind; v.sprite = tex; v.cellPixels = d.cell; v.pixelsPerUnit = 64f; v.footprintRadius = d.radius;
                v.solid = d.solid; v.resource = d.res; v.resourceAmount = d.amount; v.weight = d.weight; v.baseScale = d.scale; v.scaleJitter = d.jitter;
                v.tintMaterials = new Material[lib.tints.Length];
                for (int t = 0; t < lib.tints.Length; t++)
                {
                    v.tintMaterials[t] = SpriteMaterial($"Assets/Materials/Props/{d.file}_{t}.mat", tex, lib.tints[t]);
                }

                Color depletedTint = Color.gray;
                if (d.res != ResourceKind.None && rulesForDepleted != null && rulesForDepleted.TryGet(d.res, out GatherRule gr)) depletedTint = gr.depletedTint;
                v.depletedMaterial = SpriteMaterial($"Assets/Materials/Props/{d.file}_depleted.mat", tex, depletedTint);
                v.fadeMaterial = FadeMaterial($"Assets/Materials/Props/{d.file}_fade.mat", tex);
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
