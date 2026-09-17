using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>
    /// Builds an IslandPlan from a seed with hard constraints: an irregular coast with a beach ring
    /// (from the terrain's island falloff), facilities on a ring with minimum spacing, a dock that
    /// touches the shore, a flattened prop-free base clearing next to the crash site, biome-driven
    /// prop densities, Poisson-disk spacing between props, nothing on steep ground or in the sea.
    /// Deterministic: the same seed and config always give the same plan.
    /// </summary>
    public static class IslandGenerator
    {
        public static IslandPlan Plan(IslandConfig cfg, int seed)
        {
            for (int attempt = 0; attempt < cfg.maxAttempts; attempt++)
            {
                int s = unchecked(seed + attempt * 7919);
                IslandPlan plan = TryPlan(cfg, s, attempt);
                if (plan != null)
                {
                    plan.seed = seed;
                    return plan;
                }
            }

            throw new InvalidOperationException($"island seed {seed}: facility constraints failed after {cfg.maxAttempts} attempts");
        }

        private static IslandPlan TryPlan(IslandConfig cfg, int seed, int attempt)
        {
            TerrainConfig tc = cfg.terrain;
            var rng = new System.Random(seed);
            var plan = new IslandPlan { attempt = attempt };

            float[,] h = TerrainNoise.Heightmap(seed, tc);
            float maxStepNorm = tc.maxStepPerCell / tc.maxHeight;
            TerrainNoise.LimitSlope(h, maxStepNorm, tc.slopePasses);
            int n = h.GetLength(0);
            float half = tc.size * 0.5f;
            float seaNorm = tc.seaLevel / tc.maxHeight;

            // Facilities on a ring, then the dock pushed to the shore
            float ring = half * cfg.facilityRingFraction;
            int count = cfg.facilityNames.Length + 1;
            float startAngle = (float)rng.NextDouble() * Mathf.PI * 2f;
            var slots = new List<FacilitySlot>();
            for (int i = 0; i < count; i++)
            {
                float a = startAngle + i * Mathf.PI * 2f / count + ((float)rng.NextDouble() - 0.5f) * 0.5f;
                float r = ring + ((float)rng.NextDouble() - 0.5f) * 2f * half * cfg.facilityJitter;
                Vector3 p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                bool isDock = i == count - 1;
                bool isCrash = i == 0; // the crash site is where the player wakes up: on the beach
                if (isDock || isCrash)
                {
                    p = PushToShore(p, h, tc, cfg.dockShoreTolerance);
                    if (float.IsNaN(p.x)) return null;
                }
                else if (HeightAt(h, p, tc) < seaNorm + (cfg.beachBand * 0.5f) / tc.maxHeight)
                {
                    return null; // inland facility would be in the water or on the beach
                }

                p.y = HeightAt(h, p, tc) * tc.maxHeight;
                // The dock kit's planks run toward local south, so its yaw points local -z out to sea
                float yaw = isDock ? Mathf.Atan2(-p.x, -p.z) * Mathf.Rad2Deg : (float)rng.NextDouble() * 360f;
                slots.Add(new FacilitySlot { name = isDock ? "Dock" : cfg.facilityNames[i], position = p, rotation = yaw, isDock = isDock });
            }

            for (int i = 0; i < slots.Count; i++)
            for (int j = i + 1; j < slots.Count; j++)
            {
                if (Vector2.Distance(new Vector2(slots[i].position.x, slots[i].position.z), new Vector2(slots[j].position.x, slots[j].position.z)) < cfg.facilityMinSpacing)
                {
                    return null;
                }
            }

            plan.facilities = slots;

            // Base clearing just inland of the crash site: flatten the heightmap there
            FacilitySlot crash = slots[0];
            Vector3 toCenter = -new Vector3(crash.position.x, 0f, crash.position.z).normalized;
            float flattenRadius = cfg.campRidges
                ? (cfg.baseClearingRadius + cfg.campRidgeThickness) * Mathf.Sqrt(2f)
                : cfg.baseClearingRadius;
            plan.baseCenter = crash.position + toCenter * (flattenRadius + cfg.ClearRadiusFor(crash.name));
            plan.baseCenter.y = HeightAt(h, plan.baseCenter, tc) * tc.maxHeight;
            if (plan.baseCenter.y <= tc.seaLevel + cfg.spawnShoreMargin) return null;
            foreach (FacilitySlot slot in slots)
                if (Vector2.Distance(new Vector2(slot.position.x, slot.position.z),
                    new Vector2(plan.baseCenter.x, plan.baseCenter.z)) < flattenRadius + cfg.spawnClearRadius)
                    return null;
            Flatten(h, plan.baseCenter, flattenRadius, tc);
            TerrainNoise.LimitSlope(h, maxStepNorm, 2);
            plan.baseCenter.y = HeightAt(h, plan.baseCenter, tc) * tc.maxHeight;
            // Raising the spawn transform alone does not raise the seabed: gravity would sink the player.
            if (!TryFindSpawn(cfg, crash, h, toCenter, out plan.playerSpawn)) return null;
            plan.heights = h;

            // Props: Poisson-disk candidates over the whole island, accepted by biome density
            float bx = (float)rng.NextDouble() * 100f;
            float by = (float)rng.NextDouble() * 100f;
            var accepted = new List<Vector2>();
            var grid = new Dictionary<(int, int), List<int>>();
            float cellSize = cfg.minPropSpacing / Mathf.Sqrt(2f);
            var active = new List<Vector2> { new Vector2((float)rng.NextDouble() * tc.size - half, (float)rng.NextDouble() * tc.size - half) };
            int guard = 0;
            while (active.Count > 0 && guard++ < 200000)
            {
                int idx = rng.Next(active.Count);
                Vector2 center = active[idx];
                bool found = false;
                for (int k = 0; k < 12; k++)
                {
                    float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                    float dist = cfg.minPropSpacing * (1f + (float)rng.NextDouble());
                    Vector2 c = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                    if (Mathf.Abs(c.x) >= half - 1f || Mathf.Abs(c.y) >= half - 1f) continue;
                    if (!FarEnough(c, accepted, grid, cellSize, cfg.minPropSpacing)) continue;
                    accepted.Add(c);
                    var key = ((int)Mathf.Floor(c.x / cellSize), (int)Mathf.Floor(c.y / cellSize));
                    if (!grid.TryGetValue(key, out var list)) grid[key] = list = new List<int>();
                    list.Add(accepted.Count - 1);
                    active.Add(c);
                    found = true;
                }

                if (!found)
                {
                    active.RemoveAt(idx);
                }
            }

            float cellArea = cfg.minPropSpacing * cfg.minPropSpacing * 2f; // rough area each candidate represents
            var lastOfKind = new Dictionary<PropKind, PropVariant>();
            foreach (Vector2 c in accepted)
            {
                Vector3 p = new Vector3(c.x, 0f, c.y);
                float hn = HeightAt(h, p, tc);
                if (hn <= seaNorm) continue;
                if (SlopeAt(h, p, tc) > cfg.maxPropSlope * maxStepNorm) continue;
                float toBase = Vector2.Distance(c, new Vector2(plan.baseCenter.x, plan.baseCenter.z));
                bool inBase = cfg.campRidges
                    ? Mathf.Abs(p.x - plan.baseCenter.x) < cfg.baseClearingRadius && Mathf.Abs(p.z - plan.baseCenter.z) < cfg.baseClearingRadius
                    : toBase < cfg.baseClearingRadius;
                bool baseFringe = toBase < cfg.baseClearingRadius * 1.6f;
                bool nearFacility = false;
                foreach (FacilitySlot f in slots)
                {
                    if (Vector2.Distance(c, new Vector2(f.position.x, f.position.z)) < cfg.ClearRadiusFor(f.name)) { nearFacility = true; break; }
                }
                if (nearFacility) continue;
                if (Vector2.Distance(c, new Vector2(plan.playerSpawn.x, plan.playerSpawn.z)) < cfg.spawnClearRadius) continue;

                float heightM = hn * tc.maxHeight;
                Biome biome = BiomeAt(c, heightM, tc, cfg, bx, by);
                BiomeDensity d = Density(cfg, biome);
                PropKind? kind = PickKind(d, cellArea, rng);
                if (kind == null) continue;
                if (inBase && (kind != PropKind.Grass || rng.NextDouble() > cfg.baseGrassFraction)) continue;
                if (baseFringe && (kind == PropKind.Boulder || kind == PropKind.Tree) && rng.NextDouble() < 0.5) continue;
                PropVariant v = cfg.props.Pick(kind.Value, rng);
                if (v == null) continue;
                if (lastOfKind.TryGetValue(kind.Value, out PropVariant prev) && prev == v)
                {
                    PropVariant alt = cfg.props.Pick(kind.Value, rng); // one re-roll keeps neighbours from repeating
                    if (alt != null) v = alt;
                }
                lastOfKind[kind.Value] = v;
                if (inBase && v.solid) continue;
                if (cfg.campRidges)
                {
                    float dx = Mathf.Abs(p.x - plan.baseCenter.x), dz = Mathf.Abs(p.z - plan.baseCenter.z);
                    float footprint = v.footprintRadius * v.baseScale * (1f + v.scaleJitter);
                    float border = cfg.baseClearingRadius + cfg.campRidgeThickness + footprint;
                    if (dx < border && dz < border && (dx > cfg.baseClearingRadius || dz > cfg.baseClearingRadius))
                        continue;
                    if (Mathf.Abs(p.x - plan.baseCenter.x) < cfg.campEntranceWidth * 0.5f + footprint
                        && p.z < plan.baseCenter.z && p.z > plan.baseCenter.z - border - cfg.spawnClearRadius)
                        continue;
                }
                p.y = heightM;
                plan.props.Add(new PropPlacement
                {
                    variant = v,
                    position = p,
                    scale = PropPlacer.PickScale(v, rng),
                    tintIndex = PropPlacer.PickTintIndex(cfg.props, rng),
                    biome = biome,
                });
                plan.propCountsByKind[(int)kind.Value]++;
            }

            return plan;
        }

        private static bool TryFindSpawn(IslandConfig cfg, FacilitySlot crash, float[,] heights, Vector3 inland, out Vector3 spawn)
        {
            FacilityKit kit = cfg.facilities != null ? cfg.facilities.Find(crash.name) : null;
            Quaternion inverse = Quaternion.Euler(0f, -crash.rotation, 0f);
            float radius = cfg.ClearRadiusFor(crash.name);
            for (float distance = cfg.spawnClearRadius; distance <= radius; distance += cfg.spawnSearchStep)
            for (int i = 0; i < cfg.spawnSearchDirections; i++)
            {
                Vector3 direction = Quaternion.Euler(0f, i * 360f / cfg.spawnSearchDirections, 0f) * -inland;
                Vector3 p = crash.position + direction * distance;
                p.y = HeightAt(heights, p, cfg.terrain) * cfg.terrain.maxHeight;
                if (p.y < cfg.terrain.seaLevel + cfg.spawnShoreMargin) continue;
                Vector3 local = inverse * (p - crash.position);
                if (kit != null && kit.BlocksPoint(new Vector2(local.x, local.z), cfg.spawnClearRadius)) continue;
                spawn = p;
                return true;
            }

            spawn = default;
            return false;
        }

        public static Biome BiomeAt(Vector2 xz, float heightMeters, TerrainConfig tc, IslandConfig cfg, float ox, float oy)
        {
            if (heightMeters < tc.seaLevel + cfg.beachBand) return Biome.Beach;
            float v = Mathf.PerlinNoise(xz.x / cfg.biomeFeatureSize + ox, xz.y / cfg.biomeFeatureSize + oy);
            if (v < 0.36f) return Biome.RockField;
            if (v > 0.66f) return Biome.Clearing;
            return Biome.Jungle;
        }

        private static BiomeDensity Density(IslandConfig cfg, Biome b)
        {
            foreach (BiomeDensity d in cfg.densities)
            {
                if (d.biome == b) return d;
            }

            return default;
        }

        /// <summary>Turns per-100 m2 densities into a probability per candidate cell, picks one kind or none.</summary>
        private static PropKind? PickKind(BiomeDensity d, float cellArea, System.Random rng)
        {
            float k = cellArea / 100f;
            float pt = d.trees * k, pr = d.rocks * k, pg = d.grass * k, pb = d.bushes * k, pl = d.logs * k;
            float pbo = d.boulders * k, ps = d.stones * k, pc = d.clutter * k;
            float total = pt + pr + pg + pb + pl + pbo + ps + pc;
            float r = (float)rng.NextDouble();
            if (r >= Mathf.Min(1f, total)) return null;
            r *= total / Mathf.Min(1f, total);
            if ((r -= pt) < 0f) return PropKind.Tree;
            if ((r -= pr) < 0f) return PropKind.Rock;
            if ((r -= pg) < 0f) return PropKind.Grass;
            if ((r -= pb) < 0f) return PropKind.Bush;
            if ((r -= pl) < 0f) return PropKind.Log;
            if ((r -= pbo) < 0f) return PropKind.Boulder;
            if ((r -= ps) < 0f) return PropKind.Stone;
            return PropKind.Clutter;
        }

        private static bool FarEnough(Vector2 c, List<Vector2> pts, Dictionary<(int, int), List<int>> grid, float cellSize, float minDist)
        {
            int gx = (int)Mathf.Floor(c.x / cellSize);
            int gy = (int)Mathf.Floor(c.y / cellSize);
            for (int dx = -2; dx <= 2; dx++)
            for (int dy = -2; dy <= 2; dy++)
            {
                if (!grid.TryGetValue((gx + dx, gy + dy), out var list)) continue;
                foreach (int i in list)
                {
                    if ((pts[i] - c).sqrMagnitude < minDist * minDist) return false;
                }
            }

            return true;
        }

        private static Vector3 PushToShore(Vector3 p, float[,] h, TerrainConfig tc, float tolerance)
        {
            Vector3 dir = new Vector3(p.x, 0f, p.z).normalized;
            float seaNorm = tc.seaLevel / tc.maxHeight;
            float tolNorm = tolerance / tc.maxHeight;
            float half = tc.size * 0.5f;
            for (float r = new Vector2(p.x, p.z).magnitude; r < half - 2f; r += 0.5f)
            {
                Vector3 q = dir * r;
                float hn = HeightAt(h, q, tc);
                if (hn <= seaNorm + tolNorm)
                {
                    // step back onto the sand just above the water line
                    return dir * (r - 2.5f);
                }
            }

            return new Vector3(float.NaN, 0f, 0f);
        }

        private static void Flatten(float[,] h, Vector3 center, float radius, TerrainConfig tc)
        {
            int n = h.GetLength(0);
            float cell = tc.size / (n - 1);
            float target = HeightAt(h, center, tc);
            int cx = Mathf.RoundToInt((center.x + tc.size * 0.5f) / cell);
            int cy = Mathf.RoundToInt((center.z + tc.size * 0.5f) / cell);
            int rc = Mathf.CeilToInt((radius + 3f) / cell);
            for (int y = Mathf.Max(0, cy - rc); y <= Mathf.Min(n - 1, cy + rc); y++)
            for (int x = Mathf.Max(0, cx - rc); x <= Mathf.Min(n - 1, cx + rc); x++)
            {
                float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) * cell;
                if (d > radius + 3f) continue;
                float t = d <= radius ? 1f : 1f - (d - radius) / 3f;
                h[y, x] = Mathf.Lerp(h[y, x], target, t);
            }
        }

        public static float HeightAt(float[,] h, Vector3 world, TerrainConfig tc)
        {
            int n = h.GetLength(0);
            float u = Mathf.Clamp01((world.x + tc.size * 0.5f) / tc.size) * (n - 1);
            float v = Mathf.Clamp01((world.z + tc.size * 0.5f) / tc.size) * (n - 1);
            int x0 = Mathf.Clamp((int)u, 0, n - 2);
            int y0 = Mathf.Clamp((int)v, 0, n - 2);
            float fx = u - x0;
            float fy = v - y0;
            return Mathf.Lerp(Mathf.Lerp(h[y0, x0], h[y0, x0 + 1], fx), Mathf.Lerp(h[y0 + 1, x0], h[y0 + 1, x0 + 1], fx), fy);
        }

        private static float SlopeAt(float[,] h, Vector3 world, TerrainConfig tc)
        {
            int n = h.GetLength(0);
            int x = Mathf.Clamp(Mathf.RoundToInt((world.x + tc.size * 0.5f) / tc.size * (n - 1)), 0, n - 2);
            int y = Mathf.Clamp(Mathf.RoundToInt((world.z + tc.size * 0.5f) / tc.size * (n - 1)), 0, n - 2);
            return Mathf.Max(Mathf.Abs(h[y, x] - h[y, x + 1]), Mathf.Abs(h[y, x] - h[y + 1, x]));
        }
    }
}
