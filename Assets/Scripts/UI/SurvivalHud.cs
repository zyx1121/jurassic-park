using System.Text;
using JurassicPark.Building;
using JurassicPark.Combat;
using JurassicPark.Core;
using JurassicPark.Player;
using JurassicPark.Scene;
using UnityEngine;

namespace JurassicPark.UI
{
    /// <summary>Passive native-resolution feedback; only its visible panels consume world clicks.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class SurvivalHud : MonoBehaviour, IWorldInputBlocker
    {
        [SerializeField] private HudConfig config;

        private Canvas canvas;
        private RectTransform canvasRect;
        private Font font;
        private PlayerController player;
        private Health health;
        private ResourceInventory inventory;
        private PlayerBuilder builder;
        private WorldSelection selection;
        private Minimap minimap;
        private Camera worldCamera;
        private FollowCamera followCamera;
        private RectTransform statusPanel, clockPanel, objectivePanel, targetPanel, promptPanel, controlsPanel;
        private RectTransform brackets;
        private readonly UnityEngine.UI.Image[] bracketLines = new UnityEngine.UI.Image[8];
        private UnityEngine.UI.Text healthText, clockText, objectiveText, targetTitle, targetDetails, targetAction;
        private UnityEngine.UI.Text promptTitle, promptDetails, controlsText;
        private UnityEngine.UI.Image healthFill, dayFill;
        private readonly UnityEngine.UI.Text[] resourceText = new UnityEngine.UI.Text[4];
        private readonly UnityEngine.UI.Image[] resourceFill = new UnityEngine.UI.Image[3];
        private readonly int[] counts = { -1, -1, -1, -1 };
        private readonly int[] caps = { -1, -1, -1, -1 };
        private static readonly ResourceKind[] Kinds =
            { ResourceKind.Wood, ResourceKind.Stone, ResourceKind.Food, ResourceKind.BoatPart };
        private float lastHealth = float.NaN, lastMax = float.NaN, nextRefresh;
        private int lastMinute = -1, lastDay = -1;
        private DayPhase lastPhase;
        private StructureDef lastStructure;
        private string buildCost = "";
        private readonly StringBuilder costBuilder = new StringBuilder();

        public bool BlocksWorldInput => false;

        private void Start()
        {
            if (config == null)
            {
                Debug.LogError("SurvivalHud requires a HudConfig. Rebuild the scene with BuildFeedbackHud.", this);
                enabled = false;
                return;
            }
            selection = GetComponent<WorldSelection>();
            minimap = GetComponent<Minimap>();
            BuildCanvas();
        }

        private void OnEnable()
        {
            WorldInputBlockers.Register(this);
            nextRefresh = 0f;
        }

        private void OnDisable()
        {
            WorldInputBlockers.Unregister(this);
            if (canvas != null) canvas.gameObject.SetActive(false);
            Bind(null);
        }

        private void OnDestroy()
        {
            WorldInputBlockers.Unregister(this);
            if (canvas != null) Destroy(canvas.gameObject);
        }

        private void LateUpdate()
        {
            if (canvas == null) return;
            if (worldCamera == null || !worldCamera.isActiveAndEnabled)
            {
                worldCamera = Camera.main;
                followCamera = worldCamera != null ? worldCamera.GetComponent<FollowCamera>() : null;
            }
            PlayerController local = selection != null ? selection.LocalPlayer : null;
            if (local == null && followCamera != null && followCamera.Target != null)
                local = followCamera.Target.GetComponentInParent<PlayerController>();
            if (local == null || !local.isActiveAndEnabled)
            {
                local = null;
                foreach (PlayerController candidate in PlayerController.All)
                    if (candidate != null && candidate.isActiveAndEnabled) { local = candidate; break; }
            }
            if (local != player) Bind(local);
            bool visible = player != null && (minimap == null || !minimap.Large);
            SetVisible(canvas.gameObject, visible);
            if (!visible) return;

            RefreshHealth();
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + Mathf.Max(0.05f, config.refreshInterval);
                RefreshResources();
                RefreshClock();
            }
            RefreshContext();
            UpdateBrackets();
        }

        private void Bind(PlayerController local)
        {
            player = local;
            health = local != null ? local.GetComponent<Health>() : null;
            inventory = local != null ? local.GetComponent<ResourceInventory>() : null;
            builder = local != null ? local.GetComponent<PlayerBuilder>() : null;
            lastHealth = lastMax = float.NaN;
            for (int i = 0; i < counts.Length; i++) counts[i] = caps[i] = -1;
            lastMinute = lastDay = -1;
            lastStructure = null;
            nextRefresh = 0f;
        }

        public bool BlocksPointer(Vector2 screenPosition)
        {
            if (!isActiveAndEnabled || canvas == null || !canvas.gameObject.activeInHierarchy) return false;
            return Contains(statusPanel, screenPosition) || Contains(clockPanel, screenPosition)
                || Contains(objectivePanel, screenPosition) || Contains(targetPanel, screenPosition)
                || Contains(promptPanel, screenPosition) || Contains(controlsPanel, screenPosition);
        }

        private static bool Contains(RectTransform panel, Vector2 point) =>
            panel != null && panel.gameObject.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(panel, point, null);

        private void RefreshHealth()
        {
            if (health == null)
            {
                SetText(healthText, "Health unavailable");
                Fill(healthFill, 0f);
                return;
            }
            if (health.Current == lastHealth && health.Max == lastMax) return;
            lastHealth = health.Current;
            lastMax = health.Max;
            string state = !health.IsAlive ? "Dead" : health.Normalized <= config.lowHealthThreshold ? "Low health" : "Health";
            SetText(healthText, $"{state}   {health.Current:0.#} / {health.Max:0.#}");
            healthFill.color = health.Normalized <= config.lowHealthThreshold ? config.danger : config.healthy;
            healthText.color = health.IsAlive && health.Normalized > config.lowHealthThreshold ? config.text : config.danger;
            Fill(healthFill, health.Normalized);
        }

        private void RefreshResources()
        {
            for (int i = 0; i < Kinds.Length; i++)
            {
                int count = inventory != null ? inventory.Get(Kinds[i]) : 0;
                int cap = inventory != null ? inventory.Cap(Kinds[i]) : 0;
                if (count == counts[i] && cap == caps[i]) continue;
                counts[i] = count;
                caps[i] = cap;
                string name = i == 3 ? "Boat parts" : Kinds[i].ToString();
                string limit = cap == int.MaxValue ? "no cap" : cap.ToString();
                SetText(resourceText[i], $"{name}   {count} / {limit}");
                if (i < 3) Fill(resourceFill[i], cap > 0 && cap != int.MaxValue ? (float)count / cap : 0f);
            }
            SetText(objectiveText, counts[3] > 0
                ? "Boat part secured.\nDock repairs are not available yet."
                : "Find boat parts at the ruined facilities.");
        }

        private void RefreshClock()
        {
            DayNightCycle cycle = DayNightCycle.Current;
            if (cycle == null)
            {
                SetText(clockText, "Island exploration");
                Fill(dayFill, 0f);
                return;
            }
            int minute = ClockMinutes(cycle.NormalizedTime);
            if (minute != lastMinute || cycle.DayNumber != lastDay || cycle.Phase != lastPhase)
            {
                lastMinute = minute;
                lastDay = cycle.DayNumber;
                lastPhase = cycle.Phase;
                SetText(clockText, $"Day {cycle.DayNumber}  ·  {cycle.Phase}  {minute / 60:00}:{minute % 60:00}");
            }
            Fill(dayFill, cycle.NormalizedTime);
        }

        public static int ClockMinutes(float normalizedTime) =>
            Mathf.FloorToInt(Mathf.Repeat(normalizedTime + 0.25f, 1f) * 1440f);

        private void RefreshContext()
        {
            bool building = builder != null && builder.IsBuilding;
            bool alive = health == null || health.IsAlive;
            bool target = !building && alive && selection != null && selection.Target != null;
            SetVisible(targetPanel.gameObject, target);
            SetVisible(promptPanel.gameObject, building || !alive);
            SetText(controlsText, building
                ? "Tab  Cycle     Q  Rotate     Left click  Place     Esc  Cancel     M  Map"
                : "WASD  Move    Shift  Sprint    E  Interact    Left click  Select    Tab  Build    M  Map");
            if (!alive)
            {
                SetText(promptTitle, "You died");
                SetText(promptDetails, "Health depleted.");
                promptTitle.color = config.danger;
                promptDetails.color = config.muted;
            }
            else if (building)
            {
                StructureDef def = builder.Selected;
                if (lastStructure != def)
                {
                    lastStructure = def;
                    costBuilder.Clear();
                    if (def != null && def.cost != null)
                        foreach (ResourceCost cost in def.cost)
                        {
                            if (costBuilder.Length > 0) costBuilder.Append(" · ");
                            costBuilder.Append(cost.amount).Append(' ').Append(cost.kind);
                        }
                    buildCost = costBuilder.Length > 0 ? costBuilder.ToString() : "No resource cost";
                }
                SetText(promptTitle, def != null ? $"Build: {def.displayName}   —   {buildCost}" : "Build mode");
                SetText(promptDetails, builder.PreviewValid ? "Ready to place" : $"Cannot place: {builder.PreviewReason}");
                promptTitle.color = config.accent;
                promptDetails.color = builder.PreviewValid ? config.healthy : config.danger;
            }
            if (!target) return;
            string mode = selection.HasSelection ? "Selected" : selection.HoveredTarget != null ? "Under cursor" : "Nearby";
            SetText(targetTitle, $"{selection.TargetName}");
            SetText(targetDetails, $"{mode} · {selection.TargetDistance:0.0} m\n{selection.TargetDetails}");
            SetText(targetAction, selection.ActionText);
            targetTitle.color = selection.HasSelection ? config.accent : config.text;
            targetAction.color = selection.CanInteract ? config.accent : config.muted;
        }

        private void UpdateBrackets()
        {
            bool show = selection != null && selection.Target != null && worldCamera != null
                && (builder == null || !builder.IsBuilding) && (health == null || health.IsAlive);
            Rect pixels = default;
            show = show && selection.TryGetScreenRect(worldCamera, out pixels);
            SetVisible(brackets.gameObject, show);
            if (!show) return;
            Vector2 min = new Vector2(Mathf.Clamp(pixels.xMin, 4f, Screen.width - 4f),
                Mathf.Clamp(pixels.yMin, 4f, Screen.height - 4f));
            Vector2 max = new Vector2(Mathf.Clamp(pixels.xMax, 4f, Screen.width - 4f),
                Mathf.Clamp(pixels.yMax, 4f, Screen.height - 4f));
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, min, null, out Vector2 localMin);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, max, null, out Vector2 localMax);
            brackets.anchoredPosition = (localMin + localMax) * 0.5f;
            brackets.sizeDelta = Vector2.Max(Vector2.one * 8f, localMax - localMin);
            Color color = selection.HasSelection ? config.accent : config.muted;
            foreach (UnityEngine.UI.Image line in bracketLines) line.color = color;
        }

        private void BuildCanvas()
        {
            var root = new GameObject("Survival HUD", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            canvasRect = root.GetComponent<RectTransform>();
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            clockPanel = Panel("Island time", new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(310f, 58f));
            clockText = Label(clockPanel, "Day", 14f, 8f, 286f, 30f, config.headingFontSize);
            dayFill = Bar(clockPanel, "Day progress", 14f, 44f, 282f, 3f, config.accent);
            objectivePanel = Panel("Objective", new Vector2(0f, 1f), new Vector2(16f, -84f), new Vector2(310f, 100f));
            Label(objectivePanel, "Objective", 14f, 8f, 280f, 22f, config.bodyFontSize).color = config.accent;
            objectiveText = Label(objectivePanel, "", 14f, 34f, 280f, 58f, config.bodyFontSize);

            statusPanel = Panel("Survivor", Vector2.zero, new Vector2(16f, 16f), new Vector2(296f, 200f));
            healthText = Label(statusPanel, "", 14f, 10f, 270f, 28f, config.headingFontSize);
            healthFill = Bar(statusPanel, "Health", 14f, 44f, 268f, 10f, config.healthy);
            for (int i = 0; i < 3; i++)
            {
                resourceText[i] = Label(statusPanel, "", 14f, 64f + i * 31f, 268f, 23f, config.bodyFontSize);
                resourceFill[i] = Bar(statusPanel, Kinds[i].ToString(), 14f, 88f + i * 31f, 268f, 3f, config.muted);
            }
            resourceText[3] = Label(statusPanel, "", 14f, 163f, 268f, 28f, config.bodyFontSize);
            resourceText[3].color = config.accent;

            controlsPanel = Panel("Controls", new Vector2(1f, 0f), new Vector2(-16f, 16f), new Vector2(880f, 40f));
            controlsText = Label(controlsPanel, "", 12f, 4f, 856f, 32f, config.bodyFontSize);
            controlsText.alignment = TextAnchor.MiddleCenter;
            targetPanel = Panel("Target", new Vector2(1f, 0f), new Vector2(-16f, 72f), new Vector2(292f, 184f));
            targetTitle = Label(targetPanel, "", 14f, 8f, 264f, 52f, config.headingFontSize);
            targetDetails = Label(targetPanel, "", 14f, 64f, 264f, 64f, config.bodyFontSize);
            targetAction = Label(targetPanel, "", 14f, 132f, 264f, 38f, config.bodyFontSize);
            promptPanel = Panel("Build or survival prompt", new Vector2(0.5f, 0f), new Vector2(0f, 80f), new Vector2(500f, 94f));
            promptTitle = Label(promptPanel, "", 14f, 8f, 472f, 44f, config.bodyFontSize);
            promptDetails = Label(promptPanel, "", 14f, 54f, 472f, 32f, config.bodyFontSize);

            brackets = Rect("Target brackets", canvasRect, Vector2.one * 0.5f, Vector2.zero, Vector2.one * 32f);
            for (int i = 0; i < 4; i++)
            {
                Vector2 corner = new Vector2(i % 2, i / 2);
                bracketLines[i * 2] = Image(Rect("Horizontal", brackets, corner, Vector2.zero, new Vector2(14f, 2f)), config.accent);
                bracketLines[i * 2 + 1] = Image(Rect("Vertical", brackets, corner, Vector2.zero, new Vector2(2f, 14f)), config.accent);
            }
            brackets.SetAsFirstSibling();
            SetVisible(root, false);
        }

        private RectTransform Panel(string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            RectTransform rect = Rect(name, canvasRect, anchor, position, size);
            Image(rect, config.panel);
            return rect;
        }

        private UnityEngine.UI.Text Label(RectTransform parent, string text, float x, float y, float width, float height, int size)
        {
            RectTransform rect = Rect("Label", parent, new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(width, height));
            var label = rect.gameObject.AddComponent<UnityEngine.UI.Text>();
            label.font = font;
            label.fontSize = size;
            label.color = config.text;
            label.text = text;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.alignment = TextAnchor.UpperLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            return label;
        }

        private UnityEngine.UI.Image Bar(RectTransform parent, string name, float x, float y, float width, float height, Color color)
        {
            RectTransform track = Rect(name, parent, new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(width, height));
            Image(track, config.track);
            RectTransform fill = Rect("Fill", track, Vector2.zero, Vector2.zero, Vector2.zero);
            fill.anchorMax = Vector2.one;
            return Image(fill, color);
        }

        private static UnityEngine.UI.Image Image(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static void Fill(UnityEngine.UI.Image image, float amount)
        {
            Vector2 anchor = new Vector2(Mathf.Clamp01(amount), 1f);
            if (image.rectTransform.anchorMax != anchor) image.rectTransform.anchorMax = anchor;
        }

        private static void SetText(UnityEngine.UI.Text label, string value)
        {
            if (label.text != value) label.text = value;
        }

        private static void SetVisible(GameObject obj, bool visible)
        {
            if (obj.activeSelf != visible) obj.SetActive(visible);
        }
    }
}
