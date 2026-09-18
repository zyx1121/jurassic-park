using UnityEngine;
using UnityEngine.InputSystem;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// Fixed-orientation RTS camera: 60 degrees down, yaw never changes, pan with WASD, arrows or the screen edge, zoom with the
    /// wheel, clamped to the map. Perspective, with the original map's camera as the starting point: pitch 56 degrees, a 70
    /// degree horizontal lens and 1650 map units of distance, which is about 26 m at two metres per cell. The lean of upright
    /// things near the screen edge is part of that look, not a defect.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class RtsCamera : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [Range(30f, 80f)] [SerializeField] private float pitch = 56f;
        [Tooltip("Fixed rotation about the vertical axis. 45 puts the cell grid diagonal on screen, the classic RTS look; pan directions are screen-relative regardless.")]
        [Range(-180f, 180f)] [SerializeField] private float yaw = 45f;
        [SerializeField] private bool orthographic = false;
        [Tooltip("Half the visible height in metres when orthographic.")]
        [SerializeField] private float orthographicSize = 17f;
        [SerializeField] private float minOrthographicSize = 9f;
        [SerializeField] private float maxOrthographicSize = 34f;
        [Tooltip("Camera distance from the focus. Only framing when perspective; when orthographic it just has to clear the terrain.")]
        [SerializeField] private float distance = 26f;
        [SerializeField] private float minDistance = 14f;
        [SerializeField] private float maxDistance = 44f;
        [SerializeField] private float panSpeed = 30f;
        [Tooltip("Zoom distance at which Pan Speed applies as written.")]
        [SerializeField] private float referenceDistance = 26f;
        [SerializeField] private float referenceOrthographicSize = 17f;
        [Tooltip("Zoom change per unit of scroll. Proportional on purpose: a trackpad sends a small delta nearly every frame, and treating each as a full step slams the zoom to its limit in a quarter of a second.")]
        [SerializeField] private float zoomPerScrollUnit = 1.5f;
        [Tooltip("Largest zoom change one frame may apply, in the same units, so a wheel that reports 120 per notch does not jump.")]
        [SerializeField] private float maxZoomPerFrame = 3f;
        [SerializeField] private float edgePixels = 6f;
        [SerializeField] private bool edgePan = true;

        private Vector3 focus;
        private bool focusSet;
        private Camera viewCamera;

        public void Configure(GameSession gameSession) => session = gameSession;

        /// <summary>Ground point the camera looks at.</summary>
        public Vector3 Focus => focus;

        public void LookAt(Vector3 groundPoint)
        {
            focus = new Vector3(groundPoint.x, 0f, groundPoint.z);
            focusSet = true;
            Apply();
        }

        private void OnEnable()
        {
            session.MatchBegan += Centre;
            if (session.Model != null) Centre();
        }

        private void OnDisable() => session.MatchBegan -= Centre;

        private void Centre()
        {
            if (focusSet) return;
            var map = session.Model.Map;
            LookAt(new Vector3(map.Width * map.CellSize * 0.5f, 0f, map.Height * map.CellSize * 0.5f));
        }

        private void Update()
        {
            if (session.Model == null) return;
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            Vector2 pan = Vector2.zero;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) pan.y += 1f;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) pan.y -= 1f;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) pan.x += 1f;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) pan.x -= 1f;
            }
            float scroll = 0f;
            if (mouse != null)
            {
                if (edgePan && Application.isFocused && pan == Vector2.zero)
                {
                    Vector2 p = mouse.position.ReadValue();
                    if (p.x >= 0 && p.y >= 0 && p.x <= Screen.width && p.y <= Screen.height)
                    {
                        if (p.x <= edgePixels) pan.x -= 1f; else if (p.x >= Screen.width - edgePixels) pan.x += 1f;
                        if (p.y <= edgePixels) pan.y -= 1f; else if (p.y >= Screen.height - edgePixels) pan.y += 1f;
                    }
                }
                scroll = mouse.scroll.ReadValue().y;
            }
            // Nothing moved: leave the transform alone so an idle camera costs nothing.
            if (pan == Vector2.zero && scroll == 0f) return;
            if (scroll != 0f) Zoom(scroll);
            if (pan != Vector2.zero) Pan(pan, Time.unscaledDeltaTime);
        }

        /// <summary>Current zoom: half the visible height when orthographic, the camera distance otherwise.</summary>
        public float ZoomLevel => orthographic ? orthographicSize : distance;

        /// <summary>Turns a screen-relative pan (x right, y up the screen) into ground directions under the camera's yaw.</summary>
        public Vector3 PanToWorld(Vector2 direction) => Quaternion.Euler(0f, yaw, 0f) * new Vector3(direction.x, 0f, direction.y);

        /// <summary>Zooms by a scroll delta. Positive zooms in. Proportional to the delta and limited per call.</summary>
        public void Zoom(float scrollDelta)
        {
            float change = Mathf.Clamp(scrollDelta * zoomPerScrollUnit, -maxZoomPerFrame, maxZoomPerFrame);
            if (orthographic) orthographicSize = Mathf.Clamp(orthographicSize - change, minOrthographicSize, maxOrthographicSize);
            else distance = Mathf.Clamp(distance - change * (referenceDistance / referenceOrthographicSize), minDistance, maxDistance);
            Apply();
        }

        /// <summary>Moves the focus along the ground, faster when zoomed out so crossing the map takes about the same time at any zoom, clamped to the map.</summary>
        public void Pan(Vector2 direction, float deltaTime)
        {
            float zoom = orthographic ? orthographicSize / referenceOrthographicSize : distance / referenceDistance;
            // A long frame must not throw the camera across the map.
            float speed = panSpeed * zoom * Mathf.Min(deltaTime, 0.1f);
            focus += PanToWorld(direction).normalized * speed;
            var map = session.Model.Map;
            focus.x = Mathf.Clamp(focus.x, 0f, map.Width * map.CellSize);
            focus.z = Mathf.Clamp(focus.z, 0f, map.Height * map.CellSize);
            Apply();
        }

        private void Apply()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            if (viewCamera == null) viewCamera = GetComponent<Camera>();
            viewCamera.orthographic = orthographic;
            if (orthographic) viewCamera.orthographicSize = orthographicSize;
            transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
        }
    }
}
