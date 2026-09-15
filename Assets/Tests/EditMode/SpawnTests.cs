using JurassicPark.Dinosaurs;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class SpawnTests
    {
        private static SpawnEntry Raptors() => new SpawnEntry { species = "Raptor", fromNight = 1, packSize = 3, basePacks = 1, packsPerNight = 0.5f, cap = 9 };

        [Test]
        public void PacksGrowWithTheNightAndStartAtFromNight()
        {
            var e = Raptors();
            Assert.AreEqual(0, SpawnTable.PacksForNight(e, 0));
            Assert.AreEqual(1, SpawnTable.PacksForNight(e, 1));
            Assert.AreEqual(1, SpawnTable.PacksForNight(e, 2));
            Assert.AreEqual(2, SpawnTable.PacksForNight(e, 3));
            var rex = new SpawnEntry { fromNight = 4, packSize = 1, basePacks = 1, packsPerNight = 0f, cap = 1 };
            Assert.AreEqual(0, SpawnTable.PacksForNight(rex, 3));
            Assert.AreEqual(1, SpawnTable.PacksForNight(rex, 4));
        }

        [Test]
        public void CapLimitsWhatSpawns()
        {
            var e = Raptors();
            Assert.AreEqual(3, SpawnTable.AllowedToSpawn(e, 1, alive: 0));
            Assert.AreEqual(6, SpawnTable.AllowedToSpawn(e, 3, alive: 0));
            Assert.AreEqual(2, SpawnTable.AllowedToSpawn(e, 3, alive: 7), "only 2 slots left under the cap");
            Assert.AreEqual(0, SpawnTable.AllowedToSpawn(e, 5, alive: 9));
        }

        [Test]
        public void SpawnPointsStayOutOfSightAndInRange()
        {
            Vector3[] players = { new Vector3(0f, 0f, 0f), new Vector3(20f, 0f, 0f) };
            Assert.IsFalse(SpawnDirector.IsValidSpawnPoint(new Vector3(30f, 0f, 0f), players, null, 40f, 70f), "too close to the second player");
            Assert.IsTrue(SpawnDirector.IsValidSpawnPoint(new Vector3(-45f, 0f, 0f), players, null, 40f, 70f));
            Assert.IsFalse(SpawnDirector.IsValidSpawnPoint(new Vector3(-100f, 0f, 0f), players, null, 40f, 70f), "too far from everyone");
            // A camera at the origin looking down -z sees points ahead of it
            var camGo = new GameObject("cam");
            var cam = camGo.AddComponent<Camera>();
            cam.transform.position = new Vector3(0f, 10f, 0f);
            cam.transform.rotation = Quaternion.Euler(30f, 180f, 0f);
            cam.farClipPlane = 200f;
            Plane[][] frustums = { GeometryUtility.CalculateFrustumPlanes(cam) };
            Assert.IsFalse(SpawnDirector.IsValidSpawnPoint(new Vector3(0f, 0f, -50f), players, frustums, 40f, 70f), "in view");
            Assert.IsTrue(SpawnDirector.IsValidSpawnPoint(new Vector3(0f, 0f, 50f), players, frustums, 40f, 70f), "behind the camera");
            Object.DestroyImmediate(camGo);
        }
    }
}
