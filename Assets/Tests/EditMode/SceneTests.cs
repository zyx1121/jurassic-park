using JurassicPark.Scene;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    public class SceneTests
    {
        [Test]
        public void GeneratedPostProcessingComponentsPersistWithTheProfile()
        {
            Object profile = UnityEditor.AssetDatabase.LoadAssetAtPath<Object>("Assets/Settings/LookTestProfile.asset");
            Assert.IsNotNull(profile);
            var serialized = new UnityEditor.SerializedObject(profile);
            var components = serialized.FindProperty("components");
            Assert.GreaterOrEqual(components.arraySize, 5);
            for (int i = 0; i < components.arraySize; i++)
            {
                Object component = components.GetArrayElementAtIndex(i).objectReferenceValue;
                Assert.IsNotNull(component, $"Volume component {i} was not saved.");
                Assert.IsTrue(UnityEditor.EditorUtility.IsPersistent(component), component.name);
                Assert.AreEqual("Assets/Settings/LookTestProfile.asset", UnityEditor.AssetDatabase.GetAssetPath(component));
                Assert.IsTrue(UnityEditor.AssetDatabase.TryGetGUIDAndLocalFileIdentifier(component, out string guid, out long fileId));
                Assert.AreEqual(UnityEditor.AssetDatabase.AssetPathToGUID("Assets/Settings/LookTestProfile.asset"), guid);
                Assert.AreNotEqual(0, fileId);
            }
        }

        [Test]
        public void SoftClampLeavesInteriorUntouched()
        {
            Assert.AreEqual(3f, FollowCamera.SoftClamp(3f, -10f, 10f, 4f), 1e-5f);
        }

        [TestCase(-20f, -10f)]
        [TestCase(20f, 10f)]
        public void SoftClampNeverPassesTheBorder(float v, float expected)
        {
            Assert.AreEqual(expected, FollowCamera.SoftClamp(v, -10f, 10f, 4f), 1e-5f);
        }

        [Test]
        public void SoftClampIsMonotonicNearTheEdge()
        {
            float prev = float.NegativeInfinity;
            for (float v = 4f; v <= 14f; v += 0.25f)
            {
                float c = FollowCamera.SoftClamp(v, -10f, 10f, 4f);
                Assert.GreaterOrEqual(c, prev);
                prev = c;
            }
        }

        [Test]
        public void PhaseBoundariesFollowConfig()
        {
            var c = ScriptableObject.CreateInstance<DayNightConfig>();
            c.dayStart = 0.1f; c.duskStart = 0.55f; c.nightStart = 0.65f;
            Assert.AreEqual(DayPhase.Dawn, DayNightCycle.PhaseAt(0.05f, c));
            Assert.AreEqual(DayPhase.Day, DayNightCycle.PhaseAt(0.3f, c));
            Assert.AreEqual(DayPhase.Dusk, DayNightCycle.PhaseAt(0.6f, c));
            Assert.AreEqual(DayPhase.Night, DayNightCycle.PhaseAt(0.9f, c));
            Assert.AreEqual(DayPhase.Dawn, DayNightCycle.PhaseAt(1.02f, c));
            Object.DestroyImmediate(c);
        }

        [Test]
        public void AdvanceWrapsAndCountsDays()
        {
            var c = ScriptableObject.CreateInstance<DayNightConfig>();
            var go = new GameObject("cycle");
            var cycle = go.AddComponent<DayNightCycle>();
            var so = new UnityEditor.SerializedObject(cycle);
            so.FindProperty("config").objectReferenceValue = c;
            so.FindProperty("startTime").floatValue = 0.9f;
            so.ApplyModifiedPropertiesWithoutUndo();
            cycle.SetTime(0.9f);
            int days = 0;
            cycle.NewDay += _ => days++;
            cycle.Advance(0.2f);
            Assert.AreEqual(1, days);
            Assert.AreEqual(0.1f, cycle.NormalizedTime, 1e-4f);
            Assert.AreEqual(2, cycle.DayNumber);
            Object.DestroyImmediate(go);
            Object.DestroyImmediate(c);
        }
    }
}
