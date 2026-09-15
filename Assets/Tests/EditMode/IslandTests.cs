using JurassicPark.World;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class IslandTests
    {
        private static IslandConfig Config()
        {
            var tc = ScriptableObject.CreateInstance<TerrainConfig>();
            tc.size = 128f; tc.heightmapResolution = 65; tc.maxHeight = 8f; tc.seaLevel = 1.2f; tc.featureSize = 40f; tc.octaves = 3;
            var lib = ScriptableObject.CreateInstance<PropLibrary>();
            var vs = new System.Collections.Generic.List<PropVariant>();
            foreach (PropKind k in System.Enum.GetValues(typeof(PropKind)))
            {
                var v = ScriptableObject.CreateInstance<PropVariant>(); v.name = k.ToString(); v.kind = k; vs.Add(v);
            }
            lib.variants = vs.ToArray();
            var cfg = ScriptableObject.CreateInstance<IslandConfig>();
            cfg.terrain = tc; cfg.props = lib;
            return cfg;
        }

        private static int Hash(IslandPlan p)
        {
            unchecked
            {
                int acc = p.props.Count * 31 + p.facilities.Count;
                foreach (var pp in p.props) acc = acc * 31 + Mathf.RoundToInt(pp.position.x * 100f) + Mathf.RoundToInt(pp.position.z * 100f) * 7 + pp.variant.GetHashCode();
                foreach (var f in p.facilities) acc = acc * 31 + Mathf.RoundToInt(f.position.x * 100f);
                return acc;
            }
        }

        [Test]
        public void SameSeedSamePlan()
        {
            var cfg = Config();
            Assert.AreEqual(Hash(IslandGenerator.Plan(cfg, 5)), Hash(IslandGenerator.Plan(cfg, 5)));
            Assert.AreNotEqual(Hash(IslandGenerator.Plan(cfg, 5)), Hash(IslandGenerator.Plan(cfg, 6)));
        }

        [Test]
        public void FacilitiesKeepSpacingAndDockTouchesShore()
        {
            var cfg = Config();
            for (int seed = 1; seed <= 5; seed++)
            {
                var plan = IslandGenerator.Plan(cfg, seed);
                Assert.AreEqual(cfg.facilityNames.Length + 1, plan.facilities.Count);
                for (int i = 0; i < plan.facilities.Count; i++)
                for (int j = i + 1; j < plan.facilities.Count; j++)
                {
                    float d = Vector2.Distance(new Vector2(plan.facilities[i].position.x, plan.facilities[i].position.z), new Vector2(plan.facilities[j].position.x, plan.facilities[j].position.z));
                    Assert.GreaterOrEqual(d, cfg.facilityMinSpacing, $"seed {seed}: {plan.facilities[i].name} vs {plan.facilities[j].name}");
                }

                var dock = plan.facilities.Find(f => f.isDock);
                float shoreHeight = IslandGenerator.HeightAt(plan.heights, dock.position, cfg.terrain) * cfg.terrain.maxHeight;
                Assert.That(shoreHeight, Is.InRange(cfg.terrain.seaLevel - 0.01f, cfg.terrain.seaLevel + cfg.dockShoreTolerance + 1.6f), $"seed {seed}: dock height {shoreHeight}");
                var crash = plan.facilities[0];
                float crashHeight = IslandGenerator.HeightAt(plan.heights, crash.position, cfg.terrain) * cfg.terrain.maxHeight;
                Assert.Less(crashHeight, cfg.terrain.seaLevel + cfg.beachBand + 1.6f, $"seed {seed}: crash site should be on the beach");
                Assert.Greater(plan.playerSpawn.y, cfg.terrain.seaLevel, "player spawn above water");
            }
        }

        [Test]
        public void PropsStayOnLandOutsideTheBaseAndFacilities()
        {
            var cfg = Config();
            var plan = IslandGenerator.Plan(cfg, 3);
            Assert.Greater(plan.props.Count, 100, "the island should not be empty");
            foreach (var p in plan.props)
            {
                Assert.Greater(p.position.y, cfg.terrain.seaLevel, "prop in the sea");
                if (p.variant.kind != PropKind.Grass && p.variant.kind != PropKind.Clutter)
                {
                    Assert.GreaterOrEqual(Vector2.Distance(new Vector2(p.position.x, p.position.z), new Vector2(plan.baseCenter.x, plan.baseCenter.z)), cfg.baseClearingRadius - 0.01f, "solid prop inside the base clearing");
                }
                foreach (var f in plan.facilities)
                {
                    Assert.GreaterOrEqual(Vector2.Distance(new Vector2(p.position.x, p.position.z), new Vector2(f.position.x, f.position.z)), cfg.facilityClearRadius - 0.01f, "prop inside a facility clear radius");
                }
            }

            for (int i = 0; i < plan.props.Count; i++)
            for (int j = i + 1; j < plan.props.Count; j++)
            {
                float d = Vector2.Distance(new Vector2(plan.props[i].position.x, plan.props[i].position.z), new Vector2(plan.props[j].position.x, plan.props[j].position.z));
                Assert.GreaterOrEqual(d, cfg.minPropSpacing - 0.01f, "props closer than the minimum spacing");
            }
        }

        [Test]
        public void BaseClearingIsFlat()
        {
            var cfg = Config();
            var plan = IslandGenerator.Plan(cfg, 9);
            float c = IslandGenerator.HeightAt(plan.heights, plan.baseCenter, cfg.terrain);
            for (int i = 0; i < 16; i++)
            {
                float a = i * Mathf.PI * 2f / 16f;
                Vector3 p = plan.baseCenter + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (cfg.baseClearingRadius * 0.8f);
                float hp = IslandGenerator.HeightAt(plan.heights, p, cfg.terrain);
                Assert.AreEqual(c, hp, 0.03f, "base clearing should be flat");
            }
        }
    }
}
