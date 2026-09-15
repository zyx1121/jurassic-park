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

        // Cells and placement scales follow docs/ART_DIRECTION.md and the v2 handoff (art repo out/props_v2/SCALE.md):
        // baseScale = intended height / (cell / 64). Trees 7 to 8 m, boulders 2.2 to 3.6 m, rocks about 1 m, stones under 0.5 m.
        private static readonly Def[] Defs =
        {
            // Trees: solid trunk proxy only, never gatherable per the handoff; wood comes from logs
            new Def("tree_buttress_giant", PropKind.Tree, 256, 0.8f, true, ResourceKind.Wood, 6, 1.2f, 1.88f, 0.08f),
            new Def("tree_strangler_fig", PropKind.Tree, 256, 0.75f, true, ResourceKind.Wood, 6, 1.0f, 1.79f, 0.08f),
            new Def("tree_thin_palm", PropKind.Tree, 256, 0.35f, true, ResourceKind.Wood, 4, 0.9f, 2.04f, 0.08f),
            new Def("tree_lightning_dead", PropKind.Tree, 256, 0.5f, true, ResourceKind.Wood, 5, 0.7f, 1.93f, 0.08f),
            new Def("palm_weathered", PropKind.Tree, 192, 0.35f, true, ResourceKind.Wood, 4, 0.6f, 2.3f, 0.1f),
            // Boulders: terrain features, solid, no yield
            new Def("boulder_monolith", PropKind.Boulder, 192, 1.2f, true, ResourceKind.None, 0, 1.0f, 1.89f, 0.12f),
            new Def("boulder_fractured_slab", PropKind.Boulder, 192, 1.4f, true, ResourceKind.None, 0, 1.0f, 1.74f, 0.12f),
            new Def("boulder_twin_limestone", PropKind.Boulder, 192, 1.3f, true, ResourceKind.None, 0, 1.0f, 1.70f, 0.12f),
            new Def("boulder_buried_wedge", PropKind.Boulder, 192, 1.1f, true, ResourceKind.None, 0, 0.9f, 1.53f, 0.12f),
            // Rocks: about 1 m, solid until gathered
            new Def("rock_basalt_shard", PropKind.Rock, 128, 0.45f, true, ResourceKind.Stone, 4, 1.0f, 0.94f, 0.15f),
            new Def("rock_quartz_split", PropKind.Rock, 128, 0.5f, true, ResourceKind.Stone, 4, 1.0f, 0.97f, 0.15f),
            new Def("rock_sandstone_shelf", PropKind.Rock, 128, 0.55f, true, ResourceKind.Stone, 3, 1.0f, 0.79f, 0.15f),
            new Def("rock_limestone_knuckles", PropKind.Rock, 128, 0.5f, true, ResourceKind.Stone, 4, 1.0f, 0.85f, 0.15f),
            // Stones: flat pickups, walk-over
            new Def("stone_river_oval", PropKind.Stone, 96, 0.25f, false, ResourceKind.Stone, 1, 1.0f, 0.70f, 0.2f),
            new Def("stone_slate_flake", PropKind.Stone, 96, 0.22f, false, ResourceKind.Stone, 1, 1.0f, 0.57f, 0.2f),
            new Def("stone_granite_cobble", PropKind.Stone, 96, 0.22f, false, ResourceKind.Stone, 1, 1.0f, 0.58f, 0.2f),
            new Def("stone_rust_flat", PropKind.Stone, 96, 0.22f, false, ResourceKind.Stone, 1, 0.8f, 0.58f, 0.2f),
            // Logs and food bushes (v1 sprites)
            new Def("fallen_log", PropKind.Log, 128, 0.7f, true, ResourceKind.Wood, 2, 1.0f, 1.0f, 0.2f),
            new Def("mossy_stump", PropKind.Log, 128, 0.45f, true, ResourceKind.Wood, 1, 0.8f, 0.95f, 0.2f),
            new Def("grass_tuft", PropKind.Grass, 96, 0.2f, false, ResourceKind.None, 0, 1.5f, 0.9f, 0.25f),
            new Def("fern_tuft", PropKind.Grass, 96, 0.25f, false, ResourceKind.None, 0, 1.2f, 1.0f, 0.25f),
            new Def("bush_tangled", PropKind.Bush, 96, 0.4f, false, ResourceKind.Food, 1, 1.0f, 1.0f, 0.16f),
            new Def("bush_wide", PropKind.Bush, 96, 0.45f, false, ResourceKind.Food, 2, 0.8f, 1.05f, 0.16f),
            // Jungle clutter: non-solid unless it is a real obstacle
            new Def("clutter_fern_wall_a", PropKind.Clutter, 128, 0.5f, false, ResourceKind.None, 0, 1.4f, 0.9f, 0.15f),
            new Def("clutter_fern_wall_b", PropKind.Clutter, 128, 0.5f, false, ResourceKind.None, 0, 1.2f, 0.68f, 0.15f),
            new Def("clutter_broadleaf_a", PropKind.Clutter, 128, 0.45f, false, ResourceKind.None, 0, 1.2f, 0.73f, 0.15f),
            new Def("clutter_broadleaf_b", PropKind.Clutter, 128, 0.45f, false, ResourceKind.None, 0, 1.2f, 0.8f, 0.15f),
            new Def("clutter_vine_coil", PropKind.Clutter, 96, 0.3f, false, ResourceKind.None, 0, 0.5f, 0.6f, 0.2f),
            new Def("clutter_root_snake", PropKind.Clutter, 128, 0.4f, false, ResourceKind.None, 0, 0.8f, 0.7f, 0.2f),
            new Def("clutter_root_arch", PropKind.Clutter, 128, 0.5f, true, ResourceKind.None, 0, 0.5f, 0.55f, 0.15f),
            new Def("clutter_branch_forked", PropKind.Clutter, 128, 0.4f, false, ResourceKind.None, 0, 0.8f, 0.65f, 0.2f),
            new Def("clutter_branch_fungus", PropKind.Clutter, 128, 0.4f, false, ResourceKind.None, 0, 0.7f, 0.6f, 0.2f),
            new Def("clutter_leaf_litter_a", PropKind.Clutter, 96, 0.3f, false, ResourceKind.None, 0, 1.0f, 0.7f, 0.25f),
            new Def("clutter_leaf_litter_b", PropKind.Clutter, 96, 0.3f, false, ResourceKind.None, 0, 1.0f, 0.7f, 0.25f),
            new Def("clutter_mushroom_shelf", PropKind.Clutter, 96, 0.25f, false, ResourceKind.None, 0, 0.5f, 0.6f, 0.2f),
            new Def("clutter_mushroom_stems", PropKind.Clutter, 96, 0.25f, false, ResourceKind.None, 0, 0.5f, 0.65f, 0.2f),
            new Def("clutter_bones_ribs", PropKind.Clutter, 128, 0.4f, false, ResourceKind.None, 0, 0.08f, 0.6f, 0.1f),
            new Def("clutter_bones_skull", PropKind.Clutter, 96, 0.3f, false, ResourceKind.None, 0, 0.08f, 0.55f, 0.1f),
            // Park junk: rare in the open, dense around facilities once #80 dresses them
            new Def("junk_crate_broken", PropKind.Clutter, 96, 0.45f, true, ResourceKind.None, 0, 0.25f, 0.85f, 0.1f),
            new Def("junk_barrel_rusted", PropKind.Clutter, 96, 0.35f, true, ResourceKind.None, 0, 0.25f, 0.8f, 0.1f),
            new Def("junk_warning_sign", PropKind.Clutter, 96, 0.2f, false, ResourceKind.None, 0, 0.2f, 1.15f, 0.05f),
            new Def("junk_tire_muddy", PropKind.Clutter, 96, 0.35f, false, ResourceKind.None, 0, 0.25f, 0.7f, 0.1f),
            new Def("junk_fence_scrap", PropKind.Clutter, 128, 0.6f, true, ResourceKind.None, 0, 0.2f, 0.9f, 0.1f),
            new Def("junk_tarp_collapsed", PropKind.Clutter, 128, 0.5f, false, ResourceKind.None, 0, 0.25f, 0.65f, 0.1f),
            new Def("junk_suitcase_open", PropKind.Clutter, 128, 0.4f, false, ResourceKind.None, 0, 0.2f, 0.6f, 0.1f),
            new Def("junk_cable_reel", PropKind.Clutter, 128, 0.45f, true, ResourceKind.None, 0, 0.2f, 0.7f, 0.1f),
        };

        /// <summary>Translucent (35 percent) variant shown while the prop stands between the camera and the player.</summary>
        internal static Material FadeMaterial(string path, Texture2D tex)
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
            m.SetFloat("_HoleSoftness", 0.24f);
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        internal static Material SpriteMaterial(string path, Texture2D tex, Color tint)
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
