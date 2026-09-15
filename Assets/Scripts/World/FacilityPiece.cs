using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>One sprite of a facility kit and where it sits relative to the kit origin (layout north is +z).</summary>
    [System.Serializable]
    public struct FacilityPiece
    {
        public Texture2D sprite;
        [Tooltip("Offset from the kit origin in meters: x east, y north.")]
        public Vector2 offset;
        [Tooltip("Extra yaw of the piece's collider in degrees.")]
        public float yaw;
        [Tooltip("World width of the opaque part of the sprite, in meters.")]
        [Min(0.1f)] public float widthMeters;
        [Tooltip("Sprite cell size in pixels.")]
        [Min(8)] public int cellPixels;
        [Tooltip("Width of the opaque part of the sprite, in pixels.")]
        [Min(1)] public int opaquePixels;
        public bool solid;
        [Tooltip("Separate solid proxies leave doorways, tower legs and service routes open.")]
        public FacilityCollider[] colliders;
        public Material material;
        public Material fadeMaterial;

        /// <summary>Quad size that makes the opaque width match widthMeters.</summary>
        public float QuadSize => widthMeters * cellPixels / Mathf.Max(1, opaquePixels);
    }

    [System.Serializable]
    public struct FacilityCollider
    {
        public Vector3 size;
        public Vector3 center;

        public FacilityCollider(Vector3 size, Vector3 baseOffset = default)
        {
            this.size = size;
            center = baseOffset + Vector3.up * size.y * 0.5f;
        }
    }
}
