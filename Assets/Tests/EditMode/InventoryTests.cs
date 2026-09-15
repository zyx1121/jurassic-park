using JurassicPark.Core;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class InventoryTests
    {
        private static ResourceInventory Make()
        {
            var cfg = ScriptableObject.CreateInstance<InventoryConfig>();
            var go = new GameObject("inv");
            var inv = go.AddComponent<ResourceInventory>();
            inv.Configure(cfg);
            return inv;
        }

        [Test]
        public void AddStopsAtTheCapAndReportsWhatFit()
        {
            var inv = Make();
            Assert.AreEqual(30, inv.Add(ResourceKind.Wood, 45));
            Assert.AreEqual(30, inv.Get(ResourceKind.Wood));
            Assert.AreEqual(0, inv.Add(ResourceKind.Wood, 1));
            Assert.AreEqual(0, inv.Space(ResourceKind.Wood));
            Object.DestroyImmediate(inv.gameObject);
        }

        [Test]
        public void BoatPartsAreUniqueAndOneAtATime()
        {
            var inv = Make();
            Assert.IsTrue(inv.TryAddBoatPart(3));
            Assert.IsFalse(inv.TryAddBoatPart(3), "same part twice");
            Assert.IsFalse(inv.TryAddBoatPart(4), "cap is one part");
            Assert.AreEqual(1, inv.Get(ResourceKind.BoatPart));
            Assert.IsTrue(inv.TryRemoveBoatPart(3));
            Assert.IsTrue(inv.TryAddBoatPart(4));
            Object.DestroyImmediate(inv.gameObject);
        }

        [Test]
        public void TakeEverythingEmptiesAndLists()
        {
            var inv = Make();
            inv.Add(ResourceKind.Wood, 5);
            inv.Add(ResourceKind.Food, 2);
            inv.TryAddBoatPart(1);
            var items = inv.TakeEverything();
            Assert.AreEqual(3, items.Count);
            Assert.AreEqual(0, inv.Get(ResourceKind.Wood));
            Assert.AreEqual(0, inv.Get(ResourceKind.BoatPart));
            Object.DestroyImmediate(inv.gameObject);
        }
    }
}
