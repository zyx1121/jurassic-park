using UnityEngine;

namespace JurassicPark.World
{
    [CreateAssetMenu(menuName = "Jurassic Park/World Style", fileName = "WorldStyle")]
    public sealed class WorldStyleConfig : ScriptableObject
    {
        [Header("Original project art, shared pixel treatment")]
        [Min(1)] public int pixelStep = 2;
        [Min(2)] public int foliageRampColors = 7;
        [Min(0.1f)] public float treeScale = 0.7f;
        [Min(0.1f)] public float boulderScale = 0.8f;
        [Min(0.1f)] public float undergrowthScale = 0.8f;
        [Min(0.1f)] public float terrainTileSize = 3f;
        public Color32[] palette =
        {
            new Color32(23, 36, 32, 255), new Color32(31, 53, 41, 255),
            new Color32(42, 72, 49, 255), new Color32(57, 91, 56, 255),
            new Color32(79, 112, 64, 255), new Color32(111, 138, 79, 255),
            new Color32(150, 162, 104, 255), new Color32(191, 195, 140, 255),
            new Color32(51, 42, 33, 255), new Color32(76, 60, 42, 255),
            new Color32(102, 80, 52, 255), new Color32(133, 107, 70, 255),
            new Color32(166, 140, 93, 255), new Color32(198, 174, 124, 255),
            new Color32(224, 204, 159, 255), new Color32(235, 224, 193, 255),
            new Color32(40, 50, 52, 255), new Color32(59, 72, 74, 255),
            new Color32(82, 94, 94, 255), new Color32(112, 122, 117, 255),
            new Color32(150, 158, 143, 255), new Color32(189, 195, 179, 255),
            new Color32(123, 63, 46, 255), new Color32(172, 97, 57, 255),
            new Color32(214, 151, 69, 255), new Color32(235, 192, 100, 255)
        };

        public float ScaleFor(PropKind kind)
        {
            if (kind == PropKind.Tree) return treeScale;
            if (kind == PropKind.Boulder) return boulderScale;
            if (kind == PropKind.Grass || kind == PropKind.Bush || kind == PropKind.Clutter) return undergrowthScale;
            return 1f;
        }
    }
}
