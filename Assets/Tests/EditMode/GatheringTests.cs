using JurassicPark.Core;
using JurassicPark.World;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class GatheringTests
    {
        [Test]
        public void HitsAccumulateIntoUnitsUntilDepleted()
        {
            var stock = new ResourceStock(capacity: 2, hitsPerUnit: 3);
            Assert.AreEqual(0, stock.Hit());
            Assert.AreEqual(0, stock.Hit());
            Assert.AreEqual(1, stock.Hit());
            Assert.AreEqual(1, stock.Remaining);
            Assert.AreEqual(0, stock.Hit());
            Assert.AreEqual(0, stock.Hit());
            Assert.AreEqual(1, stock.Hit());
            Assert.IsTrue(stock.Depleted);
            Assert.AreEqual(0, stock.Hit(), "depleted nodes yield nothing");
            stock.Refill();
            Assert.AreEqual(2, stock.Remaining);
            Assert.AreEqual(0f, stock.Progress);
        }

        [Test]
        public void InventoryAddsTakesAndRefusesOverdraft()
        {
            var go = new GameObject("inv");
            var inv = go.AddComponent<ResourceInventory>();
            int events = 0;
            inv.Changed += (k, d, t) => events++;
            inv.Add(ResourceKind.Wood, 3);
            inv.Add(ResourceKind.None, 5);
            Assert.AreEqual(3, inv.Get(ResourceKind.Wood));
            Assert.IsFalse(inv.TryTake(ResourceKind.Wood, 4));
            Assert.IsTrue(inv.TryTake(ResourceKind.Wood, 2));
            Assert.AreEqual(1, inv.Get(ResourceKind.Wood));
            Assert.AreEqual(2, events);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void NodeGivesResourceToActorAndReportsDepletion()
        {
            var rules = ScriptableObject.CreateInstance<GatherRules>();
            rules.TryGet(ResourceKind.Wood, out GatherRule rule);
            var nodeGo = new GameObject("tree");
            var node = nodeGo.AddComponent<ResourceNode>();
            node.Configure(ResourceKind.Wood, 1, rule);
            var actor = new GameObject("player");
            var inv = actor.AddComponent<ResourceInventory>();
            Assert.IsTrue(node.CanInteract(actor));
            for (int i = 0; i < rule.hitsPerUnit; i++) node.Interact(actor);
            Assert.AreEqual(1, inv.Get(ResourceKind.Wood));
            Assert.IsTrue(node.Stock.Depleted);
            Assert.IsFalse(node.CanInteract(actor));
            node.Respawn();
            Assert.IsTrue(node.CanInteract(actor));
            Object.DestroyImmediate(nodeGo);
            Object.DestroyImmediate(actor);
            Object.DestroyImmediate(rules);
        }
    }
}
