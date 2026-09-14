using JurassicPark.Combat;
using JurassicPark.Dinosaurs;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class CombatTests
    {
        private static Health MakeHealth(float max, float armor = 0f, float invuln = 0f, params DamageType[] immune)
        {
            var cfg = ScriptableObject.CreateInstance<HealthConfig>();
            cfg.maxHealth = max; cfg.armor = armor; cfg.invulnerabilityAfterHit = invuln; cfg.immuneTo = immune;
            var go = new GameObject("h");
            var h = go.AddComponent<Health>();
            h.Configure(cfg);
            return h;
        }

        private static DamageInfo Hit(float amount, DamageType type = DamageType.Melee) => new DamageInfo(amount, type, null, Vector3.zero);

        [Test]
        public void DamageReducesAndClampsAtZero()
        {
            var h = MakeHealth(50f);
            Assert.AreEqual(20f, h.TakeDamage(Hit(20f)));
            Assert.AreEqual(30f, h.Current);
            h.TakeDamage(Hit(100f));
            Assert.AreEqual(0f, h.Current);
            Assert.IsFalse(h.IsAlive);
            Assert.AreEqual(0f, h.TakeDamage(Hit(5f)), "dead things take no more damage");
            Object.DestroyImmediate(h.gameObject);
        }

        [Test]
        public void DiedFiresOnceWithTheKillingBlow()
        {
            var h = MakeHealth(10f);
            int died = 0;
            h.Died += _ => died++;
            h.TakeDamage(Hit(4f));
            h.TakeDamage(Hit(8f));
            h.TakeDamage(Hit(8f));
            Assert.AreEqual(1, died);
            Object.DestroyImmediate(h.gameObject);
        }

        [Test]
        public void ArmorAndImmunityReduceDamage()
        {
            var h = MakeHealth(100f, armor: 5f, invuln: 0f, DamageType.Bite);
            Assert.AreEqual(0f, h.TakeDamage(Hit(50f, DamageType.Bite)));
            Assert.AreEqual(15f, h.TakeDamage(Hit(20f, DamageType.Melee)));
            Assert.AreEqual(0f, h.TakeDamage(Hit(3f, DamageType.Melee)), "armor absorbs small hits");
            Object.DestroyImmediate(h.gameObject);
        }

        [Test]
        public void InvulnerabilityWindowIgnoresFollowUpHits()
        {
            var h = MakeHealth(100f, armor: 0f, invuln: 0.5f);
            float now = 10f;
            h.Clock = () => now;
            Assert.AreEqual(10f, h.TakeDamage(Hit(10f)));
            now = 10.2f;
            Assert.AreEqual(0f, h.TakeDamage(Hit(10f)));
            now = 10.6f;
            Assert.AreEqual(10f, h.TakeDamage(Hit(10f)));
            Object.DestroyImmediate(h.gameObject);
        }

        [Test]
        public void HealNeverExceedsMax()
        {
            var h = MakeHealth(40f);
            h.TakeDamage(Hit(30f));
            Assert.AreEqual(30f, h.Heal(100f));
            Assert.AreEqual(40f, h.Current);
            Object.DestroyImmediate(h.gameObject);
        }

        private static DinosaurStats Stats()
        {
            var s = ScriptableObject.CreateInstance<DinosaurStats>();
            s.sightRange = 12f; s.loseRange = 20f; s.attackRange = 2f;
            return s;
        }

        [TestCase(RaptorState.Idle, 30f, RaptorState.Idle)]
        [TestCase(RaptorState.Idle, 10f, RaptorState.Chase)]
        [TestCase(RaptorState.Chase, 25f, RaptorState.Idle)]
        [TestCase(RaptorState.Chase, 5f, RaptorState.Chase)]
        [TestCase(RaptorState.Chase, 1.5f, RaptorState.Attack)]
        [TestCase(RaptorState.Attack, 2.4f, RaptorState.Attack)]
        [TestCase(RaptorState.Attack, 4f, RaptorState.Chase)]
        public void RaptorTransitions(RaptorState from, float dist, RaptorState expected)
        {
            var s = Stats();
            Assert.AreEqual(expected, RaptorBrain.NextState(from, dist, s, targetAlive: true));
            Object.DestroyImmediate(s);
        }

        [Test]
        public void DeadTargetSendsRaptorIdleAndDeadStaysDead()
        {
            var s = Stats();
            Assert.AreEqual(RaptorState.Idle, RaptorBrain.NextState(RaptorState.Attack, 1f, s, targetAlive: false));
            Assert.AreEqual(RaptorState.Dead, RaptorBrain.NextState(RaptorState.Dead, 1f, s, targetAlive: true));
            Object.DestroyImmediate(s);
        }

        [TestCase(1f, 0f, 2)]
        [TestCase(-1f, 0f, 1)]
        [TestCase(0f, 1f, 3)]
        [TestCase(0f, -1f, 0)]
        public void SheetRowFollowsFacing(float x, float z, int row)
        {
            Assert.AreEqual(row, RaptorBrain.RowFor(new Vector2(x, z)));
        }
    }
}
