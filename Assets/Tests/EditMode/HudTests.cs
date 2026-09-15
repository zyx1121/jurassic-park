using JurassicPark.UI;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Tests
{
    [Category("PlayerFeedback")]
    public sealed class HudTests
    {
        [TestCase(0f, 360)]
        [TestCase(0.25f, 720)]
        [TestCase(0.5f, 1080)]
        [TestCase(0.75f, 0)]
        [TestCase(1f, 360)]
        public void ClockStartsAtDawnAndWrapsAtMidnight(float normalized, int minutes)
        {
            Assert.That(SurvivalHud.ClockMinutes(normalized), Is.EqualTo(minutes));
        }

        [TestCase(1280, 720)]
        [TestCase(1176, 716)]
        [TestCase(2940, 1790)]
        public void MinimapUsesTheSameBalancedScaleAsTheCanvas(int width, int height)
        {
            float expected = Mathf.Pow(2f,
                Mathf.Lerp(Mathf.Log(width / 1280f, 2f), Mathf.Log(height / 720f, 2f), 0.5f));
            float scale = Minimap.ReferenceScale(width, height);
            Assert.That(scale, Is.EqualTo(expected).Within(0.0001f));
            float canvasWidth = width / scale;
            Assert.That(canvasWidth - 16f - 880f, Is.GreaterThan(312f),
                "The control strip must stay clear of the bottom-left status panel.");
            Assert.That(canvasWidth * 0.5f + 250f, Is.LessThan(canvasWidth - 16f - 292f),
                "The center build prompt must stay clear of the target panel.");
        }

        [Test]
        public void DefaultPaletteHasReadableTextAndAVisibleWarning()
        {
            HudConfig config = ScriptableObject.CreateInstance<HudConfig>();
            try
            {
                Assert.That(config.text.grayscale - config.panel.grayscale, Is.GreaterThan(0.65f));
                Assert.That(config.danger.r, Is.GreaterThan(config.danger.g));
                Assert.That(config.lowHealthThreshold, Is.InRange(0.01f, 0.99f));
                Assert.That(config.bodyFontSize, Is.GreaterThanOrEqualTo(16));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [TestCase("Island")]
        [TestCase("LookTest")]
        public void GeneratedScenesPersistHudAndSelectionSettings(string name)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.OpenPreviewScene($"Assets/Scenes/{name}.unity");
            try
            {
                GameObject root = System.Array.Find(scene.GetRootGameObjects(), candidate => candidate.name == "HUD");
                Assert.IsNotNull(root);
                Assert.IsNotNull(root.GetComponent<Minimap>());
                Assert.AreEqual(0, UnityEditor.GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(root));
                foreach (Component component in new Component[] { root.GetComponent<SurvivalHud>(), root.GetComponent<WorldSelection>() })
                {
                    Assert.IsNotNull(component);
                    var serialized = new UnityEditor.SerializedObject(component);
                    Object config = serialized.FindProperty("config").objectReferenceValue;
                    Assert.IsNotNull(config, $"{component.GetType().Name} config was lost on scene save.");
                    Assert.IsTrue(UnityEditor.EditorUtility.IsPersistent(config));
                }
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
