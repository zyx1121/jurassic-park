using UnityEngine;

namespace JurassicPark.Building
{
    [CreateAssetMenu(menuName = "Jurassic Park/Structure Library", fileName = "StructureLibrary")]
    public sealed class StructureLibrary : ScriptableObject
    {
        public StructureDef[] structures = new StructureDef[0];
        [Min(0.25f)] public float cellSize = 1f;
        [Tooltip("How far in front of the player the preview sits, in meters.")]
        [Min(0.5f)] public float placeDistance = 2f;
        [Tooltip("Ground steeper than this height difference across the footprint is not buildable.")]
        [Min(0f)] public float maxHeightDelta = 0.6f;
        public Material previewValid;
        public Material previewInvalid;
        public Material wood;
        public Material stone;
    }
}
