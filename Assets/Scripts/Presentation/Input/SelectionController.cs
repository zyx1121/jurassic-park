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
        [Tooltip("Metres around the cursor within which a click picks an entity.")]
        [SerializeField] private float pickRadius = 1.4f;
        [Tooltip("Pixels the cursor must travel before a press becomes a drag.")]
        [SerializeField] private float dragThreshold = 6f;

        private readonly List<EntityId> selection = new List<EntityId>();
        private readonly List<EntityId> scratch = new List<EntityId>();
        private Vector2 pressedAt;
        private bool pressing;

        public IReadOnlyList<EntityId> Selection => selection;
        public bool IsDragging { get; private set; }
        public Rect DragRect { get; private set; }

        /// <summary>The last order's rejection, for the HUD. None when it was accepted.</summary>
        public CommandRejection LastRejection { get; private set; }

        public void Configure(GameSession gameSession, EntityViewRegistry registry, Camera camera)
        {
            session = gameSession;
            views = registry;
            viewCamera = camera;
        }

        private void OnEnable() => session.EventsDrained += OnEvents;
        private void OnDisable() => session.EventsDrained -= OnEvents;

        private void OnEvents(IReadOnlyList<SimEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] is EntityRemoved removed) selection.Remove(removed.Entity);
                else if (events[i] is CommandResolved answer && answer.Seat == session.Runtime.LocalSeat && !answer.IsRepeat) LastRejection = answer.Rejection;
            }
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            Keyboard keyboard = Keyboard.current;
            if (mouse == null) return;
            Vector2 cursor = mouse.position.ReadValue();

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
                bool additive = keyboard != null && keyboard.shiftKey.isPressed;
                if (IsDragging) SelectInRect(DragRect, additive);
                else SelectAt(cursor, additive);
                pressing = false;
                IsDragging = false;
            }

            if (mouse.rightButton.wasPressedThisFrame && selection.Count > 0 && TryGroundPoint(cursor, out SimVector2 point))
            {
                SimulationRuntime runtime = session.Runtime;
                Entity target = OrderResolver.Pick(runtime.World, point, pickRadius, e => e.Kind != EntityKind.Unit);
                OrderResolver.Order order = OrderResolver.Resolve(runtime, selection, target, point);
                CommandMode mode = keyboard != null && keyboard.shiftKey.isPressed ? CommandMode.Queue : CommandMode.Replace;
                runtime.LocalSender.Send(order.Kind, selection, order.Point, order.Target, mode);
            }
            if (keyboard != null && keyboard.xKey.wasPressedThisFrame && selection.Count > 0)
                session.Runtime.LocalSender.Send(CommandKind.Stop, selection);
        }

        public bool TryGroundPoint(Vector2 screenPoint, out SimVector2 point)
        {
            Ray ray = viewCamera.ScreenPointToRay(screenPoint);
            point = default;
            if (ray.direction.y >= -1e-4f) return false;
            Vector3 hit = ray.origin + ray.direction * (-ray.origin.y / ray.direction.y);
            point = EntityViewRegistry.ToSim(hit);
            return true;
        }

        private void SelectAt(Vector2 screenPoint, bool additive)
        {
            scratch.Clear();
            if (TryGroundPoint(screenPoint, out SimVector2 point))
            {
                Entity picked = OrderResolver.Pick(session.Runtime.World, point, pickRadius, IsOwnUnit);
                if (picked != null) scratch.Add(picked.Id);
            }
            Apply(additive);
        }

        private void SelectInRect(Rect rect, bool additive)
        {
            scratch.Clear();
            IReadOnlyList<Entity> entities = session.Runtime.World.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity entity = entities[i];
                if (!IsOwnUnit(entity)) continue;
                Vector3 screen = viewCamera.WorldToScreenPoint(EntityViewRegistry.ToWorld(entity.Position));
                if (screen.z > 0f && rect.Contains(new Vector2(screen.x, screen.y))) scratch.Add(entity.Id);
            }
            Apply(additive);
        }

        private bool IsOwnUnit(Entity entity) => entity.IsAlive && entity.Kind == EntityKind.Unit && entity.Owner == session.Runtime.LocalSeat;

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
