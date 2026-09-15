using JurassicPark.Core;
using JurassicPark.Scene;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicPark.World
{
    /// <summary>
    /// Spawns a prop variant as a billboarded, lit, alpha-clipped quad using the variant's shared
    /// tint material (SRP-batchable and serializable, unlike property blocks), with a seeded random
    /// scale and tint and a collider sized from the footprint.
    /// </summary>
    public static class PropPlacer
    {
        private static Material fallbackTemplate;

        public static float PickScale(PropVariant v, System.Random rng)
        {
            float j = v.scaleJitter;
            return v.baseScale * Mathf.Lerp(1f - j, 1f + j, (float)rng.NextDouble());
        }

        public static int PickTintIndex(PropLibrary lib, System.Random rng)
        {
            return lib.tints == null || lib.tints.Length == 0 ? 0 : rng.Next(lib.tints.Length);
        }

        public static GameObject Place(PropVariant v, PropLibrary lib, Vector3 groundPosition, System.Random rng, Transform parent = null)
        {
            float scale = PickScale(v, rng);
            int tint = PickTintIndex(lib, rng);
            return Place(v, groundPosition, scale, tint, parent, lib.gatherRules, lib.pickups);
        }

        public static GameObject Place(PropVariant v, Vector3 groundPosition, float scale, int tintIndex, Transform parent = null, GatherRules rules = null, PickupLibrary pickups = null)
        {
            GameObject root = new GameObject(v.name);
            root.transform.SetParent(parent, false);
            root.transform.position = groundPosition;
            PropInstance inst = root.AddComponent<PropInstance>();
            inst.variant = v;
            inst.scale = scale;
            inst.tintIndex = tintIndex;

            float size = v.HeightMeters * scale;
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Sprite";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            quad.transform.localPosition = new Vector3(0f, size * 0.5f - 0.05f, 0f);
            quad.transform.localScale = new Vector3(size, size, 1f);
            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            Material mat = v.MaterialFor(tintIndex);
            if (mat == null)
            {
                // No generated material (tests, ad hoc placement): fall back to a runtime material per call
                mat = new Material(FallbackTemplate()) { name = v.name + " (runtime)" };
                mat.SetTexture("_BaseMap", v.sprite);
            }

            mr.sharedMaterial = mat;
            mr.shadowCastingMode = v.solid ? ShadowCastingMode.TwoSided : ShadowCastingMode.Off;
            mr.receiveShadows = true;
            quad.AddComponent<Billboard>();

            if (v.solid)
            {
                CapsuleCollider col = root.AddComponent<CapsuleCollider>();
                col.radius = v.footprintRadius * scale;
                col.height = size;
                col.center = new Vector3(0f, size * 0.5f, 0f);
            }

            // Anything tall enough to hide the player fades when it stands between camera and player:
            // trees and boulders always, plus fern walls and other clutter over 1.2 m. The trigger volume
            // covers the whole sprite so the camera cast catches canopies above the trunk collider.
            bool tall = size >= 1.2f;
            if (v.fadeMaterial != null && (v.kind == PropKind.Tree || v.kind == PropKind.Boulder || tall))
            {
                CapsuleCollider canopy = root.AddComponent<CapsuleCollider>();
                canopy.isTrigger = true;
                canopy.radius = Mathf.Max(v.footprintRadius * scale, size * 0.28f);
                canopy.height = size;
                canopy.center = new Vector3(0f, size * 0.5f, 0f);
                Occluder occ = root.AddComponent<Occluder>();
                occ.Configure(mr, mat, v.fadeMaterial);
            }

            if (v.resource != ResourceKind.None && v.resourceAmount > 0 && rules != null && rules.TryGet(v.resource, out GatherRule rule))
            {
                if (!v.solid)
                {
                    SphereCollider trigger = root.AddComponent<SphereCollider>();
                    trigger.isTrigger = true;
                    trigger.radius = Mathf.Max(0.5f, v.footprintRadius * scale + 0.3f);
                    trigger.center = new Vector3(0f, size * 0.4f, 0f);
                }

                ResourceNode node = root.AddComponent<ResourceNode>();
                node.Configure(v.resource, v.resourceAmount, rule, pickups, v.depletedMaterial);
            }

            return root;
        }

        /// <summary>Lit, alpha-clipped, double-sided template for sprite quads.</summary>
        public static Material FallbackTemplate()
        {
            if (fallbackTemplate == null)
            {
                fallbackTemplate = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "SpriteQuad (runtime template)" };
                ConfigureSpriteMaterial(fallbackTemplate, null);
            }

            return fallbackTemplate;
        }

        /// <summary>Shared setup for every sprite-quad material: lit, cutout at 0.5, no culling, matte.</summary>
        public static void ConfigureSpriteMaterial(Material m, Texture2D texture, Color? tint = null)
        {
            m.SetFloat("_Smoothness", 0f);
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.doubleSidedGI = true;
            if (texture != null) m.SetTexture("_BaseMap", texture);
            m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetTextureOffset("_BaseMap", Vector2.zero);
            if (tint.HasValue) m.SetColor("_BaseColor", tint.Value);
        }
    }
}
