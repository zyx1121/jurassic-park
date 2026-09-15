using System;
using JurassicPark.Core;
using UnityEngine;

namespace JurassicPark.World
{
    [Serializable]
    public struct GatherRule
    {
        public ResourceKind kind;
        [Tooltip("Interact presses needed per unit of resource.")]
        [Min(1)] public int hitsPerUnit;
        [Tooltip("Days until a depleted node comes back. 0 never respawns.")]
        [Min(0)] public int respawnDays;
        [Tooltip("Sprite tint while depleted (trees keep a stump, bushes go bare).")]
        public Color depletedTint;
        [Tooltip("Hide the prop entirely while depleted instead of tinting it.")]
        public bool hideWhenDepleted;
    }

    [CreateAssetMenu(menuName = "Jurassic Park/Gather Rules", fileName = "GatherRules")]
    public sealed class GatherRules : ScriptableObject
    {
        public GatherRule[] rules =
        {
            new GatherRule { kind = ResourceKind.Wood, hitsPerUnit = 3, respawnDays = 2, depletedTint = new Color(0.45f, 0.4f, 0.35f), hideWhenDepleted = false },
            new GatherRule { kind = ResourceKind.Stone, hitsPerUnit = 4, respawnDays = 3, depletedTint = new Color(0.5f, 0.5f, 0.5f), hideWhenDepleted = true },
            new GatherRule { kind = ResourceKind.Food, hitsPerUnit = 1, respawnDays = 1, depletedTint = new Color(0.55f, 0.6f, 0.45f), hideWhenDepleted = false },
        };

        public bool TryGet(ResourceKind kind, out GatherRule rule)
        {
            for (int i = 0; i < rules.Length; i++)
            {
                if (rules[i].kind == kind)
                {
                    rule = rules[i];
                    return true;
                }
            }

            rule = default;
            return false;
        }
    }
}
