using System.Collections.Generic;
using System.Text;
using JurassicPark.Building;
using JurassicPark.Combat;
using JurassicPark.Core;
using JurassicPark.Net;
using JurassicPark.Player;
using JurassicPark.Scene;
using JurassicPark.World;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace JurassicPark.UI
{
    /// <summary>Native-resolution, pointer-driven survival interface. Geometry guards precede input callbacks.</summary>
    [DefaultExecutionOrder(-100)]
    public sealed class SurvivalHud : MonoBehaviour, IWorldInputBlocker
    {
        [SerializeField] private HudConfig config;
        public enum ScreenMode { None, Inventory, Map, Pause, Controls, Quit }
        public ScreenMode Mode { get; private set; }
        public HudConfig Config => config;

        private Canvas canvas;
        private RectTransform canvasRect, gameplay, resources, vitals, context, palette, paletteContent;
        private RectTransform modal, menu, inventoryPanel, pausePanel, controlsPanel, quitPanel, mapPanel, lobbyPanel, unavailablePanel;
        private RectTransform lobbyCard, lobbyMainChoices, lobbyLanChoices;
        private Text healthText, clockText, targetText, actionText, progressText, buildHint, buildDescription;
        private Text notification, inventoryText, pauseSubtitle, lobbyStatus, unavailableText;
        private Image healthFill, gatherFill;
        private InputField address;
        private Button mapButton, buildButton;
        private readonly List<Button> buildButtons = new List<Button>();
        private readonly List<RectTransform> pointerRegions = new List<RectTransform>();
        private readonly Text[] resourceText = new Text[4];
        private static readonly ResourceKind[] Kinds = { ResourceKind.Wood, ResourceKind.Stone, ResourceKind.Food, ResourceKind.BoatPart };
        private PlayerController player;
        private Health health;
        private ResourceInventory inventory;
        private PlayerBuilder builder;
        private WorldSelection selection;
        private Minimap minimap;
        private NetLobby lobby;
        private Camera worldCamera;
        private readonly HudPause pause = new HudPause();
        private readonly HudPause arrivalPause = new HudPause();
        private int closedFrame = -1, hoveredBuild = -1;
        private bool hadPlayer, lobbyVisible, unavailableVisible;
        private bool lanExpanded;
        private float nextRefresh, notificationUntil;
        private readonly StringBuilder text = new StringBuilder();

        public bool BlocksWorldInput => isActiveAndEnabled && canvas != null &&
            (Mode != ScreenMode.None || lobbyVisible || unavailableVisible || closedFrame == Time.frameCount || OpeningModalKey);

        // InputAction callbacks happen before Update: reserve modal-opening keys immediately.
        private bool OpeningModalKey
        {
            get
            {
                Keyboard keys = Keyboard.current;
                if (player == null || keys == null) return false;
                return keys.tabKey.wasPressedThisFrame
                    || (keys.mKey.wasPressedThisFrame && minimap != null && minimap.Map != null)
                    || (keys.escapeKey.wasPressedThisFrame && (builder == null || !builder.IsBuilding));
            }
        }

        private void Start()
        {
            if (config == null || !config.HasFonts)
            {
                Debug.LogError("SurvivalHud requires HudConfig with bodyFont, emphasisFont and headingFont. Run build_hud_assets and regenerate this scene.", this);
                enabled = false;
                return;
            }
            selection = GetComponent<WorldSelection>();
            minimap = GetComponent<Minimap>();
            if (minimap != null) minimap.Configure(config);
            lobby = FindFirstObjectByType<NetLobby>();
            BuildCanvas();
            Canvas.willRenderCanvases += PositionContext;
            RefreshPlayer();
            RefreshVisibility();
        }

        private void OnEnable()
        {
            WorldInputBlockers.Register(this);
            if (canvas != null)
            {
                canvas.gameObject.SetActive(true);
                Canvas.willRenderCanvases += PositionContext;
            }
        }

        private void OnDisable()
        {
            WorldInputBlockers.Unregister(this);
            Canvas.willRenderCanvases -= PositionContext;
            pause.Close();
            arrivalPause.Close();
            Mode = ScreenMode.None;
            if (minimap != null) { minimap.Close(); minimap.Visible = false; }
            if (canvas != null) canvas.gameObject.SetActive(false);
            Bind(null);
        }

        private void OnDestroy()
        {
            WorldInputBlockers.Unregister(this);
            Canvas.willRenderCanvases -= PositionContext;
            pause.Close();
            arrivalPause.Close();
        }

        private void Update()
        {
            if (canvas == null) return;
            RefreshPlayer();
            Keyboard keys = Keyboard.current;
            if (keys != null && (!lobbyVisible || Mode != ScreenMode.None || lanExpanded))
            {
                if (keys.escapeKey.wasPressedThisFrame) Back();
                else if (player != null && keys.tabKey.wasPressedThisFrame
                    && (Mode == ScreenMode.None || Mode == ScreenMode.Inventory || Mode == ScreenMode.Map))
                    ToggleInventory();
                else if (player != null && keys.mKey.wasPressedThisFrame
                    && (Mode == ScreenMode.None || Mode == ScreenMode.Map))
                    ToggleMap();
            }
            RefreshVisibility();
        }

        private void LateUpdate()
        {
            if (canvas == null) return;
            if (player != null)
            {
                RefreshHealth();
                RefreshContext();
                RefreshBuild();
                if (Time.unscaledTime >= nextRefresh)
                {
                    nextRefresh = Time.unscaledTime + config.refreshInterval;
                    RefreshResources();
                    RefreshClock();
                }
            }
            if (lobby != null) lobbyStatus.text = lobby.Status == "lobby" || lobby.Status == "offline" ? "" : lobby.Status;
            if (unavailableVisible)
                unavailableText.text = health != null && !health.IsAlive ? "Your survivor died.\nThe island claimed another life."
                    : hadPlayer ? "Your survivor is unavailable.\n" + (lobby != null ? lobby.Status : "Return to the menu to quit.")
                    : "Waiting for your survivor…\n" + (lobby != null ? lobby.Status : "No local player is available.");
            notification.gameObject.SetActive(player != null && Mode == ScreenMode.None
                && (builder == null || !builder.IsBuilding) && Time.unscaledTime < notificationUntil);
        }

        private void RefreshPlayer()
        {
            if (worldCamera == null) worldCamera = Camera.main;
            PlayerController local = selection != null ? selection.LocalPlayer : null;
            if (local == null && worldCamera != null)
            {
                FollowCamera follow = worldCamera.GetComponent<FollowCamera>();
                if (follow != null && follow.Target != null) local = follow.Target.GetComponentInParent<PlayerController>();
            }
            if (local == null)
                foreach (PlayerController candidate in PlayerController.All)
                    if (candidate != null && candidate.isActiveAndEnabled) { local = candidate; break; }
            if (local != null && !local.isActiveAndEnabled) local = null;
            if (local != player) Bind(local);
            if (player != null) hadPlayer = true;
        }

        private void Bind(PlayerController local)
        {
            if (inventory != null) inventory.Changed -= OnInventoryChanged;
            player = local;
            health = local != null ? local.GetComponent<Health>() : null;
            inventory = local != null ? local.GetComponent<ResourceInventory>() : null;
            builder = local != null ? local.GetComponent<PlayerBuilder>() : null;
            if (inventory != null) inventory.Changed += OnInventoryChanged;
            notificationUntil = 0;
            nextRefresh = 0;
            hoveredBuild = -1;
            if (paletteContent != null) RebuildPalette();
            if (local == null && (Mode == ScreenMode.Map || Mode == ScreenMode.Inventory)) SetMode(ScreenMode.None);
        }

        private void OnInventoryChanged(ResourceKind kind, int delta, int total)
        {
            if (delta <= 0) return;
            notification.text = $"+{delta}  {ResourceName(kind)}";
            notificationUntil = Time.unscaledTime + config.notificationDuration;
        }

        public static int ClockMinutes(float normalizedTime) =>
            Mathf.FloorToInt(Mathf.Repeat(normalizedTime + 0.25f, 1f) * 1440f);

        private static string ResourceName(ResourceKind kind) => kind == ResourceKind.BoatPart ? "Boat parts" : kind.ToString();

        public bool BlocksPointer(Vector2 position)
        {
            if (!isActiveAndEnabled || canvas == null || !canvas.gameObject.activeInHierarchy) return false;
            if (BlocksWorldInput) return true;
            foreach (RectTransform region in pointerRegions)
                if (region != null && region.gameObject.activeInHierarchy
                    && RectTransformUtility.RectangleContainsScreenPoint(region, position, null)) return true;
            return false;
        }

        public void ToggleInventory()
        {
            if (player == null || (Mode != ScreenMode.None && Mode != ScreenMode.Map && Mode != ScreenMode.Inventory)) return;
            SetMode(Mode == ScreenMode.Inventory ? ScreenMode.None : ScreenMode.Inventory);
            RefreshResources();
        }

        public void ToggleMap()
        {
            if (player == null || minimap == null || (Mode != ScreenMode.None && Mode != ScreenMode.Map)) return;
            if (Mode == ScreenMode.Map) SetMode(ScreenMode.None);
            else if (minimap.Map != null) SetMode(ScreenMode.Map);
        }

        public void Back()
        {
            if (Mode == ScreenMode.None && lobbyVisible && lanExpanded) ShowLan(false);
            else if (Mode == ScreenMode.Quit && lobbyVisible) SetMode(ScreenMode.None);
            else if (Mode == ScreenMode.Controls || Mode == ScreenMode.Quit) SetMode(ScreenMode.Pause);
            else if (Mode != ScreenMode.None) SetMode(ScreenMode.None);
            else if (builder != null && builder.IsBuilding && !unavailableVisible) return; // PlayerBuilder consumes this Esc.
            else if (!lobbyVisible) SetMode(ScreenMode.Pause);
        }

        public void Resume() => SetMode(ScreenMode.None);

        private void ShowLan(bool value)
        {
            lanExpanded = value;
            RefreshVisibility();
        }

        private bool IsNetworkSession => (lobby != null && lobby.IsNetworkSession)
            || (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening);

        private static bool IsPauseScreen(ScreenMode mode) =>
            mode == ScreenMode.Pause || mode == ScreenMode.Controls || mode == ScreenMode.Quit;

        private void SetMode(ScreenMode mode)
        {
            bool wasPause = IsPauseScreen(Mode), willPause = IsPauseScreen(mode);
            if (Mode != mode) closedFrame = Time.frameCount;
            if (wasPause && !willPause) pause.Close();
            if (!wasPause && willPause) pause.Open(IsNetworkSession);
            if (minimap != null)
            {
                if (mode != ScreenMode.Map) minimap.Close();
                else if (!minimap.Large) minimap.Toggle();
            }
            Mode = mode;
            if (canvas != null) RefreshVisibility();
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        }

        private void RefreshVisibility()
        {
            lobbyVisible = player == null && !hadPlayer && lobby != null && !lobby.Started;
            if (lobbyVisible) arrivalPause.Open(IsNetworkSession);
            else arrivalPause.Close();
            bool alive = health == null || health.IsAlive;
            unavailableVisible = (player == null && !lobbyVisible) || !alive;
            gameplay.gameObject.SetActive(player != null && alive && Mode == ScreenMode.None);
            modal.gameObject.SetActive(Mode != ScreenMode.None || lobbyVisible || unavailableVisible);
            menu.gameObject.SetActive(Mode != ScreenMode.None && Mode != ScreenMode.Map);
            inventoryPanel.gameObject.SetActive(Mode == ScreenMode.Inventory);
            pausePanel.gameObject.SetActive(Mode == ScreenMode.Pause);
            controlsPanel.gameObject.SetActive(Mode == ScreenMode.Controls);
            quitPanel.gameObject.SetActive(Mode == ScreenMode.Quit);
            mapPanel.gameObject.SetActive(Mode == ScreenMode.Map);
            lobbyPanel.gameObject.SetActive(lobbyVisible && Mode == ScreenMode.None);
            lobbyMainChoices.gameObject.SetActive(!lanExpanded);
            lobbyLanChoices.gameObject.SetActive(lanExpanded);
            lobbyCard.sizeDelta = new Vector2(config.modalSize.x, lanExpanded ? config.lobbyHeight : config.mainMenuHeight);
            unavailablePanel.gameObject.SetActive(unavailableVisible && Mode == ScreenMode.None);
            palette.gameObject.SetActive(builder != null && builder.IsBuilding && alive);
            if (minimap != null) minimap.Visible = player != null && alive && (Mode == ScreenMode.None || Mode == ScreenMode.Map);
            pauseSubtitle.text = IsNetworkSession ? "Online session · the world continues" : "Take a breath. The island is paused.";
            mapButton.interactable = minimap != null && minimap.Map != null;
            buildButton.interactable = builder != null && builder.Definitions.Count > 0 && alive;
        }

        private void RefreshHealth()
        {
            healthText.text = health == null ? "Health unavailable" : !health.IsAlive ? "You died" : $"Health  {health.Current:0} / {health.Max:0}";
            healthFill.fillAmount = health != null ? health.Normalized : 0;
            healthFill.rectTransform.anchorMax = new Vector2(healthFill.fillAmount, 1);
            healthFill.color = health != null && health.Normalized <= config.lowHealthThreshold ? config.danger : config.healthy;
        }

        private void RefreshResources()
        {
            text.Clear();
            for (int i = 0; i < Kinds.Length; i++)
            {
                int count = inventory != null ? inventory.Get(Kinds[i]) : 0;
                resourceText[i].text = $"{ResourceName(Kinds[i])}  {count}";
                int cap = inventory != null ? inventory.Cap(Kinds[i]) : 0;
                text.Append(ResourceName(Kinds[i])).Append("    ").Append(count);
                if (cap != int.MaxValue) text.Append(" / ").Append(cap);
                text.AppendLine().AppendLine();
            }
            text.AppendLine("Carried boat parts");
            if (inventory == null || inventory.BoatParts.Count == 0) text.Append("None found");
            else
                foreach (int id in inventory.BoatParts) text.Append("Part #").Append(id).Append("   ");
            inventoryText.text = text.ToString();
        }

        private void RefreshClock()
        {
            DayNightCycle cycle = DayNightCycle.Current;
            if (cycle == null) { clockText.text = "Isla Nublar"; return; }
            int minutes = ClockMinutes(cycle.NormalizedTime);
            clockText.text = $"Day {cycle.DayNumber}  ·  {minutes / 60:00}:{minutes % 60:00}";
        }

        private void RefreshContext()
        {
            bool visible = Mode == ScreenMode.None && (builder == null || !builder.IsBuilding)
                && (health == null || health.IsAlive) && selection != null && selection.Target != null;
            context.gameObject.SetActive(visible);
            if (!visible) return;
            targetText.text = selection.TargetName;
            bool pointerTarget = selection.HoveredTarget == selection.Target && selection.HoveredTarget != null;
            ResourceNode node = selection.Target as ResourceNode;
            string binding = pointerTarget ? node != null ? "Hold click / E" : "Click / E" : "E";
            actionText.text = selection.CanInteract
                ? $"{binding}  ·  {selection.ActionText}"
                : selection.ActionText;
            actionText.color = selection.CanInteract ? config.accent : config.muted;
            bool gathering = node != null && node.Stock != null && node.Stock.Hits > 0 && node.Stock.Remaining > 0;
            gatherFill.transform.parent.gameObject.SetActive(gathering);
            progressText.gameObject.SetActive(gathering);
            if (gathering)
            {
                gatherFill.fillAmount = (float)node.Stock.Hits / node.Stock.HitsPerUnit;
                gatherFill.rectTransform.anchorMax = new Vector2(gatherFill.fillAmount, 1);
                progressText.text = $"{node.Stock.Hits} / {node.Stock.HitsPerUnit}";
            }
        }

        // Input routing runs early; anchoring waits until FollowCamera's LateUpdate has finished.
        private void PositionContext()
        {
            if (canvas == null || context == null || !context.gameObject.activeInHierarchy || selection == null) return;
            Vector2 screen = Mouse.current != null ? Mouse.current.position.ReadValue() : new Vector2(Screen.width * .5f, Screen.height * .5f);
            screen.y -= config.contextOffset * canvas.scaleFactor;
            if (worldCamera != null && selection.TryGetScreenRect(worldCamera, out Rect bounds))
                screen = new Vector2(bounds.center.x, bounds.yMin - config.contextOffset * canvas.scaleFactor);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, screen, null, out Vector2 local);
            Vector2 half = canvasRect.rect.size * .5f;
            local.x = Mathf.Clamp(local.x, -half.x + config.contextWidth * .5f + config.margin, half.x - config.contextWidth * .5f - config.margin);
            local.y = Mathf.Clamp(local.y, -half.y + config.contextHeight + config.margin, half.y - config.margin);
            context.anchoredPosition = local;
        }

        public static string CostText(StructureDef definition)
        {
            if (definition == null) return "Unavailable";
            var result = new StringBuilder();
            if (definition.cost != null)
                foreach (ResourceCost cost in definition.cost)
                {
                    if (result.Length > 0) result.Append(" · ");
                    result.Append(cost.amount).Append(' ').Append(cost.kind);
                }
            return result.Length > 0 ? result.ToString() : "No resource cost";
        }

        private void RebuildPalette()
        {
            foreach (Button button in buildButtons)
                if (button != null)
                {
                    button.gameObject.SetActive(false);
                    if (Application.isPlaying) Destroy(button.gameObject);
                    else DestroyImmediate(button.gameObject);
                }
            buildButtons.Clear();
            if (builder == null) return;
            for (int i = 0; i < builder.Definitions.Count; i++)
            {
                int index = i;
                StructureDef definition = builder.Definitions[i];
                Button button = MakeButton(paletteContent, $"Build {i}", definition != null ? $"{definition.displayName}\n{CostText(definition)}" : "Unavailable",
                    new Vector2(i * (config.buildCardWidth + config.gap), 0),
                    new Vector2(config.buildCardWidth, config.buildCardHeight), () => builder.Select(index), new Vector2(0, 1));
                button.interactable = definition != null;
                Text label = button.GetComponentInChildren<Text>();
                label.fontSize = config.smallFontSize;
                var trigger = button.gameObject.AddComponent<EventTrigger>();
                var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                enter.callback.AddListener(_ => hoveredBuild = index);
                trigger.triggers.Add(enter);
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => { if (hoveredBuild == index) hoveredBuild = -1; });
                trigger.triggers.Add(exit);
                buildButtons.Add(button);
            }
            paletteContent.sizeDelta = new Vector2(Mathf.Max(config.paletteWidth - config.margin * 2,
                builder.Definitions.Count * (config.buildCardWidth + config.gap)), config.buildCardHeight);
        }

        private void RefreshBuild()
        {
            bool building = builder != null && builder.IsBuilding && (health == null || health.IsAlive);
            palette.gameObject.SetActive(building);
            if (!building) return;
            for (int i = 0; i < buildButtons.Count; i++)
            {
                ColorBlock colors = buildButtons[i].colors;
                colors.normalColor = i == builder.SelectedIndex ? config.selected : config.button;
                buildButtons[i].colors = colors;
            }
            StructureDef selected = hoveredBuild >= 0 && hoveredBuild < builder.Definitions.Count
                ? builder.Definitions[hoveredBuild] : builder.Selected;
            buildDescription.text = selected != null
                ? $"{selected.displayName} · {selected.footprint.x} × {selected.footprint.y} m · {CostText(selected)}"
                    + (selected.isGate ? " · Opens and closes" : selected.emitsLight ? " · Provides light" : selected.solid ? " · Blocks movement" : "")
                : "Choose a structure";
            buildHint.text = (builder.PreviewValid ? "Click to place" : builder.PreviewReason) + "   ·   R Rotate   ·   Right click / Esc Cancel";
            buildHint.color = builder.PreviewValid ? config.accent : config.muted;
        }

        private void BuildCanvas()
        {
            var root = new GameObject("Survival interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            canvasRect = root.GetComponent<RectTransform>();
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = config.referenceResolution;
            scaler.matchWidthOrHeight = config.widthHeightMatch;
            EnsureEventSystem();
            gameplay = Stretch(root.transform, "In-game HUD");
            resources = Rect(gameplay, "Supplies", new Vector2(config.margin, -config.margin),
                new Vector2(config.resourceWidth * 4, config.resourceHeight), new Vector2(0, 1));
            pointerRegions.Add(resources);
            for (int i = 0; i < Kinds.Length; i++)
                resourceText[i] = Label(resources, ResourceName(Kinds[i]), "", new Vector2(i * config.resourceWidth, 0),
                    new Vector2(config.resourceWidth, config.resourceHeight), config.bodyFontSize, config.emphasisFont);
            RectTransform time = Rect(gameplay, "Time and map", new Vector2(-config.margin, -config.margin),
                new Vector2(config.healthWidth, config.buttonHeight), new Vector2(1, 1));
            pointerRegions.Add(time);
            clockText = Label(time, "Island time", "", Vector2.zero, new Vector2(config.healthWidth - config.compactButtonWidth, config.buttonHeight), config.smallFontSize);
            mapButton = MakeButton(time, "Map", "Map  M", new Vector2(config.healthWidth - config.compactButtonWidth, 0), new Vector2(config.compactButtonWidth, config.buttonHeight), ToggleMap);
            vitals = Rect(gameplay, "Health", new Vector2(config.margin, config.margin),
                new Vector2(config.healthWidth, config.healthHeight), new Vector2(0, 0));
            pointerRegions.Add(vitals);
            healthText = Label(vitals, "Health value", "", Vector2.zero, new Vector2(config.healthWidth, config.lineHeight * 1.5f), config.emphasisFontSize, config.emphasisFont);
            healthFill = Bar(vitals, "Health bar", new Vector2(0, -config.buttonHeight), new Vector2(config.healthWidth, config.progressHeight * 2));
            Button pack = MakeButton(gameplay, "Inventory", "Inventory  Tab", new Vector2(-config.margin, config.margin),
                new Vector2(config.inventoryButtonWidth, config.buttonHeight), ToggleInventory, new Vector2(1, 0));
            pointerRegions.Add(pack.GetComponent<RectTransform>());
            buildButton = MakeButton(gameplay, "Build", "Build  B", new Vector2(-config.margin - config.inventoryButtonWidth - config.gap, config.margin),
                new Vector2(config.buildButtonWidth, config.buttonHeight),
                () => { if (builder != null) { builder.SetBuilding(!builder.IsBuilding); RefreshVisibility(); } }, new Vector2(1, 0));
            pointerRegions.Add(buildButton.GetComponent<RectTransform>());
            Button pauseButton = MakeButton(gameplay, "Pause", "Menu", new Vector2(-config.margin - config.inventoryButtonWidth - config.buildButtonWidth - config.gap * 2, config.margin),
                new Vector2(config.compactButtonWidth, config.buttonHeight), () => SetMode(ScreenMode.Pause), new Vector2(1, 0));
            pointerRegions.Add(pauseButton.GetComponent<RectTransform>());
            context = Rect(gameplay, "Interaction", Vector2.zero, new Vector2(config.contextWidth, config.contextHeight), new Vector2(.5f, .5f));
            context.pivot = new Vector2(.5f, 1);
            Image contextBackground = context.gameObject.AddComponent<Image>();
            contextBackground.color = config.panel;
            contextBackground.raycastTarget = false;
            targetText = Label(context, "Target", "", new Vector2(config.gap, -config.gap),
                new Vector2(config.contextWidth - config.gap * 2, config.lineHeight), config.emphasisFontSize, config.emphasisFont);
            actionText = Label(context, "Action", "", new Vector2(config.gap, -config.lineHeight - config.gap),
                new Vector2(config.contextWidth - config.gap * 2, config.lineHeight), config.smallFontSize);
            gatherFill = Bar(context, "Gather progress", new Vector2(config.gap, -config.contextHeight + config.gap * 2),
                new Vector2(config.contextWidth - config.lineHeight * 3, config.progressHeight));
            progressText = Label(context, "Gather hits", "", new Vector2(config.contextWidth - config.lineHeight * 2.5f, -config.contextHeight + config.lineHeight),
                new Vector2(config.lineHeight * 2, config.lineHeight), config.smallFontSize);
            BuildPalette();
            notification = Label(gameplay, "Resource gained", "", new Vector2(config.margin, config.notificationBottom),
                new Vector2(config.healthWidth, config.lineHeight + config.gap), config.emphasisFontSize, config.emphasisFont, new Vector2(0, 0));
            notification.color = config.accent;
            BuildMenus();
        }

        private void BuildPalette()
        {
            palette = Panel(gameplay, "Build palette", new Vector2(0, config.paletteBottom),
                new Vector2(config.paletteWidth, config.paletteHeight), new Vector2(.5f, 0));
            pointerRegions.Add(palette);
            buildDescription = Label(palette, "Structure details", "", new Vector2(config.margin, -config.margin * .5f),
                new Vector2(config.paletteWidth - config.margin * 2, config.lineHeight + config.gap), config.smallFontSize);
            RectTransform viewport = Rect(palette, "Build choices", new Vector2(config.margin, -config.headingHeight - config.gap),
                new Vector2(config.paletteWidth - config.margin * 2, config.buildCardHeight), new Vector2(0, 1));
            viewport.gameObject.AddComponent<RectMask2D>();
            Image surface = viewport.gameObject.AddComponent<Image>();
            surface.color = config.panel;
            surface.raycastTarget = true; // Scrollable palette surface.
            ScrollRect scroll = viewport.gameObject.AddComponent<ScrollRect>();
            paletteContent = Rect(viewport, "Structures", Vector2.zero, viewport.sizeDelta, new Vector2(0, 1));
            scroll.content = paletteContent;
            scroll.viewport = viewport;
            scroll.horizontal = true;
            scroll.vertical = false;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = config.rowHeight;
            buildHint = Label(palette, "Placement", "", new Vector2(config.margin, -config.paletteHeight + config.buttonHeight),
                new Vector2(config.paletteWidth - config.margin * 2, config.lineHeight + config.gap), config.smallFontSize);
        }

        private void BuildMenus()
        {
            modal = Stretch(canvasRect, "Modal overlay");
            Image veil = modal.gameObject.AddComponent<Image>();
            veil.color = config.veil;
            veil.raycastTarget = false;
            menu = Panel(modal, "Field menu", Vector2.zero, config.modalSize, new Vector2(.5f, .5f));
            inventoryPanel = Stretch(menu, "Inventory panel");
            Heading(inventoryPanel, "Pack", "Everything you are carrying");
            inventoryText = Label(inventoryPanel, "Contents", "", new Vector2(config.margin, -config.menuContentTop),
                new Vector2(config.modalSize.x - config.margin * 2, config.modalSize.y - config.menuContentTop - config.buttonHeight - config.margin * 2), config.bodyFontSize);
            inventoryText.alignment = TextAnchor.UpperLeft;
            MakeButton(inventoryPanel, "Close inventory", "Close  Tab", new Vector2(config.margin, config.margin),
                new Vector2(config.modalSize.x - config.margin * 2, config.buttonHeight), Resume, new Vector2(0, 0));
            pausePanel = Stretch(menu, "Pause panel");
            pauseSubtitle = Heading(pausePanel, "A moment of shelter", "");
            MenuButton(pausePanel, "Resume", 0, Resume);
            MenuButton(pausePanel, "Controls", 1, () => SetMode(ScreenMode.Controls));
            MenuButton(pausePanel, "Quit to desktop", 2, () => SetMode(ScreenMode.Quit));
            controlsPanel = Stretch(menu, "Controls panel");
            Heading(controlsPanel, "Survival essentials", "Mouse & keyboard");
            Text controls = Label(controlsPanel, "Bindings",
                "WASD   Move     ·     Shift   Sprint\nClick / E   Interact · Hold click to gather\nB   Build     ·     R   Rotate structure\nClick   Place     ·     Right click   Cancel\nTab   Inventory     ·     M   Map\nEsc   Back / menu",
                new Vector2(config.margin, -config.menuContentTop),
                new Vector2(config.modalSize.x - config.margin * 2, config.modalSize.y - config.menuContentTop - config.buttonHeight - config.margin * 2), config.bodyFontSize);
            controls.lineSpacing = config.controlsLineSpacing;
            controls.alignment = TextAnchor.UpperLeft;
            MakeButton(controlsPanel, "Back", "Back", new Vector2(config.margin, config.margin),
                new Vector2(config.modalSize.x - config.margin * 2, config.buttonHeight), () => SetMode(ScreenMode.Pause), new Vector2(0, 0));
            quitPanel = Stretch(menu, "Quit confirmation");
            Heading(quitPanel, "Leave the island?", "This session will end. Progress is not saved.");
            MenuButton(quitPanel, "Stay here", 0, Back);
            MenuButton(quitPanel, "Quit to desktop", 1, () => Application.Quit());
            mapPanel = Stretch(modal, "Map controls");
            Label(mapPanel, "Map heading", "Island chart", new Vector2(config.margin, -config.margin),
                new Vector2(config.menuWidth, config.buttonHeight), config.headingFontSize, config.headingFont);
            MakeButton(mapPanel, "Close map", "Close map  M", new Vector2(-config.margin, -config.margin),
                new Vector2(config.mapCloseButtonWidth, config.buttonHeight), Resume, new Vector2(1, 1));
            BuildLobby();
            unavailablePanel = Panel(modal, "Survivor unavailable", Vector2.zero, config.modalSize, new Vector2(.5f, .5f));
            Heading(unavailablePanel, "Beyond the tree line", "Session status");
            unavailableText = Label(unavailablePanel, "Connection status", "", new Vector2(config.margin, -config.menuContentTop),
                new Vector2(config.modalSize.x - config.margin * 2, config.rowHeight * 3), config.bodyFontSize);
            unavailableText.alignment = TextAnchor.UpperLeft;
            MenuButton(unavailablePanel, "Menu", 3, () => SetMode(ScreenMode.Pause));
        }

        private void BuildLobby()
        {
            lobbyPanel = Stretch(modal, "Start screen");
            RectTransform card = Rect(lobbyPanel, "Island arrival", Vector2.zero, new Vector2(config.modalSize.x, config.mainMenuHeight), new Vector2(.5f, .5f));
            lobbyCard = card;
            Label(card, "Game title", "Jurassic Park", new Vector2(config.margin, 0),
                new Vector2(config.modalSize.x - config.margin * 2, config.titleFontSize + config.margin), config.titleFontSize, config.headingFont);
            Text subtitle = Label(card, "Arrival", "Stay alive. Find a way off the island.", new Vector2(config.margin, -config.titleFontSize - config.margin - config.gap),
                new Vector2(config.modalSize.x - config.margin * 2, config.lineHeight * 1.5f), config.bodyFontSize);
            subtitle.color = config.muted;
            lobbyMainChoices = Stretch(card, "Arrival choices");
            MenuButton(lobbyMainChoices, "Play solo", 0, () => { if (lobby != null) { lobby.StartOffline(); RefreshVisibility(); } });
            MenuButton(lobbyMainChoices, "Multiplayer (LAN)", 1, () => ShowLan(true));
            MenuButton(lobbyMainChoices, "Quit to desktop", 2, () => SetMode(ScreenMode.Quit));
            lobbyLanChoices = Stretch(card, "Local network");
            MenuButton(lobbyLanChoices, "Host LAN", 0, () => { if (lobby != null) { lobby.Host(); RefreshVisibility(); } });
            Text addressCaption = Label(lobbyLanChoices, "LAN address label", "Server IPv4 address · optional :port",
                new Vector2(config.margin, -config.menuContentTop - config.buttonHeight - config.gap),
                new Vector2(config.modalSize.x - config.margin * 2, config.lineHeight), config.smallFontSize);
            addressCaption.color = config.muted;
            RectTransform field = Panel(lobbyLanChoices, "LAN address", new Vector2(config.margin, -config.menuContentTop - config.buttonHeight - config.gap - config.margin),
                new Vector2(config.modalSize.x - config.margin * 2, config.buttonHeight), new Vector2(0, 1));
            Image background = field.GetComponent<Image>();
            background.raycastTarget = true;
            address = field.gameObject.AddComponent<InputField>();
            Text addressText = Label(field, "Address value", "", new Vector2(config.gap, 0),
                new Vector2(field.sizeDelta.x - config.gap * 2, config.buttonHeight), config.bodyFontSize);
            address.textComponent = addressText;
            address.targetGraphic = background;
            address.text = "127.0.0.1";
            address.characterLimit = 64;
            address.lineType = InputField.LineType.SingleLine;
            address.navigation = new Navigation { mode = Navigation.Mode.None };
            MakeButton(lobbyLanChoices, "Join LAN", "Join LAN", new Vector2(config.margin, field.anchoredPosition.y - config.buttonHeight - config.gap),
                new Vector2(config.modalSize.x - config.margin * 2, config.buttonHeight), () => { if (lobby != null) { lobby.Join(address.text); RefreshVisibility(); } });
            lobbyStatus = Label(lobbyLanChoices, "Lobby status", "", new Vector2(config.margin, field.anchoredPosition.y - config.buttonHeight * 2 - config.gap - config.margin),
                new Vector2(config.modalSize.x - config.margin * 2, config.lineHeight * 4), config.bodyFontSize);
            lobbyStatus.color = config.accent;
            lobbyStatus.alignment = TextAnchor.UpperLeft;
            MakeButton(lobbyLanChoices, "Back to start", "Back", new Vector2(config.margin, -config.lobbyHeight + config.buttonHeight + config.margin * 2),
                new Vector2(config.modalSize.x - config.margin * 2, config.buttonHeight), () => ShowLan(false));
        }

        private Text Heading(RectTransform parent, string title, string subtitle)
        {
            Label(parent, "Heading", title, new Vector2(config.margin, -config.margin),
                new Vector2(config.modalSize.x - config.margin * 2, config.headingHeight), config.headingFontSize, config.headingFont);
            Text label = Label(parent, "Subtitle", subtitle, new Vector2(config.margin, -config.margin - config.headingHeight - config.gap),
                new Vector2(config.modalSize.x - config.margin * 2, config.headingHeight), config.smallFontSize);
            label.color = config.muted;
            return label;
        }

        private void MenuButton(RectTransform parent, string label, int row, UnityEngine.Events.UnityAction action) =>
            MakeButton(parent, label, label, new Vector2(config.margin, -config.menuContentTop - row * (config.buttonHeight + config.gap)),
                new Vector2(config.modalSize.x - config.margin * 2, config.buttonHeight), action);

        private static void EnsureEventSystem()
        {
            EventSystem events = EventSystem.current;
            if (events == null) events = FindFirstObjectByType<EventSystem>();
            if (events == null) events = new GameObject("UI EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
            events.enabled = true;
            foreach (BaseInputModule old in events.GetComponents<BaseInputModule>())
                if (!(old is InputSystemUIInputModule)) old.enabled = false;
            InputSystemUIInputModule module = events.GetComponent<InputSystemUIInputModule>();
            if (module == null) module = events.gameObject.AddComponent<InputSystemUIInputModule>();
            module.enabled = true;
            if (module.point == null || module.leftClick == null) module.AssignDefaultActions();
            // Gameplay shortcuts must never double as UI submit/navigation actions.
            module.move = null;
            module.submit = null;
            module.cancel = null;
            events.sendNavigationEvents = false;
        }

        private RectTransform Stretch(Transform parent, string name)
        {
            RectTransform rect = Rect(parent, name, Vector2.zero, Vector2.zero, Vector2.zero);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static RectTransform Rect(Transform parent, string name, Vector2 position, Vector2 size, Vector2 anchor)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private RectTransform Panel(Transform parent, string name, Vector2 position, Vector2 size, Vector2 anchor)
        {
            RectTransform rect = Rect(parent, name, position, size, anchor);
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = config.panel;
            image.raycastTarget = false;
            return rect;
        }

        private Text Label(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize,
            Font font = null, Vector2? anchor = null)
        {
            RectTransform rect = Rect(parent, name, position, size, anchor ?? new Vector2(0, 1));
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = font != null ? font : config.bodyFont;
            label.fontSize = fontSize;
            label.color = config.text;
            label.text = value;
            label.supportRichText = false;
            label.raycastTarget = false;
            label.alignment = TextAnchor.MiddleLeft;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            Shadow shadow = rect.gameObject.AddComponent<Shadow>();
            shadow.effectColor = config.textShadow;
            shadow.effectDistance = new Vector2(1f, -1f);
            return label;
        }

        private Button MakeButton(Transform parent, string name, string label, Vector2 position, Vector2 size,
            UnityEngine.Events.UnityAction action, Vector2? anchor = null)
        {
            RectTransform rect = Rect(parent, name, position, size, anchor ?? new Vector2(0, 1));
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = Color.white;
            image.raycastTarget = true;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            ColorBlock colors = button.colors;
            colors.normalColor = config.button;
            colors.highlightedColor = config.buttonHover;
            colors.pressedColor = config.buttonPressed;
            colors.selectedColor = config.buttonHover;
            colors.disabledColor = config.buttonDisabled;
            colors.fadeDuration = config.transitionDuration;
            button.colors = colors;
            button.onClick.AddListener(action);
            Text title = Label(rect, "Label", label, new Vector2(config.gap, 0),
                new Vector2(size.x - config.gap * 2, size.y), config.bodyFontSize, config.emphasisFont);
            title.alignment = TextAnchor.MiddleCenter;
            return button;
        }

        private Image Bar(Transform parent, string name, Vector2 position, Vector2 size)
        {
            RectTransform track = Panel(parent, name, position, size, new Vector2(0, 1));
            track.GetComponent<Image>().color = config.track;
            RectTransform fill = Stretch(track, "Fill");
            Image image = fill.gameObject.AddComponent<Image>();
            image.color = config.healthy;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.raycastTarget = false;
            return image;
        }
    }
}
