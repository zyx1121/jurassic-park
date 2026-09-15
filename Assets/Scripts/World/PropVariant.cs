using JurassicPark.Core;
using UnityEngine;

namespace JurassicPark.World
{
    public enum PropKind
    {
        Tree,
        Rock,
        Grass,
        Log,
        Bush,
    }

    /// <summary>One prop sprite with its footprint and what it yields. Several variants per kind keep the island from repeating.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Prop Variant", fileName = "Prop")]
    public sealed class PropVariant : ScriptableObject
    {
        public PropKind kind = PropKind.Tree;
        public Texture2D sprite;
        [Tooltip("Sprite cell size in pixels; height in meters is cell / pixelsPerUnit.")]
        [Min(8)] public int cellPixels = 128;
        [Min(1f)] public float pixelsPerUnit = 64f;
        [Tooltip("Radius on the ground other props and structures keep clear, in meters at scale 1.")]
        [Min(0f)] public float footprintRadius = 0.6f;
        [Tooltip("Blocks movement and carves the NavMesh.")]
        public bool solid = true;
        public ResourceKind resource = ResourceKind.None;
        [Min(0)] public int resourceAmount = 0;
        [Tooltip("Relative chance when a kind is picked at random.")]
        [Min(0f)] public float weight = 1f;

        public float HeightMeters => cellPixels / pixelsPerUnit;
    }
}
