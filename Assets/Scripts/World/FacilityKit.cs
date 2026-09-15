using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>A named landmark assembled from several sprites (art repo out/facilities/LAYOUT.md).</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Facility Kit", fileName = "Facility")]
    public sealed class FacilityKit : ScriptableObject
    {
        [Tooltip("Matches IslandConfig.facilityNames or \"Dock\".")]
        public string facilityName;
        [Tooltip("Props are kept out of this radius around the kit origin.")]
        [Min(1f)] public float clearRadius = 10f;
        [Tooltip("Boat part position in the kit's walkable route (x east, y north).")]
        public Vector2 pickupOffset;
        public FacilityPiece[] pieces = new FacilityPiece[0];

        public bool BlocksPoint(Vector2 localPoint, float clearance)
        {
            foreach (FacilityPiece piece in pieces)
            {
                if (!piece.solid || piece.colliders == null) continue;
                Vector2 delta = localPoint - piece.offset;
                Vector3 p = Quaternion.Euler(0f, -piece.yaw, 0f) * new Vector3(delta.x, 0f, delta.y);
                foreach (FacilityCollider box in piece.colliders)
                {
                    if (Mathf.Abs(p.x - box.center.x) < box.size.x * 0.5f + clearance &&
                        Mathf.Abs(p.z - box.center.z) < box.size.z * 0.5f + clearance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
