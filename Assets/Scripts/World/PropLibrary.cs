using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>All prop variants plus the random ranges applied when one is placed.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Prop Library", fileName = "PropLibrary")]
    public sealed class PropLibrary : ScriptableObject
    {
        public PropVariant[] variants = new PropVariant[0];
        public GatherRules gatherRules;

        [Header("Per-placement variation")]
        public Vector2 scaleRange = new Vector2(0.8f, 1.3f);
        [Tooltip("Tints multiplied into the sprite color; white means no change.")]
        public Color[] tints =
        {
            Color.white,
            new Color(0.92f, 0.96f, 0.85f),
            new Color(0.85f, 0.9f, 1f),
            new Color(0.95f, 0.85f, 0.8f),
        };

        public PropVariant Pick(PropKind kind, System.Random rng)
        {
            float total = 0f;
            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] != null && variants[i].kind == kind) total += variants[i].weight;
            }

            if (total <= 0f)
            {
                return null;
            }

            float r = (float)rng.NextDouble() * total;
            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] == null || variants[i].kind != kind) continue;
                r -= variants[i].weight;
                if (r <= 0f) return variants[i];
            }

            return null;
        }

        public IEnumerable<PropVariant> OfKind(PropKind kind)
        {
            for (int i = 0; i < variants.Length; i++)
            {
                if (variants[i] != null && variants[i].kind == kind) yield return variants[i];
            }
        }
    }
}
