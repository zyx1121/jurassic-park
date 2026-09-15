using JurassicPark.Building;
using JurassicPark.Core;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    [Category("PlayerFeedback")]
    public class BuildingTests
    {
        [Test]
        public void SnapGoesToCellCenter()
        {
            Vector3 s = BuildGrid.Snap(new Vector3(3.2f, 7f, -0.4f), 1f);
            Assert.AreEqual(3.5f, s.x, 1e-5f);
            Assert.AreEqual(-0.5f, s.z, 1e-5f);
            Assert.AreEqual(7f, s.y, 1e-5f);
        }

        [Test]
        public void RotationSwapsFootprintAndCenterStaysOnGrid()
        {
            var fp = new Vector2Int(2, 1);
            Assert.AreEqual(new Vector2(2f, 1f), BuildGrid.RotatedFootprint(fp, 0, 1f));
            Assert.AreEqual(new Vector2(1f, 2f), BuildGrid.RotatedFootprint(fp, 1, 1f));
            Assert.AreEqual(new Vector2(2f, 1f), BuildGrid.RotatedFootprint(fp, 2, 1f));
            Vector3 cell = BuildGrid.Snap(new Vector3(0.2f, 0f, 0.2f), 1f); // (0.5, 0, 0.5)
            Vector3 c0 = BuildGrid.FootprintCenter(cell, fp, 0, 1f);
            Assert.AreEqual(1.0f, c0.x, 1e-5f, "a 2-wide footprint centers on a grid line");
            Assert.AreEqual(0.5f, c0.z, 1e-5f);
            Vector3 c1 = BuildGrid.FootprintCenter(cell, fp, 1, 1f);
            Assert.AreEqual(0.5f, c1.x, 1e-5f);
            Assert.AreEqual(1.0f, c1.z, 1e-5f);
        }

        [Test]
        public void OverlapDetectsTouchingButNotAdjacent()
        {
            var a = new Vector3(0f, 0f, 0f); var sa = new Vector2(2f, 1f);
            Assert.IsTrue(BuildGrid.Overlaps(a, sa, new Vector3(1f, 0f, 0f), new Vector2(2f, 1f)));
            Assert.IsFalse(BuildGrid.Overlaps(a, sa, new Vector3(2f, 0f, 0f), new Vector2(2f, 1f)), "sharing an edge is fine");
            Assert.IsFalse(BuildGrid.Overlaps(a, sa, new Vector3(0f, 0f, 1f), new Vector2(2f, 1f)));
        }

        [Test]
        public void AffordabilityAndPayment()
        {
            var cfg = ScriptableObject.CreateInstance<InventoryConfig>();
            var go = new GameObject("inv");
            var inv = go.AddComponent<ResourceInventory>();
            inv.Configure(cfg);
            var def = ScriptableObject.CreateInstance<StructureDef>();
            def.cost = new[] { new ResourceCost { kind = ResourceKind.Wood, amount = 3 }, new ResourceCost { kind = ResourceKind.Stone, amount = 1 } };
            Assert.IsFalse(BuildGrid.CanAfford(inv, def));
            inv.Add(ResourceKind.Wood, 5);
            inv.Add(ResourceKind.Stone, 1);
            Assert.IsTrue(BuildGrid.CanAfford(inv, def));
            Assert.IsTrue(BuildGrid.Pay(inv, def));
            Assert.AreEqual(2, inv.Get(ResourceKind.Wood));
            Assert.AreEqual(0, inv.Get(ResourceKind.Stone));
            Assert.IsFalse(BuildGrid.Pay(inv, def), "cannot pay twice");
            Assert.IsFalse(BuildGrid.Pay(inv, def, 0.5f), "half repair cost still needs 1 stone (rounded up)");
            inv.Add(ResourceKind.Stone, 1);
            Assert.IsTrue(BuildGrid.CanAfford(inv, def, 0.5f));
            Assert.IsTrue(BuildGrid.Pay(inv, def, 0.5f));
            Assert.AreEqual(0, inv.Get(ResourceKind.Wood), "half of 3 wood rounds up to 2");
            Object.DestroyImmediate(go);
        }
    }
}
