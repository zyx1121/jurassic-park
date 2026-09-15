using System;
using UnityEngine;

namespace JurassicPark.Core
{
    [Serializable]
    public struct ResourceCap
    {
        public ResourceKind kind;
        [Min(1)] public int max;
    }

    /// <summary>How much of each resource one player can carry.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Inventory Config", fileName = "Inventory")]
    public sealed class InventoryConfig : ScriptableObject
    {
        public ResourceCap[] caps =
        {
            new ResourceCap { kind = ResourceKind.Wood, max = 30 },
            new ResourceCap { kind = ResourceKind.Stone, max = 30 },
            new ResourceCap { kind = ResourceKind.Food, max = 10 },
            new ResourceCap { kind = ResourceKind.BoatPart, max = 1 },
        };

        public int Cap(ResourceKind kind)
        {
            for (int i = 0; i < caps.Length; i++)
            {
                if (caps[i].kind == kind) return caps[i].max;
            }

            return int.MaxValue;
        }
    }
}
