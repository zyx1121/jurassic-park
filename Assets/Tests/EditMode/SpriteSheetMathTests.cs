using JurassicPark.Core;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class SpriteSheetMathTests
    {
        [Test]
        public void TopLeftCellStartsAtTopOfTexture()
        {
            Vector4 st = SpriteSheetMath.CellScaleOffset(0, 0, 8, 4);
            Assert.AreEqual(0.125f, st.x, 1e-5f);
            Assert.AreEqual(0.25f, st.y, 1e-5f);
            Assert.AreEqual(0f, st.z, 1e-5f);
            Assert.AreEqual(0.75f, st.w, 1e-5f);
        }

        [Test]
        public void BottomRightCellEndsAtTextureCorner()
        {
            Vector4 st = SpriteSheetMath.CellScaleOffset(7, 3, 8, 4);
            Assert.AreEqual(0.875f, st.z, 1e-5f);
            Assert.AreEqual(0f, st.w, 1e-5f);
        }

        [TestCase(0f, 0)]
        [TestCase(0.49f, 3)]
        [TestCase(1.0f, 0)]
        [TestCase(1.3f, 2)]
        public void LoopingFrameWraps(float t, int expected)
        {
            Assert.AreEqual(expected, SpriteSheetMath.FrameAt(t, 8f, 8, loop: true));
        }

        [Test]
        public void NonLoopingFrameClampsToLast()
        {
            Assert.AreEqual(3, SpriteSheetMath.FrameAt(10f, 8f, 4, loop: false));
        }
    }
}
