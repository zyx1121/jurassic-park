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
        public bool IsDragging { get; private set; }
        public Rect DragRect { get; private set; }

        /// <summary>Changes whenever the selection or the last answer does, so a readout knows when to rebuild.</summary>
        public int Version { get; private set; }

        /// <summary>The last order's rejection, for the HUD. None when it was accepted.</summary>
        public CommandRejection LastRejection { get; private set; }

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

            if (mouse.leftButton.wasPressedThisFrame)
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
            if (mouse.rightButton.wasPressedThisFrame) OrderAt(cursor, shift);
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
            if (TryPickAt(screenPoint, IsOwnUnit, out EntitySnapshot picked)) scratch.Add(picked.Id);
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
            if (selection.Count == 0 || !TryGroundPoint(screenPoint, out SimVector2 point)) return null;
            // Units are not order targets yet, so a friendly standing on the spot never swallows a move order.
            EntitySnapshot? target = TryPickAt(screenPoint, IsNotAUnit, out EntitySnapshot picked) ? picked : (EntitySnapshot?)null;
            OrderResolver.Order order = OrderResolver.Resolve(session.Model, selection, target, point);
            session.Commands.Send(order.Kind, selection, order.Point, order.Target, queue ? CommandMode.Queue : CommandMode.Replace);
            return order;
        }

        public void StopSelection()
        {
            if (selection.Count > 0) session.Commands.Send(CommandKind.Stop, selection);
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
