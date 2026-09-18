using System.Collections.Generic;
using System.Text;
using JurassicPark.Simulation;
using UnityEngine;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// The diagnostic HUD of milestone M2, drawn with IMGUI: a resource bar across the top, a selection panel bottom left, a
    /// command card bottom right and a minimap between them. It reads <see cref="MatchReadModel"/> and sends only through
    /// <see cref="SelectionController"/> and <see cref="GameSession.Commands"/>, so it runs unchanged on a client that has no
    /// simulation. Strings and the minimap texture are rebuilt at most once per tick, never per repaint. Issue #118 replaces it
    /// with the art HUD; until then this is what makes the match readable.
    /// </summary>
    public sealed class HudOverlay : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private SelectionController selection;
        [SerializeField] private RtsCamera rtsCamera;
        [SerializeField] private Camera viewCamera;
        [Tooltip("Width of the minimap on screen in pixels. Its height follows the map's shape.")]
        [SerializeField] private float minimapWidth = 208f;
        [SerializeField] private float panelWidth = 380f;
        [SerializeField] private float cardWidth = 300f;
        [Tooltip("Rows of the selection listed one by one before it is summed up by kind.")]
        [SerializeField] private int listedRows = 6;

        private const float BarHeight = 26f;
        private const float Margin = 8f;
        private const float ButtonHeight = 24f;

        private readonly StringBuilder text = new StringBuilder(256);
        private readonly List<HudButton> buttons = new List<HudButton>(8);
        private readonly List<string> labels = new List<string>(8);   // built with the buttons, once per tick, never per event
        private readonly List<HudModel.DefinitionCount> counts = new List<HudModel.DefinitionCount>(8);
        private readonly MinimapPainter painter = new MinimapPainter();

        private Texture2D pixel;
        private Texture2D minimap;
        private GUIStyle barStyle;
        private GUIStyle panelStyle;
        private GUIStyle buttonStyle;
        private string barText = string.Empty;
        private string hintText = string.Empty;
        private string panelText = string.Empty;
        private string alertText = string.Empty;
        private CommandRejection shownRejection;
        private long builtAtTick = -1;
        private int builtForVersion = -1;
        private int builtForPlacing = -2;
        private Rect barRect, panelRect, cardRect, minimapRect;

        public void Configure(GameSession gameSession, SelectionController controller, RtsCamera camera, Camera view)
        {
            session = gameSession;
            selection = controller;
            rtsCamera = camera;
            viewCamera = view;
        }

        /// <summary>The minimap in screen coordinates, y up from the bottom, as the mouse reports them.</summary>
        public Rect MinimapScreenRect
        {
            get
            {
                Layout();
                return new Rect(minimapRect.x, Screen.height - minimapRect.yMax, minimapRect.width, minimapRect.height);
            }
        }

        /// <summary>The ground point a minimap press means, or false when the press was not on the minimap.</summary>
        public bool TryMinimapWorldPoint(Vector2 screenPoint, out Vector3 world)
        {
            world = default;
            MapDefinition map = session != null && session.Model != null ? session.Model.Map : null;
            Rect rect = MinimapScreenRect;
            if (map == null || rect.width <= 0f || !rect.Contains(screenPoint)) return false;
            world = new Vector3(
                (screenPoint.x - rect.xMin) / rect.width * map.Width * map.CellSize,
                0f,
                (screenPoint.y - rect.yMin) / rect.height * map.Height * map.CellSize);
            return true;
        }

        /// <summary>Moves the camera to what was pressed on the minimap. Returns false when the press was somewhere else.</summary>
        public bool ClickMinimap(Vector2 screenPoint)
        {
            if (rtsCamera == null || !TryMinimapWorldPoint(screenPoint, out Vector3 world)) return false;
            rtsCamera.LookAt(world);
            return true;
        }

        /// <summary>True where the HUD covers the screen, so a press there is the HUD's and not an order to the world.</summary>
        public bool IsOverHud(Vector2 screenPoint)
        {
            Layout();
            var point = new Vector2(screenPoint.x, Screen.height - screenPoint.y);
            return barRect.Contains(point) || panelRect.Contains(point) || cardRect.Contains(point) || minimapRect.Contains(point);
        }

        private void OnEnable()
        {
            if (selection != null) selection.PointerOverHud = IsOverHud;
        }

        private void OnDisable()
        {
            if (selection != null) selection.PointerOverHud = null;
        }

        private void Layout()
        {
            float minimapHeight = minimapWidth * 0.66f;
            MapDefinition map = session != null && session.Model != null ? session.Model.Map : null;
            if (map != null) minimapHeight = minimapWidth * map.Height / Mathf.Max(1, map.Width);
            barRect = new Rect(0f, 0f, Screen.width, BarHeight);
            float panelHeight = 44f + listedRows * 16f;
            panelRect = new Rect(Margin, Screen.height - Margin - panelHeight, panelWidth, panelHeight);
            minimapRect = new Rect((Screen.width - minimapWidth) * 0.5f, Screen.height - Margin - minimapHeight, minimapWidth, minimapHeight);
            // No buttons, no card: an empty card must not swallow clicks in the corner.
            float cardHeight = buttons.Count == 0 ? 0f : Margin * 2f + buttons.Count * (ButtonHeight + 2f);
            cardRect = buttons.Count == 0 ? Rect.zero : new Rect(Screen.width - Margin - cardWidth, Screen.height - Margin - cardHeight, cardWidth, cardHeight);
        }

        private void OnGUI()
        {
            if (session == null || selection == null) return;
            MatchReadModel model = session.Model;
            EnsureTextures();
            EnsureStyles();
            Rebuild(model);
            Layout();

            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && minimapRect.Contains(current.mousePosition))
            {
                ClickMinimap(new Vector2(current.mousePosition.x, Screen.height - current.mousePosition.y));
                current.Use();
                return;
            }

            if (current.type == EventType.Repaint)
            {
                Fill(barRect, new Color(0.05f, 0.06f, 0.07f, 0.82f));
                GUI.Label(new Rect(barRect.x + 10f, barRect.y + 4f, barRect.width - 20f, BarHeight), barText, barStyle);
                if (model != null)
                {
                    Fill(panelRect, new Color(0.05f, 0.06f, 0.07f, 0.78f));
                    GUI.Label(new Rect(panelRect.x + 8f, panelRect.y + 6f, panelRect.width - 16f, panelRect.height - 12f), panelText, panelStyle);
                    DrawMinimap(model);
                }
                if (alertText.Length > 0)
                    GUI.Label(new Rect(panelRect.x + 8f, panelRect.y - 20f, panelWidth, 18f), alertText, panelStyle);
                GUI.Label(new Rect(Margin, barRect.yMax + 4f, Screen.width - Margin * 2f, 18f), hintText, panelStyle);
            }
            if (model != null) DrawCard();
        }

        /// <summary>Everything the HUD says, built once per tick: the strings and the buttons, not the drawing.</summary>
        private void Rebuild(MatchReadModel model)
        {
            if (session.Failure != null)
            {
                barText = session.Failure;
                hintText = panelText = alertText = string.Empty;
                buttons.Clear();
                return;
            }
            if (model == null || session.Commands == null)
            {
                barText = model == null ? "waiting for a match" : "connected, waiting for a seat";
                hintText = panelText = alertText = string.Empty;
                buttons.Clear();
                return;
            }
            if (model.Tick == builtAtTick && selection.Version == builtForVersion && selection.PlacingIndex == builtForPlacing) return;
            builtAtTick = model.Tick;
            builtForVersion = selection.Version;
            builtForPlacing = selection.PlacingIndex;

            MatchSnapshot match = model.Match;
            HudModel.Stores stores = HudModel.StoresOf(model);
            text.Clear();
            text.Append("wood ").Append(stores.Total)
                .Append("  (own ").Append(stores.Own).Append("  allied ").Append(stores.Allied).Append("  carried ").Append(stores.Carried).Append(')')
                .Append("   |   ").Append(match.Phase).Append("  ");
            HudModel.AppendClock(text, match.SecondsLeft);
            text.Append(" left  ");
            HudModel.AppendTimeOfDay(text, match.TimeOfDay);
            MatchRulesAsset rules = session.MatchRules;
            if (rules != null) text.Append(HudModel.IsNight(match.TimeOfDay, rules.nightStartsAt, rules.nightEndsAt) ? " night" : " day");
            if (match.BoardedByLocal > 0) text.Append("  boarded ").Append(match.BoardedByLocal);
            if (match.LocalOutcome != SeatOutcome.Undecided) text.Append("  ").Append(match.LocalOutcome.ToString().ToUpperInvariant());
            text.Append("   |   ").Append(session.Role).Append("  seat ").Append(model.LocalSeat.Value).Append("  tick ").Append(model.Tick);
            barText = text.ToString();

            text.Clear();
            text.Append("LMB select  drag box  RMB order  Shift queue  X stop  B wall  G gate  Del demolish  WASD pan  wheel zoom  minimap click centres");
            if (selection.PlacingIndex >= 0)
                text.Append("\nplacing ").Append(model.Catalog.entries[selection.PlacingIndex].id).Append(" at ").Append(selection.PlacingCell).Append("  (LMB place, Shift keeps placing, RMB or Esc cancel)");
            hintText = text.ToString();

            if (selection.LastRejection != shownRejection)
            {
                shownRejection = selection.LastRejection;
                if (shownRejection == CommandRejection.None) alertText = string.Empty;
                else
                {
                    text.Clear();
                    text.Append("last order refused: ").Append(shownRejection).Append("  (tick ").Append(model.Tick).Append(')');
                    alertText = text.ToString();
                }
            }

            text.Clear();
            IReadOnlyList<EntityId> selected = selection.Selection;
            text.Append("selection ").Append(selected.Count);
            HudModel.CountByDefinition(model, selected, counts);
            for (int i = 0; i < counts.Count; i++) text.Append("   ").Append(counts[i].Id).Append(" x").Append(counts[i].Count);
            for (int i = 0; i < selected.Count && i < listedRows; i++)
            {
                if (!model.TryGet(selected[i], out EntitySnapshot entity)) continue;
                text.Append('\n');
                HudModel.AppendSelected(text, model, entity);
            }
            if (selected.Count > listedRows) text.Append("\n... ").Append(selected.Count - listedRows).Append(" more");
            panelText = text.ToString();

            CommandCard.Build(model, selected, selection.PlacingIndex, buttons, text);
            labels.Clear();
            for (int i = 0; i < buttons.Count; i++) labels.Add(LabelOf(buttons[i]));
        }

        private void DrawCard()
        {
            if (buttons.Count == 0) return;
            if (Event.current.type == EventType.Repaint) Fill(cardRect, new Color(0.05f, 0.06f, 0.07f, 0.78f));
            float y = cardRect.y + Margin;
            for (int i = 0; i < buttons.Count; i++)
            {
                HudButton button = buttons[i];
                var rect = new Rect(cardRect.x + Margin, y, cardRect.width - Margin * 2f, ButtonHeight);
                y += ButtonHeight + 2f;
                GUI.enabled = button.Enabled;
                bool pressed = GUI.Button(rect, labels[i], buttonStyle);
                GUI.enabled = true;
                if (pressed) Invoke(button);
            }
        }

        /// <summary>The button's own text, with the hotkey and, when it is greyed, the reason it would be refused. Called from Rebuild only.</summary>
        private string LabelOf(in HudButton button)
        {
            text.Clear();
            text.Append(button.Label);
            if (button.Hotkey.Length > 0) text.Append(" (").Append(button.Hotkey).Append(')');
            if (!button.Enabled && button.Reason.Length > 0) text.Append("  -  ").Append(button.Reason);
            return text.ToString();
        }

        /// <summary>Every button goes out the same way a key press or a click would, so the HUD adds no rules of its own.</summary>
        private void Invoke(in HudButton button)
        {
            switch (button.Action)
            {
                case HudAction.Stop:
                    selection.StopSelection();
                    break;
                case HudAction.Place:
                    selection.BeginPlacing(button.DefinitionId);
                    break;
                case HudAction.CancelPlacement:
                    selection.CancelPlacing();
                    break;
                case HudAction.ToggleGate:
                    // The resolver decides what pointing at that gate means, exactly as it does for a right click.
                    if (session.Model == null || !session.Model.TryGet(button.Target, out EntitySnapshot gate)) break;
                    OrderResolver.Order order = OrderResolver.Resolve(session.Model, selection.Selection, gate, gate.Position);
                    session.Commands.Send(order.Kind, selection.Selection, order.Point, order.Target);
                    break;
            }
        }

        private void DrawMinimap(MatchReadModel model)
        {
            MapDefinition map = model.Map;
            if (painter.NeedsPaint(map, model.Fog))
            {
                painter.Paint(map, model.Fog);
                if (minimap == null || minimap.width != painter.Width || minimap.height != painter.Height)
                {
                    if (minimap != null) Destroy(minimap);
                    minimap = new Texture2D(painter.Width, painter.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                }
                minimap.SetPixels32(painter.Pixels);
                minimap.Apply(false);
            }
            if (minimap == null) return;
            GUI.DrawTexture(minimapRect, minimap);

            float worldWidth = map.Width * map.CellSize, worldHeight = map.Height * map.CellSize;
            IReadOnlyList<EntitySnapshot> entities = model.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                EntitySnapshot entity = entities[i];
                Vector2 at = ToMinimap(entity.Position.X, entity.Position.Y, worldWidth, worldHeight);
                GUI.color = MinimapPainter.DotOf(model, entity);
                GUI.DrawTexture(new Rect(at.x - 1.5f, at.y - 1.5f, 3f, 3f), pixel);
            }
            GUI.color = Color.white;
            DrawCameraRectangle(worldWidth, worldHeight);
        }

        /// <summary>
        /// Where the camera is looking, as the box around the ground under the four screen corners. The projection is the one
        /// <see cref="SelectionController.TryGroundPoint"/> uses, so the box and a click agree on where the ground is.
        /// </summary>
        private void DrawCameraRectangle(float worldWidth, float worldHeight)
        {
            if (viewCamera == null) return;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            int found = 0;
            for (int i = 0; i < 4; i++)
            {
                var corner = new Vector2(i == 1 || i == 2 ? Screen.width : 0f, i >= 2 ? Screen.height : 0f);
                if (!selection.TryGroundPoint(corner, out SimVector2 ground)) continue;
                found++;
                minX = Mathf.Min(minX, ground.X);
                maxX = Mathf.Max(maxX, ground.X);
                minY = Mathf.Min(minY, ground.Y);
                maxY = Mathf.Max(maxY, ground.Y);
            }
            // Above the horizon the ray never meets the ground: fall back to a mark on what the camera is centred on.
            if (found < 2 && rtsCamera != null)
            {
                minX = maxX = rtsCamera.Focus.x;
                minY = maxY = rtsCamera.Focus.z;
            }
            else if (found < 2) return;
            Vector2 min = ToMinimap(Mathf.Clamp(minX, 0f, worldWidth), Mathf.Clamp(minY, 0f, worldHeight), worldWidth, worldHeight);
            Vector2 max = ToMinimap(Mathf.Clamp(maxX, 0f, worldWidth), Mathf.Clamp(maxY, 0f, worldHeight), worldWidth, worldHeight);
            var box = Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            Fill(new Rect(box.xMin, box.yMin, box.width, 1f), Color.white);
            Fill(new Rect(box.xMin, box.yMax - 1f, box.width, 1f), Color.white);
            Fill(new Rect(box.xMin, box.yMin, 1f, box.height), Color.white);
            Fill(new Rect(box.xMax - 1f, box.yMin, 1f, box.height), Color.white);
            GUI.color = Color.white;
        }

        /// <summary>A ground point in GUI coordinates on the minimap. The map's y runs up the minimap, as a map should.</summary>
        private Vector2 ToMinimap(float x, float y, float worldWidth, float worldHeight) => new Vector2(
            minimapRect.x + Mathf.Clamp01(x / worldWidth) * minimapRect.width,
            minimapRect.yMax - Mathf.Clamp01(y / worldHeight) * minimapRect.height);

        private void Fill(Rect rect, Color colour)
        {
            Color was = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(rect, pixel);
            GUI.color = was;
        }

        private void EnsureTextures()
        {
            if (pixel != null) return;
            pixel = new Texture2D(1, 1);
            pixel.SetPixel(0, 0, Color.white);
            pixel.Apply();
        }

        private void EnsureStyles()
        {
            if (barStyle != null) return;
            barStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, richText = false };
            barStyle.normal.textColor = new Color(0.92f, 0.94f, 0.9f);
            panelStyle = new GUIStyle(barStyle) { alignment = TextAnchor.UpperLeft, wordWrap = false };
            buttonStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft, fontSize = 11 };
        }

        private void OnDestroy()
        {
            if (pixel != null) Destroy(pixel);
            if (minimap != null) Destroy(minimap);
        }
    }
}
