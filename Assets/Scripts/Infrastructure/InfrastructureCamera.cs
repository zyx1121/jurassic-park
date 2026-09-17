using System.Collections.Generic;
using JurassicPark.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JurassicPark.Infrastructure
{
    [RequireComponent(typeof(Camera))]
    public sealed class InfrastructureCamera : MonoBehaviour
    {
        [SerializeField] private InfrastructureSession session;
        private Camera view;
        private InfrastructureHud hud;
        private Vector2 focus;
        private float zoom;
        private bool initialized;
        private bool dragging;
        private Vector2 dragStart;
        private LineRenderer preview;
        private readonly Plane ground = new Plane(Vector3.up, Vector3.zero);

        public Camera View => view;
        public void Configure(InfrastructureSession value) => session = value;

        private void Start()
        {
            view = GetComponent<Camera>();
            hud = session.GetComponent<InfrastructureHud>();
            view.orthographic = true;
            zoom = session.Config.initialZoom;
            var outline = new GameObject("PlacementFootprint");
            preview = outline.AddComponent<LineRenderer>();
            preview.sharedMaterial = session.Config.structureMaterial;
            preview.useWorldSpace = true;
            preview.loop = true;
            preview.positionCount = 4;
            preview.widthMultiplier = .12f;
            preview.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (preview != null) Destroy(preview.gameObject);
        }

        public void Frame(Vector2 point) => focus = point;

        public bool ScreenToGround(Vector2 screen, out Vector2 point)
        {
            Ray ray = view.ScreenPointToRay(screen);
            if (ground.Raycast(ray, out float distance))
            {
                Vector3 hit = ray.GetPoint(distance);
                point = new Vector2(hit.x, hit.z);
                return true;
            }
            point = default;
            return false;
        }

        private void Update()
        {
            if (session.World == null) return;
            if (!initialized)
            {
                focus = (session.Layout.Map.CellCenter(session.Config.map.arrival) +
                    session.Layout.Map.CellCenter(session.Layout.MainCamp.center)) * .5f;
                initialized = true;
            }
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard != null && !WorldInputBlockers.BlocksWorldInput)
            {
                Vector2 movement = Vector2.zero;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) movement.x--;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) movement.x++;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) movement.y--;
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) movement.y++;
                focus += movement.normalized * (session.Config.cameraPanSpeed * Time.unscaledDeltaTime);
                if (keyboard.bKey.wasPressedThisFrame) session.BeginWallPlacement();
                if (keyboard.xKey.wasPressedThisFrame) session.OrderSelection(InfraCommandKind.Stop, default);
                if (keyboard.rKey.wasPressedThisFrame) session.SpawnRaid();
                if (keyboard.f6Key.wasPressedThisFrame) session.BeginDemonstration();
                if (keyboard.spaceKey.wasPressedThisFrame && session.World.Entities.TryGetValue(session.SelectedId, out InfraEntity entity))
                    Frame(entity.Position);
                if (keyboard.homeKey.wasPressedThisFrame)
                {
                    Frame(Vector2.zero);
                    zoom = session.Config.cameraZoomRange.y;
                }
                if (keyboard.escapeKey.wasPressedThisFrame)
                {
                    if (session.PlacingWall) session.PlacingWall = false;
                    else session.Paused = !session.Paused;
                }
            }
            if (mouse != null) HandlePointer(mouse, keyboard);
            float halfWidth = session.Layout.Map.Width * session.Layout.Map.CellSize * .5f;
            float halfHeight = session.Layout.Map.Height * session.Layout.Map.CellSize * .5f;
            focus.x = Mathf.Clamp(focus.x, -halfWidth, halfWidth);
            focus.y = Mathf.Clamp(focus.y, -halfHeight, halfHeight);
            transform.rotation = Quaternion.Euler(session.Config.cameraPitch, 0, 0);
            transform.position = new Vector3(focus.x, 0, focus.y) - transform.forward * 160f;
            view.orthographicSize = zoom;
        }

        private void HandlePointer(Mouse mouse, Keyboard keyboard)
        {
            Vector2 screen = mouse.position.ReadValue();
            bool blocked = WorldInputBlockers.BlocksPointer(screen);
            if (!blocked)
                zoom = Mathf.Clamp(zoom - mouse.scroll.ReadValue().y / 120f * session.Config.cameraZoomSpeed,
                    session.Config.cameraZoomRange.x, session.Config.cameraZoomRange.y);
            if (hud != null && mouse.leftButton.wasPressedThisFrame && hud.TryMinimapPoint(screen, out Vector2 minimap))
            {
                Frame(minimap);
                return;
            }
            bool hit = ScreenToGround(screen, out Vector2 world);
            Vector2Int cell = session.Layout.Map.WorldToCell(world);
            bool placement = session.PlacingWall && hit && !blocked;
            preview.gameObject.SetActive(placement);
            if (placement)
            {
                bool valid = session.PreviewBuild(cell, out string reason);
                float half = session.Layout.Map.CellSize * .5f;
                Vector2 center = session.Layout.Map.CellCenter(cell);
                preview.SetPosition(0, new Vector3(center.x - half, .12f, center.y - half));
                preview.SetPosition(1, new Vector3(center.x - half, .12f, center.y + half));
                preview.SetPosition(2, new Vector3(center.x + half, .12f, center.y + half));
                preview.SetPosition(3, new Vector3(center.x + half, .12f, center.y - half));
                var properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", valid ? Color.green : Color.red);
                preview.SetPropertyBlock(properties);
                if (hud != null) hud.PlacementMessage = valid ? $"Build wall at {cell}" : $"Cannot build: {reason}";
            }
            else if (hud != null) hud.PlacementMessage = "";

            if (mouse.rightButton.wasPressedThisFrame && !blocked && hit)
            {
                if (session.PlacingWall) { session.PlacingWall = false; return; }
                int target = session.Pick(world);
                if (target != 0 && session.World.Entities.TryGetValue(target, out InfraEntity entity) &&
                    (entity.Kind == InfraEntityKind.Source || entity.Kind == InfraEntityKind.GroundPile))
                    session.OrderSelection(InfraCommandKind.Gather, cell, target);
                else session.OrderSelection(InfraCommandKind.Move, cell);
            }
            if (mouse.leftButton.wasPressedThisFrame && !blocked && hit)
            {
                if (session.PlacingWall)
                {
                    session.OrderSelection(InfraCommandKind.Build, cell);
                    session.PlacingWall = false;
                    return;
                }
                dragging = true;
                dragStart = screen;
            }
            if (dragging && hud != null) hud.ShowSelectionRectangle(dragStart, screen, true);
            if (!dragging || !mouse.leftButton.wasReleasedThisFrame) return;
            dragging = false;
            if (hud != null) hud.ShowSelectionRectangle(dragStart, screen, false);
            if (blocked || !hit) return;
            if ((screen - dragStart).sqrMagnitude < 64f)
                session.Select(session.Pick(world), keyboard != null && keyboard.shiftKey.isPressed);
            else
            {
                Rect rectangle = Rect.MinMaxRect(Mathf.Min(screen.x, dragStart.x), Mathf.Min(screen.y, dragStart.y),
                    Mathf.Max(screen.x, dragStart.x), Mathf.Max(screen.y, dragStart.y));
                var selected = new List<int>();
                foreach (InfraEntity entity in session.World.Entities.Values)
                {
                    Vector3 projected = view.WorldToScreenPoint(new Vector3(entity.Position.x, 0, entity.Position.y));
                    if (projected.z > 0 && rectangle.Contains(projected)) selected.Add(entity.Id);
                }
                session.SelectWorkers(selected);
            }
        }
    }
}
