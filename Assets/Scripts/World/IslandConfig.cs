using UnityEngine;

namespace JurassicPark.World
{
    public enum Biome
    {
        Beach,
        Jungle,
        RockField,
        Clearing,
    }

    [System.Serializable]
    public struct BiomeDensity
    {
        public Biome biome;
        [Tooltip("Props per 100 square meters.")]
        public float trees;
        public float rocks;
        public float grass;
        public float bushes;
        public float logs;
    }

    /// <summary>Everything the island generator needs besides the seed.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Island Config", fileName = "Island")]
    public sealed class IslandConfig : ScriptableObject
    {
        public TerrainConfig terrain;
        public PropLibrary props;

        [Header("Facilities")]
        public string[] facilityNames = { "CrashSite", "VisitorCenter", "PowerStation", "Paddock", "Lookout" };
        [Tooltip("Facilities sit on a ring at this fraction of the half size.")]
        [Range(0.2f, 0.9f)] public float facilityRingFraction = 0.55f;
        [Tooltip("Random radial jitter as a fraction of the half size.")]
        [Range(0f, 0.3f)] public float facilityJitter = 0.08f;
        [Min(1f)] public float facilityMinSpacing = 22f;
        [Tooltip("Props are kept out of this radius around each facility.")]
        [Min(0f)] public float facilityClearRadius = 7f;
        [Tooltip("The dock is pushed outward from its ring point until the ground is this close to sea level.")]
        [Min(0f)] public float dockShoreTolerance = 0.35f;

        [Header("Base")]
        [Tooltip("Flattened, prop-free circle next to the crash site.")]
        [Min(1f)] public float baseClearingRadius = 9f;

        [Header("Scatter")]
        [Tooltip("Minimum distance between any two props, in meters.")]
        [Min(0.5f)] public float minPropSpacing = 1.6f;
        [Tooltip("Size of the low-frequency biome noise in meters.")]
        [Min(4f)] public float biomeFeatureSize = 45f;
        [Tooltip("Ground steeper than this fraction of the slope cap gets no props.")]
        [Range(0f, 1f)] public float maxPropSlope = 0.8f;
        [Tooltip("Land this close to sea level (meters) counts as beach.")]
        [Min(0f)] public float beachBand = 0.6f;
        public BiomeDensity[] densities =
        {
            new BiomeDensity { biome = Biome.Beach, trees = 0.4f, rocks = 0.3f, grass = 0f, bushes = 0f, logs = 0.2f },
            new BiomeDensity { biome = Biome.Jungle, trees = 5f, rocks = 0.4f, grass = 3f, bushes = 1.2f, logs = 0.4f },
            new BiomeDensity { biome = Biome.RockField, trees = 0.6f, rocks = 4f, grass = 0.8f, bushes = 0.2f, logs = 0.1f },
            new BiomeDensity { biome = Biome.Clearing, trees = 0.8f, rocks = 0.5f, grass = 4f, bushes = 1.5f, logs = 0.3f },
        };

        [Tooltip("Retry the whole plan with a derived seed if the facility constraints fail.")]
        [Range(1, 50)] public int maxAttempts = 20;
    }
}
