using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JurassicPark.Building;
using JurassicPark.Core;
using JurassicPark.Net;
using JurassicPark.Player;
using JurassicPark.UI;
using JurassicPark.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

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
                    if (config is HudConfig hud)
                    {
                        Assert.IsTrue(hud.HasFonts, "Run build_hud_assets before rebuilding the scenes.");
                        Assert.IsTrue(EditorUtility.IsPersistent(hud.bodyFont));
                        Assert.IsTrue(EditorUtility.IsPersistent(hud.emphasisFont));
                        Assert.IsTrue(EditorUtility.IsPersistent(hud.headingFont));
                    }
                }
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Category("PlayerFeedback")]
        public sealed class SurvivalInterfaceTests
        {
            private readonly List<Object> owned = new List<Object>();
            private SurvivalHud hud;
            private HudConfig config;
            private EventSystem events, previousEvents;
            private float previousScale;

            private T Keep<T>(T value) where T : Object { owned.Add(value); return value; }

            private static void Invoke(object target, string method, params object[] args)
            {
                MethodInfo callback = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(callback, method);
                callback.Invoke(target, args);
            }

            private static void SetField(Object target, string property, Object value)
            {
                var serialized = new SerializedObject(target);
                serialized.FindProperty(property).objectReferenceValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            private T Named<T>(string name) where T : Component =>
                hud.GetComponentsInChildren<T>(true).Single(component => component.name == name);

            [SetUp]
            public void SetUp()
            {
                previousScale = Time.timeScale;
                previousEvents = EventSystem.current;
                events = Keep(new GameObject("HUD test events")).AddComponent<EventSystem>();
                Invoke(events, "OnEnable");
                EventSystem.current = events;
                config = Keep(ScriptableObject.CreateInstance<HudConfig>());
                config.bodyFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/SourceSans3-Regular.ttf");
                config.emphasisFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/SourceSans3-Semibold.ttf");
                config.headingFont = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/SourceSerif4-Semibold.ttf");
                Assert.IsTrue(config.HasFonts, "Import the bundled licensed fonts before running UI regressions.");
                hud = Keep(new GameObject("HUD test root")).AddComponent<SurvivalHud>();
                SetField(hud, "config", config);
                Invoke(hud, "BuildCanvas");
            }

            [TearDown]
            public void TearDown()
            {
                if (hud != null) Invoke(hud, "OnDisable");
                if (events != null) Invoke(events, "OnDisable");
                for (int i = owned.Count - 1; i >= 0; i--)
                    if (owned[i] != null)
                    {
                        if (owned[i] is GameObject go && go.TryGetComponent(out PlayerController player))
                            PlayerController.All.Remove(player);
                        Object.DestroyImmediate(owned[i]);
                    }
                owned.Clear();
                if (previousEvents != null) EventSystem.current = previousEvents;
                Time.timeScale = previousScale;
            }

            private PlayerController BindPlayer()
            {
                GameObject go = Keep(new GameObject("HUD test survivor"));
                go.AddComponent<ResourceInventory>();
                PlayerController player = go.AddComponent<PlayerController>();
                Invoke(hud, "Bind", player);
                Invoke(hud, "RefreshVisibility");
                return player;
            }

            [Test]
            public void StartScreenHidesNetworkFormAndKeepsIslandClockAtArrival()
            {
                var lobby = Keep(new GameObject("Test lobby")).AddComponent<NetLobby>();
                typeof(SurvivalHud).GetField("lobby", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(hud, lobby);
                Time.timeScale = .8f;
                Invoke(hud, "RefreshVisibility");
                Assert.AreEqual(0f, Time.timeScale);
                Assert.IsFalse(Named<InputField>("LAN address").gameObject.activeInHierarchy);
                Named<Button>("Multiplayer (LAN)").onClick.Invoke();
                Assert.IsTrue(Named<InputField>("LAN address").gameObject.activeInHierarchy);
                hud.Back();
                Assert.IsFalse(Named<InputField>("LAN address").gameObject.activeInHierarchy);
                Assert.AreEqual(0f, Time.timeScale);
                typeof(NetLobby).GetProperty("Started").SetValue(lobby, true);
                Invoke(hud, "RefreshVisibility");
                Assert.AreEqual(.8f, Time.timeScale);
            }

            [Test]
            public void PassiveWorldPromptNeverStealsTheClickItDescribes()
            {
                BindPlayer();
                Canvas.ForceUpdateCanvases();
                RectTransform prompt = Named<RectTransform>("Interaction");
                Vector2 point = RectTransformUtility.WorldToScreenPoint(null, prompt.TransformPoint(prompt.rect.center));
                Assert.IsFalse(hud.BlocksPointer(point));
            }

            [Test]
            public void ButtonsHavePointerFeedbackWithoutGameplaySubmitOrNavigation()
            {
                Assert.IsNotNull(hud.GetComponentInChildren<GraphicRaycaster>());
                InputSystemUIInputModule module = events.GetComponent<InputSystemUIInputModule>();
                Assert.IsNotNull(module);
                Assert.IsNotNull(module.point);
                Assert.IsNotNull(module.leftClick);
                Assert.IsNull(module.move);
                Assert.IsNull(module.submit);
                Assert.IsNull(module.cancel);
                Assert.IsFalse(events.sendNavigationEvents);
                foreach (Button button in hud.GetComponentsInChildren<Button>(true))
                {
                    Assert.That(button.navigation.mode, Is.EqualTo(Navigation.Mode.None));
                    Assert.AreNotEqual(button.colors.normalColor, button.colors.highlightedColor);
                    Assert.AreNotEqual(button.colors.normalColor, button.colors.pressedColor);
                    Assert.AreNotEqual(button.colors.normalColor, button.colors.disabledColor);
                    Assert.IsTrue(button.targetGraphic.raycastTarget);
                }
                foreach (Text label in hud.GetComponentsInChildren<Text>(true))
                {
                    Assert.IsFalse(label.raycastTarget);
                    Assert.That(label.font, Is.EqualTo(config.bodyFont).Or.EqualTo(config.emphasisFont).Or.EqualTo(config.headingFont));
                }
                foreach (Image image in hud.GetComponentsInChildren<Image>(true))
                    if (image.GetComponent<Selectable>() == null && image.GetComponent<ScrollRect>() == null)
                        Assert.IsFalse(image.raycastTarget, image.name);
            }

            [Test]
            public void ResumeButtonRestoresOfflineSpeedAndKeepsClosureFrameBlocked()
            {
                BindPlayer();
                Time.timeScale = .75f;
                Named<Button>("Pause").onClick.Invoke();
                Assert.That(hud.Mode, Is.EqualTo(SurvivalHud.ScreenMode.Pause));
                Assert.AreEqual(0, Time.timeScale);
                Assert.IsTrue(hud.BlocksWorldInput);
                Named<Button>("Resume").onClick.Invoke();
                Assert.AreEqual(SurvivalHud.ScreenMode.None, hud.Mode);
                Assert.AreEqual(.75f, Time.timeScale);
                Assert.IsTrue(hud.BlocksWorldInput, "A closing click must not hit the world in this frame.");
            }

            [Test]
            public void InventoryShowsRealStacksAndIdsAndCloseButtonWorks()
            {
                PlayerController player = BindPlayer();
                ResourceInventory inventory = player.GetComponent<ResourceInventory>();
                inventory.Add(ResourceKind.Wood, 7);
                inventory.TryAddBoatPart(42);
                Named<Button>("Inventory").onClick.Invoke();
                Assert.AreEqual(SurvivalHud.ScreenMode.Inventory, hud.Mode);
                Assert.IsTrue(hud.BlocksPointer(Vector2.zero));
                StringAssert.Contains("Wood    7", Named<Text>("Contents").text);
                StringAssert.Contains("Part #42", Named<Text>("Contents").text);
                StringAssert.Contains("+1", Named<Text>("Resource gained").text);
                Named<Button>("Close inventory").onClick.Invoke();
                Assert.AreEqual(SurvivalHud.ScreenMode.None, hud.Mode);
                Assert.IsTrue(hud.BlocksWorldInput);
            }

            [Test]
            public void HoveredResourceOffersHoldClickAndDisplaysActualHitProgress()
            {
                PlayerController player = BindPlayer();
                player.transform.position = new Vector3(1000, 1000, 1000);
                ResourceNode node = Keep(new GameObject("Gathering test tree")).AddComponent<ResourceNode>();
                node.transform.position = player.transform.position;
                node.Configure(ResourceKind.Wood, 10, new GatherRule { hitsPerUnit = 3 });
                node.Stock.Hit();
                WorldSelection selection = hud.gameObject.AddComponent<WorldSelection>();
                typeof(WorldSelection).GetProperty("LocalPlayer").SetValue(selection, player);
                typeof(WorldSelection).GetProperty("Target").SetValue(selection, node);
                typeof(WorldSelection).GetProperty("HoveredTarget").SetValue(selection, node);
                typeof(SurvivalHud).GetField("selection", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(hud, selection);
                Invoke(hud, "RefreshContext");
                StringAssert.Contains("Hold click / E", Named<Text>("Action").text);
                StringAssert.Contains("Gather", Named<Text>("Action").text);
                Assert.AreEqual("1 / 3", Named<Text>("Gather hits").text);
                Image progress = Named<RectTransform>("Gather progress").GetComponentInChildren<Image>();
                Assert.IsTrue(progress.gameObject.activeInHierarchy);
                typeof(WorldSelection).GetProperty("HoveredTarget").SetValue(selection, null);
                Invoke(hud, "RefreshContext");
                Assert.AreEqual("E  ·  Gather", Named<Text>("Action").text, "A nearest fallback must not advertise pointer gathering.");
            }

            [Test]
            public void BuildButtonsSelectActualDefinitionsAndExposeTheirCosts()
            {
                PlayerController player = BindPlayer();
                PlayerBuilder builder = player.gameObject.AddComponent<PlayerBuilder>();
                Invoke(builder, "Awake");
                StructureLibrary library = Keep(ScriptableObject.CreateInstance<StructureLibrary>());
                StructureDef fence = Keep(ScriptableObject.CreateInstance<StructureDef>());
                StructureDef torch = Keep(ScriptableObject.CreateInstance<StructureDef>());
                torch.displayName = "Torch";
                torch.cost = new[] { new ResourceCost { kind = ResourceKind.Wood, amount = 2 } };
                library.structures = new[] { fence, torch };
                SetField(builder, "library", library);
                Invoke(hud, "Bind", player);
                Named<Button>("Build 1").onClick.Invoke();
                Assert.AreEqual(1, builder.SelectedIndex);
                Assert.AreSame(torch, builder.Selected);
                StringAssert.Contains("2 Wood", Named<Button>("Build 1").GetComponentInChildren<Text>().text);
            }

            [Test]
            public void MapAndInventoryAreExclusiveAndClosingMapGuardsTheFrame()
            {
                PlayerController player = BindPlayer();
                Invoke(player, "Awake");
                Minimap map = hud.gameObject.AddComponent<Minimap>();
                typeof(Minimap).GetField("config", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(map, config);
                typeof(Minimap).GetProperty("Map").SetValue(map, Keep(new Texture2D(2, 2)));
                typeof(SurvivalHud).GetField("minimap", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(hud, map);
                hud.ToggleMap();
                Assert.IsTrue(map.Large);
                Assert.AreEqual(SurvivalHud.ScreenMode.Map, hud.Mode);
                hud.ToggleInventory();
                Assert.IsFalse(map.Large);
                Assert.AreEqual(SurvivalHud.ScreenMode.Inventory, hud.Mode);
                hud.ToggleMap();
                map.Toggle();
                Assert.IsFalse(map.Large, "Neither the HUD nor direct map toggle may open behind inventory.");
                hud.Resume();
                hud.ToggleMap();
                Assert.IsTrue(map.Large);
                hud.Back();
                Assert.IsFalse(map.Large);
                Assert.AreEqual(SurvivalHud.ScreenMode.None, hud.Mode);
                Assert.IsTrue(map.BlocksWorldInput);
                Assert.IsTrue(hud.BlocksWorldInput);
            }

            [Test]
            public void NetworkMenuAndPreexistingPauseNeverAcquireOfflinePause()
            {
                var pause = new HudPause();
                Time.timeScale = .5f;
                pause.Open(true);
                Assert.AreEqual(.5f, Time.timeScale);
                pause.Close();
                Assert.AreEqual(.5f, Time.timeScale);
                Time.timeScale = 0;
                pause.Open(false);
                pause.Close();
                Assert.AreEqual(0, Time.timeScale);
            }

            [Test]
            public void DisablingHudRestoresOnlyItsOwnPause()
            {
                BindPlayer();
                Time.timeScale = .8f;
                Named<Button>("Pause").onClick.Invoke();
                Invoke(hud, "OnDisable");
                Assert.AreEqual(.8f, Time.timeScale);
                var pause = new HudPause();
                pause.Open(false);
                Time.timeScale = .4f;
                pause.Close();
                Assert.AreEqual(.4f, Time.timeScale, "Do not overwrite a newer simulation-speed decision.");
            }

            [TestCase(1280, 720)]
            [TestCase(1176, 716)]
            [TestCase(2940, 1790)]
            public void ConstructedRectGeometryFitsSupportedAspectRatios(int width, int height)
            {
                BindPlayer();
                Canvas canvas = hud.GetComponentInChildren<Canvas>();
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                Assert.AreEqual(CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
                Assert.AreEqual(config.referenceResolution, scaler.referenceResolution);
                float scale = Minimap.ReferenceScale(width, height);
                // Exercise the actual constructed RectTransforms at the canvas's effective logical dimensions.
                // This is a geometry regression, not a substitute for native-resolution rendered screenshots.
                scaler.enabled = false;
                canvas.renderMode = RenderMode.WorldSpace;
                RectTransform root = canvas.GetComponent<RectTransform>();
                root.sizeDelta = new Vector2(width / scale, height / scale);
                Canvas.ForceUpdateCanvases();
                var names = new[] { "Supplies", "Time and map", "Health", "Inventory", "Pause", "Build palette", "Field menu", "Island arrival" };
                foreach (string name in names)
                {
                    RectTransform rect = Named<RectTransform>(name);
                    var corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    foreach (Vector3 corner in corners)
                    {
                        Vector3 local = root.InverseTransformPoint(corner);
                        Assert.That(local.x, Is.InRange(root.rect.xMin, root.rect.xMax), name);
                        Assert.That(local.y, Is.InRange(root.rect.yMin, root.rect.yMax), name);
                    }
                }
                Rect health = LocalBounds(root, Named<RectTransform>("Health"));
                Rect palette = LocalBounds(root, Named<RectTransform>("Build palette"));
                Assert.IsFalse(health.Overlaps(palette));
                Assert.IsFalse(LocalBounds(root, Named<RectTransform>("Supplies")).Overlaps(LocalBounds(root, Named<RectTransform>("Time and map"))));
            }

            private static Rect LocalBounds(RectTransform root, RectTransform child)
            {
                var corners = new Vector3[4];
                child.GetWorldCorners(corners);
                Vector3 min = root.InverseTransformPoint(corners[0]);
                Vector3 max = root.InverseTransformPoint(corners[2]);
                return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            }
        }
    }
}
