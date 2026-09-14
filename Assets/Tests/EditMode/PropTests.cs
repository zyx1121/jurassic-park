using JurassicPark.World;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class PropTests
    {
        private static PropLibrary Lib()
        {
            var lib = ScriptableObject.CreateInstance<PropLibrary>();
            var a = ScriptableObject.CreateInstance<PropVariant>(); a.name = "a"; a.kind = PropKind.Tree; a.weight = 1f;
            var b = ScriptableObject.CreateInstance<PropVariant>(); b.name = "b"; b.kind = PropKind.Tree; b.weight = 3f;
            var r = ScriptableObject.CreateInstance<PropVariant>(); r.name = "r"; r.kind = PropKind.Rock; r.weight = 1f;
            lib.variants = new[] { a, b, r };
            return lib;
        }

        [Test]
        public void PickRespectsKindAndWeights()
        {
            var lib = Lib();
            var rng = new System.Random(1);
            int a = 0, b = 0;
            for (int i = 0; i < 2000; i++)
            {
                var v = lib.Pick(PropKind.Tree, rng);
                Assert.AreEqual(PropKind.Tree, v.kind);
                if (v.name == "a") a++; else b++;
            }

            Assert.Greater(b, a * 2, "weight 3 should win about three times as often");
            Assert.IsNull(lib.Pick(PropKind.Grass, rng));
        }

        [Test]
        public void SameSeedSameVariantScaleAndTint()
        {
            var lib = Lib();
            var r1 = new System.Random(42);
            var r2 = new System.Random(42);
            for (int i = 0; i < 20; i++)
            {
                Assert.AreEqual(lib.Pick(PropKind.Tree, r1), lib.Pick(PropKind.Tree, r2));
                Assert.AreEqual(PropPlacer.PickScale(lib, r1), PropPlacer.PickScale(lib, r2));
                Assert.AreEqual(PropPlacer.PickTint(lib, r1), PropPlacer.PickTint(lib, r2));
            }
        }

        [Test]
        public void ScaleStaysInRange()
        {
            var lib = Lib();
            var rng = new System.Random(7);
            for (int i = 0; i < 500; i++)
            {
                float s = PropPlacer.PickScale(lib, rng);
                Assert.That(s, Is.InRange(lib.scaleRange.x, lib.scaleRange.y));
            }
        }
    }
}
