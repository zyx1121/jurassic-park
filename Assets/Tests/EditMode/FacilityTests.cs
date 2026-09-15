using JurassicPark.World;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class FacilityTests
    {
        private readonly System.Collections.Generic.List<Object> objects = new System.Collections.Generic.List<Object>();

        private T Track<T>(T value) where T : Object
        {
            objects.Add(value);
            return value;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--) Object.DestroyImmediate(objects[i]);
            objects.Clear();
        }

        private FacilityKit Kit()
        {
            var kit = Track(ScriptableObject.CreateInstance<FacilityKit>());
            kit.facilityName = "Test";
            kit.clearRadius = 9f;
            var tex = Track(new Texture2D(64, 64));
            var material = Track(new Material(Shader.Find("Universal Render Pipeline/Lit")));
            kit.pieces = new[]
            {
                new FacilityPiece { sprite = tex, material = material, offset = Vector2.zero, widthMeters = 6f, cellPixels = 64, opaquePixels = 32, solid = true, colliders = new[] { new FacilityCollider(new Vector3(6f, 2f, 2f)) } },
                new FacilityPiece { sprite = tex, material = material, offset = new Vector2(3f, -4f), widthMeters = 2f, cellPixels = 64, opaquePixels = 64, solid = false },
            };
            return kit;
        }

        [Test]
        public void QuadSizeScalesOpaqueWidthToMeters()
        {
            FacilityKit kit = Kit();
            Assert.AreEqual(12f, kit.pieces[0].QuadSize, 0.001f);
            Assert.AreEqual(2f, kit.pieces[1].QuadSize, 0.001f);
        }

        [Test]
        public void PlacerBuildsPiecesWithCollidersOnlyWhenSolid()
        {
            var slot = Track(new GameObject("Slot"));
            slot.transform.position = new Vector3(10f, 2f, 20f);
            slot.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            int placed = FacilityPlacer.Place(Kit(), slot.transform);
            Assert.AreEqual(2, placed);
            Assert.AreEqual(1, slot.GetComponentsInChildren<BoxCollider>().Length);
            // Layout north (+z) rotated 90 degrees around Y points east: (3, -4) -> (-4, -3) relative to the slot
            Vector3 second = slot.transform.GetChild(1).position;
            Assert.AreEqual(10f - 4f, second.x, 0.01f);
            Assert.AreEqual(20f - 3f, second.z, 0.01f);
        }

        [Test]
        public void ConfigUsesKitClearRadiusWhenPresent()
        {
            var lib = Track(ScriptableObject.CreateInstance<FacilityLibrary>());
            lib.kits = new[] { Kit() };
            var cfg = Track(ScriptableObject.CreateInstance<IslandConfig>());
            cfg.facilities = lib;
            cfg.facilityClearRadius = 7f;
            Assert.AreEqual(9f, cfg.ClearRadiusFor("Test"));
            Assert.AreEqual(7f, cfg.ClearRadiusFor("Nope"));
        }

        [Test]
        public void PlacerSamplesEachPieceAtItsRotatedWorldPosition()
        {
            var slot = Track(new GameObject("Slot"));
            slot.transform.position = new Vector3(10f, 2f, 20f);
            slot.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            FacilityPlacer.Place(Kit(), slot.transform, p => p.x * 0.1f + p.z * 0.2f);
            foreach (Transform piece in slot.transform)
            {
                Assert.AreEqual(piece.position.x * 0.1f + piece.position.z * 0.2f, piece.position.y, 0.001f);
            }
        }

        [Test]
        public void SplitCollidersKeepDoorwayOpenIncludingAfterRotation()
        {
            FacilityKit kit = Kit();
            FacilityPiece piece = kit.pieces[0];
            piece.colliders = new[]
            {
                new FacilityCollider(new Vector3(2f, 3f, 2f), new Vector3(-2f, 0f, 0f)),
                new FacilityCollider(new Vector3(2f, 3f, 2f), new Vector3(2f, 0f, 0f)),
            };
            piece.yaw = 90f;
            kit.pieces = new[] { piece };
            Assert.IsFalse(kit.BlocksPoint(Vector2.zero, 0.35f), "doorway");
            Assert.IsTrue(kit.BlocksPoint(new Vector2(0f, 2f), 0.35f), "rotated wall");
            var slot = Track(new GameObject("Slot"));
            FacilityPlacer.Place(kit, slot.transform);
            Assert.AreEqual(2, slot.GetComponentsInChildren<BoxCollider>().Length);
        }

        [TestCase("CrashSite")]
        [TestCase("VisitorCenter")]
        [TestCase("PowerStation")]
        [TestCase("Paddock")]
        [TestCase("Lookout")]
        public void AuthoredBoatPartHasPlayerClearance(string name)
        {
            FacilityKit kit = UnityEditor.AssetDatabase.LoadAssetAtPath<FacilityKit>($"Assets/Data/Facilities/{name}.asset");
            Assert.IsNotNull(kit, "Run build_facility_library before testing.");
            Assert.AreEqual(4, kit.pieces.Length);
            Assert.IsFalse(kit.BlocksPoint(kit.pickupOffset, 0.5f), $"{name} pickup trapped in collision");
        }
    }
}
