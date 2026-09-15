using JurassicPark.Core;
using JurassicPark.Dinosaurs;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class DinosaurTests
    {
        private static DinosaurStats Stats()
        {
            var s = ScriptableObject.CreateInstance<DinosaurStats>();
            s.sightRange = 12f; s.firelightSightBonus = 1.5f; s.loseRange = 22f; s.attackRange = 2f; s.fleeHealthFraction = 0.25f;
            return s;
        }

        private static DinosaurState Next(DinosaurState from, float d, bool alive = true, bool low = false, bool timer = false, bool blocked = false)
            => DinosaurBrain.NextState(from, d, Stats(), alive, low, timer, blocked);

        [Test]
        public void IdlePatrolInvestigateCycleOnTimersAndNoticeTargets()
        {
            Assert.AreEqual(DinosaurState.Idle, Next(DinosaurState.Idle, 99f));
            Assert.AreEqual(DinosaurState.Patrol, Next(DinosaurState.Idle, 99f, timer: true));
            Assert.AreEqual(DinosaurState.Idle, Next(DinosaurState.Patrol, 99f, timer: true));
            Assert.AreEqual(DinosaurState.Idle, Next(DinosaurState.Investigate, 99f, timer: true));
            Assert.AreEqual(DinosaurState.Chase, Next(DinosaurState.Patrol, 10f));
            Assert.AreEqual(DinosaurState.Chase, Next(DinosaurState.Investigate, 10f));
        }

        [Test]
        public void ChaseAttackLoseAndBlocked()
        {
            Assert.AreEqual(DinosaurState.Chase, Next(DinosaurState.Chase, 8f));
            Assert.AreEqual(DinosaurState.Attack, Next(DinosaurState.Chase, 1.5f));
            Assert.AreEqual(DinosaurState.Attack, Next(DinosaurState.Chase, 8f, blocked: true), "a fence in the way is attacked");
            Assert.AreEqual(DinosaurState.Idle, Next(DinosaurState.Chase, 30f));
            Assert.AreEqual(DinosaurState.Idle, Next(DinosaurState.Attack, 1f, alive: false));
            Assert.AreEqual(DinosaurState.Chase, Next(DinosaurState.Attack, 4f));
        }

        [Test]
        public void FleeWhenLowAndReturnAfterTimer()
        {
            Assert.AreEqual(DinosaurState.Flee, Next(DinosaurState.Attack, 1f, low: true));
            Assert.AreEqual(DinosaurState.Flee, Next(DinosaurState.Flee, 1f, low: true));
            Assert.AreEqual(DinosaurState.Idle, Next(DinosaurState.Flee, 1f, low: true, timer: true));
            Assert.AreEqual(DinosaurState.Dead, Next(DinosaurState.Dead, 1f, low: true, timer: true));
        }

        [Test]
        public void SightConeAndFirelightBonus()
        {
            Vector3 eye = Vector3.zero;
            Assert.IsTrue(Senses.CanSee(eye, Vector3.forward, new Vector3(0f, 0f, 10f), 12f, 70f));
            Assert.IsFalse(Senses.CanSee(eye, Vector3.forward, new Vector3(0f, 0f, -10f), 12f, 70f), "behind the cone");
            Assert.IsTrue(Senses.CanSee(eye, Vector3.forward, new Vector3(0f, 0f, -10f), 12f, 180f), "180 sees all around");
            Assert.IsFalse(Senses.CanSee(eye, Vector3.forward, new Vector3(0f, 0f, 15f), 12f, 70f));
            Assert.IsTrue(Senses.CanSee(eye, Vector3.forward, new Vector3(0f, 0f, 15f), 12f, 70f, rangeMultiplier: 1.5f), "firelight extends sight");
        }

        [Test]
        public void FlankSlotsSpreadAroundTheTarget()
        {
            Vector3 target = new Vector3(0f, 0f, 10f);
            Vector3 approach = Vector3.forward; // pack comes from -z
            Vector3 leader = Senses.FlankSlot(0, 3, target, approach, 4f, 110f);
            Vector3 left = Senses.FlankSlot(1, 3, target, approach, 4f, 110f);
            Vector3 right = Senses.FlankSlot(2, 3, target, approach, 4f, 110f);
            Assert.AreEqual(6f, leader.z, 1e-3f, "leader holds the direct line");
            Assert.AreEqual(4f, Vector3.Distance(left, target), 1e-3f);
            Assert.AreEqual(4f, Vector3.Distance(right, target), 1e-3f);
            Assert.Greater(Mathf.Abs(left.x), 3f, "followers go to the sides");
            Assert.AreEqual(-left.x, right.x, 1e-3f, "mirrored slots");
            Assert.Greater(left.z, leader.z, "flankers are further around than the leader");
        }

        [Test]
        public void HearingUsesTheLargerOfNoiseAndListenerRadius()
        {
            var n = new NoiseBus.Noise { position = Vector3.zero, radius = 5f };
            Assert.IsTrue(NoiseBus.Hears(new Vector3(8f, 0f, 0f), 10f, n));
            Assert.IsFalse(NoiseBus.Hears(new Vector3(12f, 0f, 0f), 10f, n));
            Assert.IsTrue(NoiseBus.Hears(new Vector3(12f, 0f, 0f), 3f, new NoiseBus.Noise { position = Vector3.zero, radius = 25f }));
        }

        [TestCase(1f, 0f, 2)]
        [TestCase(-1f, 0f, 1)]
        [TestCase(0f, 1f, 3)]
        [TestCase(0f, -1f, 0)]
        public void SheetRowFollowsFacing(float x, float z, int row)
        {
            Assert.AreEqual(row, DinosaurBrain.RowFor(new Vector2(x, z)));
        }
    }
}
