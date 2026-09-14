using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>Pure heightmap and splat math so the island is deterministic per seed and testable without a scene.</summary>
    public static class TerrainNoise
    {
        public const int Sand = 0;
        public const int GrassA = 1;
        public const int GrassB = 2;
        public const int Dirt = 3;
        public const int Rock = 4;
        public const int LayerCount = 5;

        /// <summary>Normalized heights [0,1], resolution x resolution, seeded, with radial island falloff.</summary>
        public static float[,] Heightmap(int seed, TerrainConfig c)
        {
            int n = c.heightmapResolution;
            float[,] h = new float[n, n];
            System.Random rng = new System.Random(seed);
            float ox = (float)rng.NextDouble() * 1000f;
            float oy = (float)rng.NextDouble() * 1000f;
            float cell = c.size / (n - 1);
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float wx = x * cell;
                    float wy = y * cell;
                    float v = Fbm(wx / c.featureSize + ox, wy / c.featureSize + oy, c.octaves, c.persistence);
                    if (c.islandFalloff > 0f)
                    {
                        float dx = (x / (float)(n - 1)) * 2f - 1f;
                        float dy = (y / (float)(n - 1)) * 2f - 1f;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float falloff = Mathf.Clamp01(1f - Mathf.Pow(r, 3f));
                        v = Mathf.Lerp(v, v * falloff, c.islandFalloff);
                    }

                    h[y, x] = v;
                }
            }

            return h;
        }

        public static float Fbm(float x, float y, int octaves, float persistence)
        {
            float sum = 0f;
            float amp = 1f;
            float total = 0f;
            float freq = 1f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x * freq, y * freq) * amp;
                total += amp;
                amp *= persistence;
                freq *= 2f;
            }

            return Mathf.Clamp01(sum / total);
        }

        /// <summary>
        /// Limits the height difference between 4-neighbours to maxStep (normalized) by lowering
        /// cells only: h[i] = min(h[i], h[j] + maxStep). A forward and a backward sweep per pass
        /// propagate the constraint in every direction, so one pass already converges for the
        /// 4-neighbour case; extra passes are a safety net.
        /// </summary>
        public static void LimitSlope(float[,] h, float maxStep, int passes)
        {
            int n = h.GetLength(0);
            for (int p = 0; p < Mathf.Max(1, passes); p++)
            {
                bool changed = false;
                for (int y = 0; y < n; y++)
                {
                    for (int x = 0; x < n; x++)
                    {
                        changed |= Lower(h, x, y, maxStep);
                    }
                }

                for (int y = n - 1; y >= 0; y--)
                {
                    for (int x = n - 1; x >= 0; x--)
                    {
                        changed |= Lower(h, x, y, maxStep);
                    }
                }

                if (!changed)
                {
                    break;
                }
            }
        }

        private static bool Lower(float[,] h, int x, int y, float maxStep)
        {
            int n = h.GetLength(0);
            float limit = float.MaxValue;
            if (x > 0) limit = Mathf.Min(limit, h[y, x - 1] + maxStep);
            if (x + 1 < n) limit = Mathf.Min(limit, h[y, x + 1] + maxStep);
            if (y > 0) limit = Mathf.Min(limit, h[y - 1, x] + maxStep);
            if (y + 1 < n) limit = Mathf.Min(limit, h[y + 1, x] + maxStep);
            if (h[y, x] > limit)
            {
                h[y, x] = limit;
                return true;
            }

            return false;
        }

        /// <summary>Largest normalized height difference to any 4-neighbour.</summary>
        public static float MaxSlope(float[,] h)
        {
            int n = h.GetLength(0);
            float m = 0f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    if (x + 1 < n) m = Mathf.Max(m, Mathf.Abs(h[y, x] - h[y, x + 1]));
                    if (y + 1 < n) m = Mathf.Max(m, Mathf.Abs(h[y, x] - h[y + 1, x]));
                }
            }

            return m;
        }

        /// <summary>
        /// Splat weights [res, res, LayerCount] from height and slope: sand near sea level, rock on steep
        /// cells, dirt clearings and a second grass from a low-frequency noise. Weights sum to 1.
        /// </summary>
        public static float[,,] Splat(float[,] h, int seed, TerrainConfig c, int alphaResolution)
        {
            int n = h.GetLength(0);
            float[,,] w = new float[alphaResolution, alphaResolution, LayerCount];
            System.Random rng = new System.Random(seed + 17);
            float ox = (float)rng.NextDouble() * 500f;
            float oy = (float)rng.NextDouble() * 500f;
            float maxStepNorm = c.maxStepPerCell / c.maxHeight;
            float seaNorm = c.seaLevel / c.maxHeight;
            float sandNorm = (c.seaLevel + c.sandBand) / c.maxHeight;
            for (int ay = 0; ay < alphaResolution; ay++)
            {
                for (int ax = 0; ax < alphaResolution; ax++)
                {
                    float u = ax / (float)(alphaResolution - 1);
                    float v = ay / (float)(alphaResolution - 1);
                    int hx = Mathf.Clamp(Mathf.RoundToInt(u * (n - 1)), 0, n - 2);
                    int hy = Mathf.Clamp(Mathf.RoundToInt(v * (n - 1)), 0, n - 2);
                    float height = h[hy, hx];
                    float slope = Mathf.Max(Mathf.Abs(h[hy, hx] - h[hy, hx + 1]), Mathf.Abs(h[hy, hx] - h[hy + 1, hx])) / Mathf.Max(maxStepNorm, 1e-5f);
                    float variety = Mathf.PerlinNoise(u * 6f + ox, v * 6f + oy);
                    float clearing = Mathf.PerlinNoise(u * 3f + oy, v * 3f + ox);

                    int layer;
                    if (height < sandNorm)
                    {
                        layer = Sand;
                    }
                    else if (slope > c.rockSlope)
                    {
                        layer = Rock;
                    }
                    else if (clearing > c.dirtThreshold)
                    {
                        layer = Dirt;
                    }
                    else
                    {
                        layer = variety > c.grassMix ? GrassB : GrassA;
                    }

                    // Soft edge between sand and grass so the beach does not cut hard
                    float sandBlend = height < sandNorm ? 1f : Mathf.Clamp01(1f - (height - sandNorm) / Mathf.Max(0.02f, sandNorm - seaNorm));
                    for (int l = 0; l < LayerCount; l++)
                    {
                        w[ay, ax, l] = l == layer ? 1f : 0f;
                    }

                    if (layer != Sand && sandBlend > 0f)
                    {
                        w[ay, ax, layer] = 1f - sandBlend * 0.6f;
                        w[ay, ax, Sand] = sandBlend * 0.6f;
                    }
                }
            }

            return w;
        }
    }
}
