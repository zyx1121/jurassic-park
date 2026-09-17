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
        public float boulders;
        public float stones;
        public float clutter;
    }

    /// <summary>Everything the island generator needs besides the seed.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Island Config", fileName = "Island")]
    public sealed class IslandConfig : ScriptableObject
    {
        public TerrainConfig terrain;
        public PropLibrary props;
        [Tooltip("Facility kits assembled at each facility slot; a slot without a kit gets a placeholder pad.")]
        public FacilityLibrary facilities;

        [Header("Facilities")]
        public string[] facilityNames = { "CrashSite", "VisitorCenter", "PowerStation", "Paddock", "Lookout" };
        [Tooltip("Facilities sit on a ring at this fraction of the half size.")]
        [Range(0.2f, 0.9f)] public float facilityRingFraction = 0.55f;
        [Tooltip("Random radial jitter as a fraction of the half size.")]
        [Range(0f, 0.3f)] public float facilityJitter = 0.08f;
        [Min(1f)] public float facilityMinSpacing = 22f;
        [Tooltip("Props are kept out of this radius around a facility that has no kit; kits carry their own radius.")]
        [Min(0f)] public float facilityClearRadius = 7f;

        public float ClearRadiusFor(string facilityName)
        {
            FacilityKit kit = facilities != null ? facilities.Find(facilityName) : null;
            return kit != null ? kit.clearRadius : facilityClearRadius;
        }
        [Tooltip("The dock is pushed outward from its ring point until the ground is this close to sea level.")]
        [Min(0f)] public float dockShoreTolerance = 0.35f;

        [Header("Base")]
        [Tooltip("Flattened, prop-free circle next to the crash site.")]
        [Min(1f)] public float baseClearingRadius = 9f;
        [Tooltip("Original stepped rock ridges create a readable, single-entrance camp.")]
        public bool campRidges = true;
        [Min(0.5f)] public float campRidgeHeight = 2.8f;
        [Min(0.5f)] public float campRidgeThickness = 2.2f;
        [Min(1f)] public float campEntranceWidth = 4f;
        [Min(0f)] public float campCapOffset = 0.015f;
        [Range(0f, 1f)] public float baseGrassFraction = 0.15f;
        public Material campRockMaterial;
        public Material campTopMaterial;
        [Tooltip("Dry, obstacle-free space reserved around the beach spawn.")]
        [Min(0.5f)] public float spawnClearRadius = 1.2f;
        [Min(0.01f)] public float spawnShoreMargin = 0.1f;
        [Min(0.25f)] public float spawnSearchStep = 0.75f;
        [Min(4)] public int spawnSearchDirections = 16;

        [Header("Scatter")]
        [Tooltip("Minimum distance between any two props, in meters.")]
        [Min(0.5f)] public float minPropSpacing = 0.7f;
        [Tooltip("Size of the low-frequency biome noise in meters.")]
        [Min(4f)] public float biomeFeatureSize = 45f;
        [Tooltip("Ground steeper than this fraction of the slope cap gets no props.")]
        [Range(0f, 1f)] public float maxPropSlope = 0.8f;
        [Tooltip("Land this close to sea level (meters) counts as beach.")]
        [Min(0f)] public float beachBand = 0.6f;
        public BiomeDensity[] densities =
        {
            // items per 100 m2, from the v2 art handoff (art repo out/REPORT_V2.md) except trees: the handoff's
            // 9-13 trees per 100 m2 at 7.5 m turned the 3/4 view into solid canopy, 4.5 keeps the ground readable
            new BiomeDensity { biome = Biome.Beach, trees = 1.2f, rocks = 4f, grass = 1.5f, bushes = 0.4f, logs = 0.8f, boulders = 1.0f, stones = 10f, clutter = 7f },
            new BiomeDensity { biome = Biome.Jungle, trees = 4.5f, rocks = 3f, grass = 14f, bushes = 5f, logs = 1.2f, boulders = 0.8f, stones = 6f, clutter = 22f },
            new BiomeDensity { biome = Biome.RockField, trees = 2f, rocks = 5f, grass = 4f, bushes = 1f, logs = 0.4f, boulders = 2.5f, stones = 9f, clutter = 14f },
            new BiomeDensity { biome = Biome.Clearing, trees = 1.5f, rocks = 1.5f, grass = 10f, bushes = 4f, logs = 0.8f, boulders = 0.4f, stones = 4f, clutter = 12f },
        };

        [Tooltip("Retry the whole plan with a derived seed if the facility constraints fail.")]
        [Range(1, 50)] public int maxAttempts = 20;
    }
}
