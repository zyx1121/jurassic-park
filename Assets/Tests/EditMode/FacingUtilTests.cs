using JurassicPark.Player;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class FacingUtilTests
    {
        [Test]
        public void ZeroInputKeepsCurrentFacing()
        {
            Assert.AreEqual(Facing.Up, FacingUtil.FromDirection(Vector2.zero, Facing.Up));
        }

        [TestCase(1f, 0f, Facing.Right)]
        [TestCase(-1f, 0f, Facing.Left)]
        [TestCase(0f, 1f, Facing.Up)]
        [TestCase(0f, -1f, Facing.Down)]
        [TestCase(0.9f, 0.3f, Facing.Right)]
        [TestCase(-0.2f, -0.8f, Facing.Down)]
        public void CardinalAndDiagonalInputsResolveToDominantAxis(float x, float y, Facing expected)
        {
            Assert.AreEqual(expected, FacingUtil.FromDirection(new Vector2(x, y), Facing.Down));
        }

        [Test]
        public void ExactDiagonalPrefersHorizontal()
        {
            Assert.AreEqual(Facing.Left, FacingUtil.FromDirection(new Vector2(-1f, 1f), Facing.Down));
        }

        [Test]
        public void ToVectorRoundTrips()
        {
            foreach (Facing f in System.Enum.GetValues(typeof(Facing)))
            {
                Assert.AreEqual(f, FacingUtil.FromDirection(FacingUtil.ToVector(f), Facing.Down));
            }
        }
    }
}
