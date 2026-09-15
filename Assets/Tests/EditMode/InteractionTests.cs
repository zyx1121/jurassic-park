using System.Collections.Generic;
using System.Reflection;
using JurassicPark.Building;
using JurassicPark.Combat;
using JurassicPark.Core;
using JurassicPark.Player;
using JurassicPark.UI;
using JurassicPark.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JurassicPark.Tests
{
    [Category("PlayerFeedback")]
    public class InteractionTests
    {
        private readonly List<Object> objects = new List<Object>();
        private PlayerController player;
        private ResourceInventory inventory;
        private readonly Vector3 origin = new Vector3(1000f, 1000f, 1000f);

        private sealed class Blocker : IWorldInputBlocker
        {
            public bool Modal;
            public bool Pointer;
            public bool BlocksWorldInput => Modal;
            public bool BlocksPointer(Vector2 _) => Pointer;
        }

        private T Keep<T>(T value) where T : Object { objects.Add(value); return value; }

        private static void Invoke(Component component, string method, params object[] args)
        {
            MethodInfo callback = component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(callback);
            callback.Invoke(component, args);
        }

        [SetUp]
        public void SetUp()
        {
            var go = Keep(new GameObject("Interaction test player"));
            go.transform.position = origin;
            inventory = go.AddComponent<ResourceInventory>();
            player = go.AddComponent<PlayerController>();
            var config = Keep(ScriptableObject.CreateInstance<PlayerMovementConfig>());
            var so = new SerializedObject(player);
            so.FindProperty("config").objectReferenceValue = config;
            so.ApplyModifiedPropertiesWithoutUndo();
            Invoke(player, "Awake");
        }

        [TearDown]
        public void TearDown()
        {
            PlayerController.All.Remove(player);
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        private ResourceNode Node(Vector3 offset, bool childCollider = false)
        {
            var go = Keep(new GameObject("Tree_Test"));
            go.transform.position = origin + offset;
            var node = go.AddComponent<ResourceNode>();
            node.Configure(ResourceKind.Wood, 20, new GatherRule { hitsPerUnit = 1 });
            GameObject colliderGo = childCollider ? new GameObject("Trunk") : go;
            if (childCollider) colliderGo.transform.SetParent(go.transform, false);
            var collider = colliderGo.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.3f, 2f, 0.3f);
            collider.center = Vector3.up;
            Physics.SyncTransforms();
            return node;
        }

        [Test]
        public void ExplicitTargetWinsOverNearest()
        {
            ResourceNode near = Node(Vector3.left * 0.5f);
            ResourceNode selected = Node(Vector3.right);
            player.InteractionTarget = selected;
            Assert.AreSame(selected, player.TryInteract());
            Assert.AreEqual(19, selected.Stock.Remaining);
            Assert.AreEqual(20, near.Stock.Remaining);
            Assert.AreEqual(1, inventory.Get(ResourceKind.Wood));
        }

        [Test]
        public void FarExplicitTargetDoesNotGatherDifferentNearbyNode()
        {
            ResourceNode near = Node(Vector3.left * 0.5f);
            player.InteractionTarget = Node(Vector3.right * 4f);
            Assert.IsNull(player.TryInteract());
            Assert.AreEqual(20, near.Stock.Remaining);
            player.InteractionTarget = null;
            Assert.AreSame(near, player.TryInteract());
        }

        [Test]
        public void InputCallbackWaitsForCurrentFrameSelection()
        {
            ResourceNode near = Node(Vector3.left * 0.5f);
            Invoke(player, "OnInteract", default(InputAction.CallbackContext));
            Assert.AreEqual(20, near.Stock.Remaining);
            player.InteractionTarget = Node(Vector3.right * 4f);
            Invoke(player, "Update");
            Assert.AreEqual(20, near.Stock.Remaining);
            Assert.AreEqual(0, inventory.Get(ResourceKind.Wood));
        }

        [Test]
        public void SuppressedInteractionDoesNotUseNearestFallback()
        {
            ResourceNode near = Node(Vector3.left);
            player.InteractionSuppressed = true;
            Assert.IsNull(player.TryInteract());
            Invoke(player, "OnInteract", default(InputAction.CallbackContext));
            player.InteractionSuppressed = false;
            Invoke(player, "Update");
            Assert.AreEqual(20, near.Stock.Remaining, "a build-mode keypress cannot run after build mode closes");
        }

        [Test]
        public void CanopyTriggerDoesNotExtendGatheringRange()
        {
            ResourceNode node = Node(Vector3.right * 5f);
            var canopy = node.gameObject.AddComponent<SphereCollider>();
            canopy.isTrigger = true;
            canopy.radius = 6f;
            Physics.SyncTransforms();
            Assert.Greater(player.InteractionDistance(node), 4f);
            Assert.IsNull(player.FindNearestInteractable());
        }

        [Test]
        public void ChildColliderResolvesParentInteraction()
        {
            ResourceNode node = Node(Vector3.right, true);
            Assert.AreSame(node, player.FindNearestInteractable());
            Assert.AreSame(node, WorldSelection.ResolveSelectable(node.GetComponentInChildren<Collider>()));
        }

        [Test]
        public void SolidWallBlocksInteractionButTriggersDoNot()
        {
            ResourceNode node = Node(Vector3.right * 1.2f);
            var wall = Keep(new GameObject("Wall"));
            wall.transform.position = origin + Vector3.right * 0.6f + Vector3.up;
            var collider = wall.AddComponent<BoxCollider>();
            collider.size = new Vector3(0.2f, 3f, 3f);
            Physics.SyncTransforms();
            Assert.IsFalse(player.HasClearInteractionPath(node));
            Assert.IsNull(player.TryInteract());
            collider.isTrigger = true;
            Physics.SyncTransforms();
            Assert.IsTrue(player.CanInteractWith(node));
        }

        [Test]
        public void UnusableExplicitTargetDoesNotFallBack()
        {
            ResourceNode near = Node(Vector3.left);
            ResourceNode selected = Node(Vector3.right);
            while (!selected.Stock.Depleted) selected.Stock.Hit();
            player.InteractionTarget = selected;
            Assert.IsNull(player.TryInteract());
            Assert.AreEqual(20, near.Stock.Remaining);
        }

        [Test]
        public void ModalBlocksMovementAndInteractionButPanelDoesNot()
        {
            ResourceNode node = Node(Vector3.right);
            var blocker = new Blocker { Modal = true, Pointer = true };
            WorldInputBlockers.Register(blocker);
            try
            {
                player.Step(Vector2.up, 0.1f);
                Assert.AreEqual(0f, player.Velocity.x);
                Assert.AreEqual(0f, player.Velocity.z);
                Assert.IsNull(player.TryInteract());
                blocker.Modal = false;
                player.Step(Vector2.up, 0.1f);
                Assert.IsTrue(player.IsMoving);
                Assert.AreSame(node, player.TryInteract());
            }
            finally { WorldInputBlockers.Unregister(blocker); }
        }

        [Test]
        public void PanelsBlockAttackBeforeActionCallbackAndUnregisterRestoresIt()
        {
            var blocker = new Blocker { Pointer = true };
            int attacks = 0;
            player.AttackPressed += () => attacks++;
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            WorldInputBlockers.Register(blocker);
            WorldInputBlockers.Register(blocker);
            try
            {
                Invoke(player, "OnAttack", default(InputAction.CallbackContext));
                Assert.AreEqual(0, attacks);
                WorldInputBlockers.Unregister(blocker);
                Invoke(player, "OnAttack", default(InputAction.CallbackContext));
                Assert.AreEqual(1, attacks, "one unregister removes duplicate registrations");
            }
            finally
            {
                WorldInputBlockers.Unregister(blocker);
                InputSystem.RemoveDevice(mouse);
            }
        }

        [Test]
        public void PickerSelectsFrontObjectAndRespectsSolidOcclusion()
        {
            ResourceNode node = Node(Vector3.forward * 3f, true);
            var selectionGo = Keep(new GameObject("Selection"));
            WorldSelection selection = selectionGo.AddComponent<WorldSelection>();
            selection.Configure(Keep(ScriptableObject.CreateInstance<SelectionConfig>()));
            Ray ray = new Ray(origin + Vector3.up, Vector3.forward);
            Assert.AreSame(node, selection.PickTarget(ray, out _));
            var wall = Keep(new GameObject("Solid occluder"));
            wall.transform.position = origin + Vector3.forward + Vector3.up;
            wall.AddComponent<BoxCollider>();
            Physics.SyncTransforms();
            Assert.IsNull(selection.PickTarget(ray, out _));
        }

        [Test]
        public void PickerRejectsCanopyOutsideBillboardRectangle()
        {
            ResourceNode node = Node(Vector3.forward * 3f);
            var canopy = node.gameObject.AddComponent<SphereCollider>();
            canopy.isTrigger = true;
            canopy.radius = 2f;
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(node.transform, false);
            quad.transform.localPosition = Vector3.up;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            var go = Keep(new GameObject("Selection"));
            var selection = go.AddComponent<WorldSelection>();
            selection.Configure(Keep(ScriptableObject.CreateInstance<SelectionConfig>()));
            Physics.SyncTransforms();
            Assert.AreSame(node, selection.PickTarget(new Ray(origin + Vector3.up, Vector3.forward), out _));
            Assert.IsNull(selection.PickTarget(new Ray(origin + Vector3.up + Vector3.right, Vector3.forward), out _));
        }

        [Test]
        public void SelectionReportsResourceStateAndReadableName()
        {
            ResourceNode node = Node(Vector3.right);
            var go = Keep(new GameObject("Selection"));
            var selection = go.AddComponent<WorldSelection>();
            selection.SelectTarget(node);
            Assert.IsTrue(selection.HasSelection);
            Assert.AreSame(node, selection.Target);
            Assert.AreEqual("Tree Test", selection.TargetName);
            StringAssert.Contains("20 Wood remaining", selection.TargetDetails);
            selection.ClearSelection();
            Assert.IsFalse(selection.HasSelection);
        }

        [Test]
        public void DuplicateBoatPartIsNotPresentedAsCollectable()
        {
            var cfg = Keep(ScriptableObject.CreateInstance<InventoryConfig>());
            cfg.caps = new[] { new ResourceCap { kind = ResourceKind.BoatPart, max = 2 } };
            inventory.Configure(cfg);
            Assert.IsTrue(inventory.TryAddBoatPart(3));
            var go = Keep(new GameObject("Boat part"));
            var pickup = go.AddComponent<Pickup>();
            pickup.Configure(ResourceKind.BoatPart, 1, 3);
            Assert.Greater(inventory.Space(ResourceKind.BoatPart), 0);
            Assert.IsFalse(pickup.CanInteract(player.gameObject));
            pickup.Configure(ResourceKind.BoatPart, 1, 4);
            Assert.IsTrue(pickup.CanInteract(player.gameObject));
        }

        private Structure Gate(bool gate)
        {
            var lib = Keep(ScriptableObject.CreateInstance<StructureLibrary>());
            var def = Keep(ScriptableObject.CreateInstance<StructureDef>());
            def.isGate = gate;
            def.solid = true;
            def.kind = gate ? StructureKind.Gate : StructureKind.Wall;
            def.health = Keep(ScriptableObject.CreateInstance<HealthConfig>());
            def.health.maxHealth = 100f;
            def.cost = new[] { new ResourceCost { kind = ResourceKind.Wood, amount = 4 } };
            def.repairCostFraction = 0.5f;
            Structure structure = StructureFactory.Place(lib, def, origin + Vector3.right, 0);
            Keep(structure.gameObject);
            Invoke(structure, "Awake");
            Physics.SyncTransforms();
            return structure;
        }

        [Test]
        public void OpenGateKeepsInteractionTriggerAndCanClose()
        {
            Structure gate = Gate(true);
            Assert.IsTrue(gate.CanInteract(player.gameObject));
            gate.Interact(player.gameObject);
            Assert.IsTrue(gate.IsOpen);
            Assert.IsFalse(gate.GetComponent<BoxCollider>().enabled);
            BoxCollider handle = System.Array.Find(gate.GetComponents<BoxCollider>(), c => c.isTrigger);
            Assert.IsTrue(handle.enabled && handle.isTrigger);
            Assert.AreEqual(gate.GetComponent<BoxCollider>().size, handle.size, "targeting must not extend the footprint");
            Assert.AreSame(gate, WorldSelection.ResolveSelectable(handle));
            Assert.IsTrue(player.CanInteractWith(gate));
            player.InteractionTarget = gate;
            player.TryInteract();
            Assert.IsFalse(gate.IsOpen);
        }

        [Test]
        public void RepairPromptRequiresAffordableResources()
        {
            Structure wall = Gate(false);
            wall.Health.TakeDamage(new DamageInfo(10f, DamageType.Melee, null, Vector3.zero));
            Assert.IsFalse(wall.CanInteract(player.gameObject));
            inventory.Add(ResourceKind.Wood, 2);
            Assert.IsTrue(wall.CanInteract(player.gameObject));
            wall.Interact(player.gameObject);
            Assert.AreEqual(wall.Health.Max, wall.Health.Current);
            Assert.AreEqual(0, inventory.Get(ResourceKind.Wood));
        }
    }
}
