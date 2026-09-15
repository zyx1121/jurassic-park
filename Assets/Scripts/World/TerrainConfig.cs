using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>Island terrain numbers: size, height range, noise, slope limit and how layers are painted.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Terrain Config", fileName = "Terrain")]
    public sealed class TerrainConfig : ScriptableObject
    {
        [Header("Size")]
        [Tooltip("Terrain width and depth in meters.")]
        [Min(16f)] public float size = 192f;
        [Tooltip("Heightmap resolution, must be 2^n + 1.")]
        public int heightmapResolution = 257;
        [Tooltip("Highest point above terrain origin, in meters.")]
        [Min(1f)] public float maxHeight = 14f;
        [Tooltip("Height of the sea surface above terrain origin, in meters.")]
        [Min(0f)] public float seaLevel = 3.5f;

        [Header("Noise")]
        [Tooltip("Size of the largest noise feature, in meters.")]
        [Min(4f)] public float featureSize = 28f;
        [Range(1, 8)] public int octaves = 5;
        [Range(0.1f, 0.9f)] public float persistence = 0.5f;
        [Tooltip("Pushes the island up in the middle and down at the coast. 0 disables.")]
        [Range(0f, 1f)] public float islandFalloff = 0.7f;

        [Header("Slope")]
        [Tooltip("Maximum height difference between neighboring heightmap cells, in meters. Keeps everything walkable.")]
        [Min(0.05f)] public float maxStepPerCell = 0.55f;
        [Range(0, 16)] public int slopePasses = 6;

        [Header("Layers (index into terrain layers)")]
        public TerrainLayer[] layers;
        [Tooltip("Below this height above sea level the ground is sand.")]
        [Min(0f)] public float sandBand = 0.6f;
        [Tooltip("Slope (0..1 of max step) above which rock shows through.")]
        [Range(0f, 1f)] public float rockSlope = 0.75f;
        [Tooltip("Noise threshold that swaps grass A for grass B for variety.")]
        [Range(0f, 1f)] public float grassMix = 0.5f;
        [Tooltip("Noise threshold that opens dirt clearings.")]
        [Range(0f, 1f)] public float dirtThreshold = 0.78f;
    }
}
