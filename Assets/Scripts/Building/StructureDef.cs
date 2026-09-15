using System;
using JurassicPark.Combat;
using JurassicPark.Core;
using UnityEngine;

namespace JurassicPark.Building
{
    public enum StructureKind
    {
        Fence,
        Wall,
        Gate,
        Campfire,
        Torch,
    }

    [Serializable]
    public struct ResourceCost
    {
        public ResourceKind kind;
        [Min(0)] public int amount;
    }

    /// <summary>One buildable thing: footprint on the 1 m grid, cost, health, and how it behaves.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Structure Def", fileName = "Structure")]
    public sealed class StructureDef : ScriptableObject
    {
        public StructureKind kind = StructureKind.Fence;
        public string displayName = "Fence";
        [Tooltip("Footprint in grid cells along the structure's local X (width) and Z (depth).")]
        public Vector2Int footprint = new Vector2Int(2, 1);
        [Min(0.1f)] public float height = 1.4f;
        public ResourceCost[] cost = { new ResourceCost { kind = ResourceKind.Wood, amount = 3 } };
        public HealthConfig health;
        [Tooltip("Blocks movement and carves the NavMesh.")]
        public bool solid = true;
        [Tooltip("Can be opened and closed with Interact.")]
        public bool isGate = false;
        [Tooltip("Emits light (torch, campfire).")]
        public bool emitsLight = false;
        [Tooltip("Optional prefab; when null the StructureFactory builds simple geometry.")]
        public GameObject prefab;
        [Tooltip("Resources refunded when a structure is repaired to full: fraction of the cost per repair.")]
        [Range(0f, 1f)] public float repairCostFraction = 0.5f;
    }
}
