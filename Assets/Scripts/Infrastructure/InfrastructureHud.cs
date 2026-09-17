using System.Text;
using JurassicPark.Core;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace JurassicPark.Infrastructure
{
    [RequireComponent(typeof(InfrastructureSession))]
    public sealed class InfrastructureHud : MonoBehaviour, IWorldInputBlocker
    {
        private InfrastructureSession session;
        private Canvas canvas;
        private RectTransform header;
        private RectTransform footer;
        private RectTransform minimap;
        private RectTransform drag;
        private TextMeshProUGUI inspector;
        private TextMeshProUGUI state;
        private TextMeshProUGUI feedback;
        private UnityEngine.UI.RawImage mapImage;
        private Texture2D mapTexture;
        private Color32[] terrainPixels;
        private Color32[] mapPixels;
        private float nextRefresh;
        private readonly Color panel = new Color(.055f, .085f, .08f, .96f);

        public string PlacementMessage { get; set; } = "";
        public bool BlocksWorldInput => false;

        private void OnEnable() => WorldInputBlockers.Register(this);
        private void OnDisable() => WorldInputBlockers.Unregister(this);

        private void Start()
        {
            session = GetComponent<InfrastructureSession>();
            if (session.Config.font == null) throw new System.InvalidOperationException("Build the persistent infrastructure font before running the scene.");
            var root = new GameObject("InfrastructureHUD", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = root.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 800);
            scaler.matchWidthOrHeight = .5f;

            header = Panel(root.transform, "Header", new Vector2(0, 1), Vector2.zero, new Vector2(0, 70), true);
            Label(header, "Title", "JURASSIC PARK  /  SYSTEMS FIELD TEST", new Vector2(20, -10), new Vector2(730, 27), 22);
            state = Label(header, "WorldState", "", new Vector2(20, -39), new Vector2(1220, 24), 15);
            footer = Panel(root.transform, "CommandDeck", new Vector2(0, 0), Vector2.zero, new Vector2(0, 220), true);
            minimap = Rect(footer, "Minimap", new Vector2(0, 1), new Vector2(14, -14), new Vector2(174, 174));
            mapImage = minimap.gameObject.AddComponent<UnityEngine.UI.RawImage>();
            mapImage.raycastTarget = true;
            Label(footer, "MapHint", "12 CAMPS / CLICK TO PAN", new Vector2(14, -192), new Vector2(200, 20), 12);
            inspector = Label(footer, "SelectedEntity", "", new Vector2(210, -14), new Vector2(590, 118), 16);
            feedback = Label(footer, "Feedback", "", new Vector2(210, -141), new Vector2(1000, 66), 15);

            Button(footer, "Haul source", new Vector2(-388, -15), () => session.OrderSelection(InfraCommandKind.Gather, default, session.MainSourceId));
            Button(footer, "Build wall [B]", new Vector2(-260, -15), session.BeginWallPlacement);
            Button(footer, "Stop [X]", new Vector2(-132, -15), () => session.OrderSelection(InfraCommandKind.Stop, default));
            Button(footer, "Spawn raid [R]", new Vector2(-388, -53), () => session.SpawnRaid());
            Button(footer, "Drop supplies", new Vector2(-260, -53), session.DropSupply);
            Button(footer, "Cancel site", new Vector2(-132, -53), session.CancelSelectedBlueprint);
            Button(footer, "Reset + run check [F6]", new Vector2(-388, -91), session.BeginDemonstration, 376);
            Label(root.transform, "Controls", "LMB / drag: select   RMB: order   WASD: camera   Wheel: zoom   Space: focus   Home: map   Esc: pause",
                new Vector2(20, -80), new Vector2(1200, 24), 15);

            drag = Rect(root.transform, "DragSelection", Vector2.zero, Vector2.zero, Vector2.one);
            var dragImage = drag.gameObject.AddComponent<UnityEngine.UI.Image>();
            dragImage.color = new Color(.45f, .9f, .8f, .2f);
            dragImage.raycastTarget = false;
            drag.gameObject.SetActive(false);
            if (FindFirstObjectByType<EventSystem>() != null)
                throw new System.InvalidOperationException("Infrastructure must not be loaded alongside another gameplay EventSystem.");
            var events = new GameObject("InfrastructureEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.transform.SetParent(transform, false);
            var input = events.GetComponent<InputSystemUIInputModule>();
            input.AssignDefaultActions();
            input.move = null;
            input.submit = null;
            input.cancel = null;
            events.GetComponent<EventSystem>().sendNavigationEvents = false;
            session.WorldReset += RebuildMap;
            if (session.World != null) RebuildMap();
        }

        private void OnDestroy()
        {
            if (session != null) session.WorldReset -= RebuildMap;
            if (mapTexture != null) Destroy(mapTexture);
        }

        public bool BlocksPointer(Vector2 screenPosition) =>
            (header != null && RectTransformUtility.RectangleContainsScreenPoint(header, screenPosition)) ||
            (footer != null && RectTransformUtility.RectangleContainsScreenPoint(footer, screenPosition));

        public bool TryMinimapPoint(Vector2 screen, out Vector2 world)
        {
            world = default;
            if (minimap == null || !RectTransformUtility.RectangleContainsScreenPoint(minimap, screen) ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(minimap, screen, null, out Vector2 local)) return false;
            Rect rect = minimap.rect;
            float x = Mathf.InverseLerp(rect.xMin, rect.xMax, local.x);
            float y = Mathf.InverseLerp(rect.yMin, rect.yMax, local.y);
            world = new Vector2((x - .5f) * session.Layout.Map.Width * session.Layout.Map.CellSize,
                (y - .5f) * session.Layout.Map.Height * session.Layout.Map.CellSize);
            return true;
        }

        public void ShowSelectionRectangle(Vector2 start, Vector2 end, bool visible)
        {
            if (drag == null) return;
            drag.gameObject.SetActive(visible);
            if (!visible) return;
            float scale = canvas.scaleFactor;
            drag.anchoredPosition = Vector2.Min(start, end) / scale;
            drag.sizeDelta = new Vector2(Mathf.Abs(end.x - start.x), Mathf.Abs(end.y - start.y)) / scale;
        }

        private void RebuildMap()
        {
            if (mapTexture != null) Destroy(mapTexture);
            InfraMap map = session.Layout.Map;
            mapTexture = new Texture2D(map.Width, map.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            mapTexture.name = "LiveInfrastructureMinimap";
            terrainPixels = new Color32[map.Width * map.Height];
            mapPixels = new Color32[terrainPixels.Length];
            for (int i = 0; i < terrainPixels.Length; i++)
                terrainPixels[i] = session.Config.terrainColors[(int)session.Layout.Surfaces[i]];
            foreach (Vector2Int camp in map.Camps) Dot(terrainPixels, camp, Color.white, 1);
            mapImage.texture = mapTexture;
        }

        private void Update()
        {
            if (session == null || session.World == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .15f;
            InfraWorld world = session.World;
            state.text = $"OFFLINE  |  {session.Config.map.mapVersion}  |  FULL VISIBILITY (debug)  |  t={world.Time:0.0}s  |  " +
                $"physical {world.TotalPhysicalMaterials} + consumed {world.ConsumedMaterials} = {world.InitialMaterials}  |  " +
                $"spatial r{world.Revision}" + (session.Paused ? "  |  PAUSED" : "");
            if (world.Entities.TryGetValue(session.SelectedId, out InfraEntity entity))
            {
                inspector.text = $"{entity.Kind} #{entity.Id}  /  owner {entity.Owner}  /  selected {session.Selection.Count}\n" +
                    $"HP {entity.Health:0}   Stored {entity.Stored}   Claims {entity.OutgoingReserved}/{entity.IncomingReserved} out/in   " +
                    $"Carried {entity.Carried}/{session.Config.rules.CarryCapacity}   Delivered {entity.Delivered}\n" +
                    $"Task: {entity.TaskStatus}  /  {entity.Action}  /  target #{entity.TargetId}\n{entity.Reason}";
                if (entity.Kind == InfraEntityKind.Blueprint)
                    inspector.text += $"  Materials {entity.Delivered}/{entity.RequiredMaterials}, work {entity.BuildProgress:0.0}/{session.Config.rules.BuildSeconds:0.0}s";
            }
            else inspector.text = "No selection\nSelect a survivor, container, resource, or construction site.\nCommands replace the current task; Stop keeps carried material.";
            var text = new StringBuilder();
            text.Append(string.IsNullOrEmpty(PlacementMessage) ? session.Feedback : PlacementMessage);
            text.Append("\nCheck: ").Append(session.ScenarioPhase).Append("  ").Append(session.ScenarioEvidence);
            if (world.Events.Count > 0)
            {
                InfraEvent latest = world.Events[world.Events.Count - 1];
                text.Append("\n").Append(latest.Time.ToString("0.0")).Append("s  #").Append(latest.EntityId).Append("  ").Append(latest.Message);
            }
            feedback.text = text.ToString();
            if (terrainPixels == null) RebuildMap();
            System.Array.Copy(terrainPixels, mapPixels, terrainPixels.Length);
            foreach (InfraEntity value in world.Entities.Values)
            {
                if (value.Health <= 0) continue;
                Color color = value.Kind == InfraEntityKind.Dinosaur ? session.Config.dinosaurColor :
                    value.Kind == InfraEntityKind.Worker ? session.Config.workerColor : new Color(.9f, .72f, .3f);
                Dot(mapPixels, world.Map.WorldToCell(value.Position), color, 1);
            }
            mapTexture.SetPixels32(mapPixels);
            mapTexture.Apply(false);
        }

        private void Dot(Color32[] pixels, Vector2Int cell, Color color, int radius)
        {
            InfraMap map = session.Layout.Map;
            for (int y = -radius; y <= radius; y++)
            for (int x = -radius; x <= radius; x++)
            {
                Vector2Int point = cell + new Vector2Int(x, y);
                if (map.Contains(point)) pixels[point.x + point.y * map.Width] = color;
            }
        }

        private RectTransform Rect(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = anchor;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private RectTransform Panel(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, bool stretch)
        {
            RectTransform rect = Rect(parent, name, anchor, position, size);
            if (stretch) rect.anchorMax = new Vector2(1, anchor.y);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = panel;
            image.raycastTarget = true;
            return rect;
        }

        private TextMeshProUGUI Label(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize)
        {
            RectTransform rect = Rect(parent, name, new Vector2(0, 1), position, size);
            var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
            label.font = session.Config.font;
            label.fontSize = fontSize;
            label.enableAutoSizing = false;
            label.richText = false;
            label.color = new Color(.91f, .94f, .88f);
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;
            label.text = value;
            return label;
        }

        private void Button(Transform parent, string title, Vector2 position, UnityEngine.Events.UnityAction action, float width = 120)
        {
            RectTransform rect = Rect(parent, title, new Vector2(1, 1), position, new Vector2(width, 30));
            rect.pivot = new Vector2(0, 1);
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = new Color(.16f, .26f, .23f);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            button.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
            button.onClick.AddListener(action);
            TextMeshProUGUI label = Label(rect, "Label", title, new Vector2(4, -5), new Vector2(width - 8, 24), 15);
            label.alignment = TextAlignmentOptions.Top;
        }
    }
}
