using System.Collections.Generic;
using JurassicPark.Simulation;
using UnityEngine;
using UnityEngine.InputSystem;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// Left click or drag selects the local seat's units, right click orders them (what the click means is OrderResolver's job),
    /// X stops them, holding Shift queues. It only ever sends commands; whether they are accepted is the authority's call.
    /// </summary>
    public sealed class SelectionController : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private EntityViewRegistry views;
        [SerializeField] private Camera viewCamera;
        [Tooltip("Extra pixels around a thing's drawn silhouette that still count as clicking it.")]
        [SerializeField] private float pickSlackPixels = 6f;
        [Tooltip("Pixels the cursor must travel before a press becomes a drag.")]
        [SerializeField] private float dragThreshold = 6f;

        private readonly List<EntityId> selection = new List<EntityId>();
        private readonly List<EntityId> scratch = new List<EntityId>();
        private Vector2 pressedAt;
        private bool pressing;

        public IReadOnlyList<EntityId> Selection => selection;

        /// <summary>Catalog index of the building being placed, or -1 when not placing.</summary>
        public int PlacingIndex { get; private set; } = -1;

        /// <summary>Cell under the cursor while placing.</summary>
        public Cell PlacingCell { get; private set; }
        public bool IsDragging { get; private set; }
        public Rect DragRect { get; private set; }

        /// <summary>Changes whenever the selection or the last answer does, so a readout knows when to rebuild.</summary>
        public int Version { get; private set; }

        /// <summary>The last order's rejection, for the HUD. None when it was accepted.</summary>
        public CommandRejection LastRejection { get; private set; }

        /// <summary>
        /// Where the HUD covers the screen, set by it. A press there belongs to the HUD, so it neither selects, orders nor
        /// places: clicking a button or the minimap must not also send the world an order behind it.
        /// </summary>
        public System.Predicate<Vector2> PointerOverHud { get; set; }

        public void Configure(GameSession gameSession, EntityViewRegistry registry, Camera camera)
        {
            session = gameSession;
            views = registry;
            viewCamera = camera;
        }

        private MatchReadModel model;

        private void OnEnable()
        {
            session.MatchBegan += Hook;
            session.CommandAnswered += OnAnswer;
            if (session.Model != null) Hook();
        }

        private void OnDisable()
        {
            session.MatchBegan -= Hook;
            session.CommandAnswered -= OnAnswer;
            if (model != null) model.EntityVanished -= OnVanished;
            model = null;
        }

        private void Hook()
        {
            if (model != null) return;
            model = session.Model;
            model.EntityVanished += OnVanished;
        }

        private void OnVanished(EntityId id)
        {
            if (selection.Remove(id)) Version++;
        }

        private void OnAnswer(CommandResolved answer)
        {
            if (answer.IsRepeat) return;
            LastRejection = answer.Rejection;
            Version++;
        }

        private void Update()
        {
            if (!session.IsReady) return;
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (mouse == null) return;
            Vector2 cursor = mouse.position.ReadValue();
            bool shift = keyboard != null && keyboard.shiftKey.isPressed;
            bool overHud = PointerOverHud != null && PointerOverHud(cursor);

            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) CancelPlacing();
                if (keyboard.bKey.wasPressedThisFrame) BeginPlacing("wall");
                if (keyboard.gKey.wasPressedThisFrame) BeginPlacing("gate");
                if ((keyboard.deleteKey.wasPressedThisFrame || keyboard.backspaceKey.wasPressedThisFrame) && !overHud) DemolishAt(cursor);
            }
            if (PlacingIndex >= 0)
            {
                if (TryGroundPoint(cursor, out SimVector2 aim)) PlacingCell = CellOf(aim);
                if (mouse.leftButton.wasPressedThisFrame && !overHud) PlaceAt(cursor, shift);
                if (mouse.rightButton.wasPressedThisFrame) CancelPlacing();
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame && !overHud)
            {
                pressing = true;
                pressedAt = cursor;
            }
            if (pressing)
            {
                IsDragging = (cursor - pressedAt).sqrMagnitude >= dragThreshold * dragThreshold;
                DragRect = Rect.MinMaxRect(Mathf.Min(pressedAt.x, cursor.x), Mathf.Min(pressedAt.y, cursor.y), Mathf.Max(pressedAt.x, cursor.x), Mathf.Max(pressedAt.y, cursor.y));
            }
            if (pressing && mouse.leftButton.wasReleasedThisFrame)
            {
                if (IsDragging) BoxSelect(DragRect, shift);
                else ClickSelect(cursor, shift);
                pressing = false;
                IsDragging = false;
            }
            if (mouse.rightButton.wasPressedThisFrame && !overHud) OrderAt(cursor, shift);
            if (keyboard != null && keyboard.xKey.wasPressedThisFrame) StopSelection();
        }

        // The methods below are the whole behaviour. Update only translates devices into calls, so tests drive the same
        // code from screen coordinates without a mouse.

        /// <summary>What a click at this screen point would hit among the things that pass the filter.</summary>
        public bool TryPickAt(Vector2 screenPoint, System.Predicate<EntitySnapshot> filter, out EntitySnapshot picked) =>
            ScreenPicker.TryPick(viewCamera, session.Model, screenPoint, pickSlackPixels, filter, out picked);

        public void ClickSelect(Vector2 screenPoint, bool additive)
        {
            scratch.Clear();
            // A click picks own units and own buildings (a gate, to toggle it); a box picks units only, as in the original.
            if (TryPickAt(screenPoint, IsOwnSelectable, out EntitySnapshot picked)) scratch.Add(picked.Id);
            Apply(additive);
        }

        public void BoxSelect(Rect screenRect, bool additive)
        {
            scratch.Clear();
            IReadOnlyList<EntitySnapshot> entities = session.Model.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                EntitySnapshot entity = entities[i];
                if (!IsOwnUnit(entity)) continue;
                Vector3 screen = viewCamera.WorldToScreenPoint(EntityViewRegistry.ToWorld(entity.Position));
                if (screen.z > 0f && screenRect.Contains(new Vector2(screen.x, screen.y))) scratch.Add(entity.Id);
            }
            Apply(additive);
        }

        /// <summary>Orders the selection at a screen point. Returns the order sent, or null when there was nothing to order or nowhere to order it.</summary>
        public OrderResolver.Order? OrderAt(Vector2 screenPoint, bool queue)
        {
            if (!TryGroundPoint(screenPoint, out SimVector2 point)) return null;
            IReadOnlyList<EntityId> actors = SelectedUnits();
            if (actors.Count == 0) return null;
            // Units are not order targets yet, so a friendly standing on the spot never swallows a move order.
            EntitySnapshot? target = TryPickAt(screenPoint, IsNotAUnit, out EntitySnapshot picked) ? picked : (EntitySnapshot?)null;
            OrderResolver.Order order = OrderResolver.Resolve(session.Model, actors, target, point);
            Send(order.Kind, actors, order.Point, order.Target, queue ? CommandMode.Queue : CommandMode.Replace);
            return order;
        }

        /// <summary>Enters placement for a buildable catalog entry. Needs a selected unit: somebody has to build it, and a building cannot.</summary>
        public bool BeginPlacing(string definitionId)
        {
            if (SelectedUnits().Count == 0) return false;
            EntityCatalogAsset catalog = session.CatalogAsset;
            for (int i = 0; i < catalog.entries.Length; i++)
            {
                if (catalog.entries[i].id != definitionId || catalog.entries[i].buildCost.Length == 0) continue;
                PlacingIndex = i;
                Version++;
                return true;
            }
            return false;
        }

        public void CancelPlacing()
        {
            if (PlacingIndex < 0) return;
            PlacingIndex = -1;
            Version++;
        }

        /// <summary>Sends the Build order for the cell under the screen point. The authority answers whether the site is legal.</summary>
        public bool PlaceAt(Vector2 screenPoint, bool queue)
        {
            if (PlacingIndex < 0 || !TryGroundPoint(screenPoint, out SimVector2 point)) return false;
            IReadOnlyList<EntityId> builders = SelectedUnits();
            if (builders.Count == 0) return false;
            Cell cell = CellOf(point);
            float size = session.Model.Map.CellSize;
            var anchorPoint = new SimVector2((cell.X + 0.5f) * size, (cell.Y + 0.5f) * size);
            Send(CommandKind.Build, builders, anchorPoint, EntityId.None, queue ? CommandMode.Queue : CommandMode.Replace, PlacingIndex);
            if (!(Keyboard.current != null && Keyboard.current.shiftKey.isPressed)) CancelPlacing();
            return true;
        }

        /// <summary>Demolishes the building under the screen point if it is ours.</summary>
        public bool DemolishAt(Vector2 screenPoint)
        {
            if (!TryPickAt(screenPoint, e => e.Kind == EntityKind.Building && e.Owner == session.Model.LocalSeat, out EntitySnapshot building)) return false;
            IReadOnlyList<EntityId> asker = selection.Count > 0 ? selection : (IReadOnlyList<EntityId>)new[] { building.Id };
            Send(CommandKind.Demolish, asker, building.Position, building.Id);
            return true;
        }

        private Cell CellOf(SimVector2 point)
        {
            float size = session.Model.Map.CellSize;
            return new Cell((int)Mathf.Floor(point.X / size), (int)Mathf.Floor(point.Y / size));
        }

        public void StopSelection()
        {
            IReadOnlyList<EntityId> actors = SelectedUnits();
            if (actors.Count > 0) Send(CommandKind.Stop, actors);
        }

        private readonly List<EntityId> unitScratch = new List<EntityId>();
        private readonly List<EntityId> lastSentActors = new List<EntityId>();

        /// <summary>The actors of the last command this controller sent, for tests that must see who was ordered rather than guess from side effects.</summary>
        public IReadOnlyList<EntityId> LastSentActors => lastSentActors;

        /// <summary>The one door out of the controller: every order records its actors, then goes through the session's sender.</summary>
        private void Send(CommandKind kind, IReadOnlyList<EntityId> actors, SimVector2 point = default, EntityId target = default, CommandMode mode = CommandMode.Replace, int argument = 0)
        {
            lastSentActors.Clear();
            lastSentActors.AddRange(actors);
            session.Commands.Send(kind, actors, point, target, mode, argument);
        }

        /// <summary>The selected units: a selected building is looked at and toggled, never given a walk order.</summary>
        private IReadOnlyList<EntityId> SelectedUnits()
        {
            unitScratch.Clear();
            for (int i = 0; i < selection.Count; i++)
                if (session.Model.TryGet(selection[i], out EntitySnapshot snapshot) && snapshot.Kind == EntityKind.Unit) unitScratch.Add(selection[i]);
            return unitScratch;
        }

        private static bool IsNotAUnit(EntitySnapshot entity) => entity.Kind != EntityKind.Unit;

        public bool TryGroundPoint(Vector2 screenPoint, out SimVector2 point)
        {
            Ray ray = viewCamera.ScreenPointToRay(screenPoint);
            point = default;
            if (ray.direction.y >= -1e-4f) return false;
            Vector3 hit = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
            point = EntityViewRegistry.ToSim(hit);
            return true;
        }

        private bool IsOwnUnit(EntitySnapshot entity) => entity.Kind == EntityKind.Unit && entity.Owner == session.Model.LocalSeat;

        /// <summary>What a single click may select: own units and own standing buildings. Team mates' things are usable, not selectable.</summary>
        public static bool IsOwnSelectable(MatchReadModel model, in EntitySnapshot entity) =>
            entity.Owner == model.LocalSeat && !entity.Remembered && (entity.Kind == EntityKind.Unit || entity.Kind == EntityKind.Building);

        private bool IsOwnSelectable(EntitySnapshot entity) => IsOwnSelectable(session.Model, entity);

        private void Apply(bool additive)
        {
            if (!additive)
            {
                for (int i = 0; i < selection.Count; i++) views.SetSelected(selection[i], false);
                selection.Clear();
            }
            for (int i = 0; i < scratch.Count; i++)
            {
                if (selection.Contains(scratch[i])) continue;
                selection.Add(scratch[i]);
                views.SetSelected(scratch[i], true);
            }
            Version++;
        }

        /// <summary>Replaces the selection from code, for tests and scripted checks.</summary>
        public void Select(IReadOnlyList<EntityId> ids)
        {
            scratch.Clear();
            for (int i = 0; i < ids.Count; i++) scratch.Add(ids[i]);
            Apply(false);
        }
    }
}
