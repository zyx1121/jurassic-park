using JurassicPark.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicPark.World
{
    /// <summary>Icons and materials for pickups, and the one place that spawns them.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Pickup Library", fileName = "PickupLibrary")]
    public sealed class PickupLibrary : ScriptableObject
    {
        public Texture2D wood;
        public Texture2D stone;
        public Texture2D food;
        public Texture2D boatPart;
        [Min(8f)] public float pixelsPerUnit = 64f;
        [Min(0.1f)] public float collectRadius = 1.1f;
        public Material spriteMaterial;

        public Texture2D Icon(ResourceKind kind)
        {
            switch (kind)
            {
                case ResourceKind.Wood: return wood;
                case ResourceKind.Stone: return stone;
                case ResourceKind.Food: return food;
                case ResourceKind.BoatPart: return boatPart;
                default: return null;
            }
        }
    }
}
