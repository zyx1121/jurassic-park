using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>Turns TerrainConfig plus a seed into a live Unity Terrain. Works in the Editor and at runtime.</summary>
    public static class TerrainBuilder
    {
        public static System.Func<TerrainData, string, TerrainData> PersistTerrainData;

        public static Terrain Build(TerrainConfig config, int seed, Transform parent = null, Material material = null)
        {
            float[,] heights = TerrainNoise.Heightmap(seed, config);
            TerrainNoise.LimitSlope(heights, config.maxStepPerCell / config.maxHeight, config.slopePasses);

            TerrainData data = new TerrainData
            {
                heightmapResolution = config.heightmapResolution,
                size = new Vector3(config.size, config.maxHeight, config.size),
                alphamapResolution = Mathf.Max(16, config.heightmapResolution - 1),
            };
            data.SetHeights(0, 0, heights);
            if (config.layers != null && config.layers.Length >= TerrainNoise.LayerCount)
            {
                data.terrainLayers = config.layers;
                float[,,] splat = TerrainNoise.Splat(heights, seed, config, data.alphamapResolution);
                data.SetAlphamaps(0, 0, splat);
            }

            if (PersistTerrainData != null)
            {
                data = PersistTerrainData(data, $"LookTestTerrain_seed{seed}");
            }

            GameObject go = Terrain.CreateTerrainGameObject(data);
            go.name = "Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(-config.size * 0.5f, 0f, -config.size * 0.5f);
            Terrain terrain = go.GetComponent<Terrain>();
            terrain.heightmapPixelError = 4f;
            terrain.drawInstanced = true;
            if (material != null)
            {
                terrain.materialTemplate = material;
            }

            return terrain;
        }

        /// <summary>World-space Y of the terrain surface at (x, z). Falls back to 0 when no terrain exists.</summary>
        public static float SampleHeight(Vector3 worldXZ)
        {
            Terrain t = Terrain.activeTerrain;
            return t != null ? t.SampleHeight(worldXZ) + t.transform.position.y : 0f;
        }

        public static Vector3 OnGround(Vector3 worldXZ, float lift = 0f)
        {
            return new Vector3(worldXZ.x, SampleHeight(worldXZ) + lift, worldXZ.z);
        }
    }
}
