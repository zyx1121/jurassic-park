using JurassicPark.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicPark.World
{
    /// <summary>
    /// Spawns a prop variant as a billboarded, lit, alpha-clipped quad with a seeded random scale
    /// and tint, and a collider sized from the footprint. Pure math lives in PickScale/PickTint so
    /// the generator's determinism can be tested.
    /// </summary>
    public static class PropPlacer
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static Material sharedTemplate;

        public static float PickScale(PropLibrary lib, System.Random rng)
        {
            return Mathf.Lerp(lib.scaleRange.x, lib.scaleRange.y, (float)rng.NextDouble());
        }

        public static Color PickTint(PropLibrary lib, System.Random rng)
        {
            return lib.tints == null || lib.tints.Length == 0 ? Color.white : lib.tints[rng.Next(lib.tints.Length)];
        }

        public static GameObject Place(PropVariant v, PropLibrary lib, Vector3 groundPosition, System.Random rng, Transform parent = null, Material spriteMaterial = null)
        {
            float scale = PickScale(lib, rng);
            Color tint = PickTint(lib, rng);
            return Place(v, groundPosition, scale, tint, parent, spriteMaterial, lib.gatherRules, lib.pickups);
        }

        public static GameObject Place(PropVariant v, Vector3 groundPosition, float scale, Color tint, Transform parent = null, Material spriteMaterial = null, GatherRules rules = null, PickupLibrary pickups = null)
        {
            GameObject root = new GameObject(v.name);
            root.transform.SetParent(parent, false);
            root.transform.position = groundPosition;
            PropInstance inst = root.AddComponent<PropInstance>();
            inst.variant = v;
            inst.scale = scale;
            inst.tint = tint;

            float size = v.HeightMeters * scale;
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Sprite";
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(root.transform, false);
            quad.transform.localPosition = new Vector3(0f, size * 0.5f - 0.05f, 0f);
            quad.transform.localScale = new Vector3(size, size, 1f);
            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = spriteMaterial != null ? spriteMaterial : Template();
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            var block = new MaterialPropertyBlock();
            block.SetTexture("_BaseMap", v.sprite);
            block.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, 0f));
            block.SetColor(BaseColor, tint);
            mr.SetPropertyBlock(block);
            quad.AddComponent<JurassicPark.Core.Billboard>();

            if (v.solid)
            {
                CapsuleCollider col = root.AddComponent<CapsuleCollider>();
                col.radius = v.footprintRadius * scale;
                col.height = size;
                col.center = new Vector3(0f, size * 0.5f, 0f);
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
                node.Configure(v.resource, v.resourceAmount, rule, tint, pickups);
            }

            return root;
        }

        /// <summary>One shared lit alpha-clipped material; per-prop texture and tint go through property blocks.</summary>
        public static Material Template()
        {
            if (sharedTemplate == null)
            {
                sharedTemplate = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "PropSprite (runtime)" };
                sharedTemplate.SetFloat("_Smoothness", 0f);
                sharedTemplate.SetFloat("_AlphaClip", 1f);
                sharedTemplate.SetFloat("_Cutoff", 0.5f);
                sharedTemplate.EnableKeyword("_ALPHATEST_ON");
                sharedTemplate.SetFloat("_Cull", (float)CullMode.Off);
            }

            return sharedTemplate;
        }
    }
}
