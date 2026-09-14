using JurassicPark.World;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class TerrainTests
    {
        private static TerrainConfig Config()
        {
            var c = ScriptableObject.CreateInstance<TerrainConfig>();
            c.size = 64f; c.heightmapResolution = 33; c.maxHeight = 8f; c.seaLevel = 1.2f;
            c.featureSize = 20f; c.octaves = 3; c.maxStepPerCell = 0.35f; c.slopePasses = 8;
            return c;
        }

        private static int Hash(float[,] h)
        {
            unchecked
            {
                int acc = 17;
                foreach (float v in h) acc = acc * 31 + Mathf.RoundToInt(v * 10000f);
                return acc;
            }
        }

        [Test]
        public void SameSeedSameIsland()
        {
            var c = Config();
            Assert.AreEqual(Hash(TerrainNoise.Heightmap(42, c)), Hash(TerrainNoise.Heightmap(42, c)));
            Assert.AreNotEqual(Hash(TerrainNoise.Heightmap(42, c)), Hash(TerrainNoise.Heightmap(43, c)));
            Object.DestroyImmediate(c);
        }

        [Test]
        public void HeightsStayNormalizedAndCoastIsLow()
        {
            var c = Config();
            float[,] h = TerrainNoise.Heightmap(7, c);
            int n = h.GetLength(0);
            foreach (float v in h) Assert.That(v, Is.InRange(0f, 1f));
            float center = h[n / 2, n / 2];
            float corner = h[0, 0];
            Assert.Less(corner, center, "island falloff should sink the corners");
            Object.DestroyImmediate(c);
        }

        [Test]
        public void SlopeLimiterCapsNeighbourSteps()
        {
            var c = Config();
            float[,] h = TerrainNoise.Heightmap(3, c);
            float maxStep = c.maxStepPerCell / c.maxHeight;
            h[5, 5] = 1f; h[5, 6] = 0f; // force a cliff
            TerrainNoise.LimitSlope(h, maxStep, 32);
            Assert.LessOrEqual(TerrainNoise.MaxSlope(h), maxStep + 1e-4f);
            Object.DestroyImmediate(c);
        }

        [Test]
        public void SplatWeightsSumToOneAndUseSandNearSea()
        {
            var c = Config();
            float[,] h = TerrainNoise.Heightmap(5, c);
            TerrainNoise.LimitSlope(h, c.maxStepPerCell / c.maxHeight, 8);
            float[,,] w = TerrainNoise.Splat(h, 5, c, 32);
            for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float sum = 0f;
                for (int l = 0; l < TerrainNoise.LayerCount; l++) sum += w[y, x, l];
                Assert.AreEqual(1f, sum, 1e-4f);
            }

            Assert.Greater(w[0, 0, TerrainNoise.Sand], 0.9f, "corner is below the sand band");
            Object.DestroyImmediate(c);
        }
    }
}
