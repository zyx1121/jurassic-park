using UnityEngine;
using UnityEngine.InputSystem;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// Fixed-orientation RTS camera: 60 degrees down, yaw never changes, pan with WASD, arrows or the screen edge, zoom with the
    /// wheel, clamped to the map. Orthographic by default: every cell is the same size on screen, upright things stay upright at
    /// the screen edge, and the art direction's fixed pixels-per-metre only exists without perspective.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class RtsCamera : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [Range(30f, 80f)] [SerializeField] private float pitch = 60f;
        [SerializeField] private bool orthographic = true;
        [Tooltip("Half the visible height in metres when orthographic.")]
        [SerializeField] private float orthographicSize = 17f;
        [SerializeField] private float minOrthographicSize = 9f;
        [SerializeField] private float maxOrthographicSize = 34f;
        [Tooltip("Camera distance from the focus. Only framing when perspective; when orthographic it just has to clear the terrain.")]
        [SerializeField] private float distance = 75f;
        [SerializeField] private float minDistance = 32f;
        [SerializeField] private float maxDistance = 140f;
        [SerializeField] private float panSpeed = 30f;
        [Tooltip("Zoom distance at which Pan Speed applies as written.")]
        [SerializeField] private float referenceDistance = 75f;
        [SerializeField] private float referenceOrthographicSize = 17f;
        [SerializeField] private float zoomStep = 3f;
        [SerializeField] private float edgePixels = 6f;
        [SerializeField] private bool edgePan = true;

        private Vector3 focus;
        private bool focusSet;
        private bool zoomed;
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

        private void Start()
        {
            if (!focusSet)
            {
                var map = session.Runtime.Map;
                LookAt(new Vector3(map.Width * map.CellSize * 0.5f, 0f, map.Height * map.CellSize * 0.5f));
            }
        }

        private void Update()
        {
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
                float scroll = mouse.scroll.ReadValue().y;
                if (scroll != 0f)
                {
                    if (orthographic) orthographicSize = Mathf.Clamp(orthographicSize - Mathf.Sign(scroll) * zoomStep * 0.5f, minOrthographicSize, maxOrthographicSize);
                    else distance = Mathf.Clamp(distance - Mathf.Sign(scroll) * zoomStep, minDistance, maxDistance);
                    zoomed = true;
                }
            }
            // Nothing moved: leave the transform alone so an idle camera costs nothing.
            if (pan == Vector2.zero && !zoomed) return;
            zoomed = false;

            // Pan faster when zoomed out, so crossing the map takes about the same time at any zoom.
            float zoom = orthographic ? orthographicSize / referenceOrthographicSize : distance / referenceDistance;
            float speed = panSpeed * zoom * Time.unscaledDeltaTime;
            focus += new Vector3(pan.x, 0f, pan.y).normalized * speed;
            var map = session.Runtime.Map;
            focus.x = Mathf.Clamp(focus.x, 0f, map.Width * map.CellSize);
            focus.z = Mathf.Clamp(focus.z, 0f, map.Height * map.CellSize);
            Apply();
        }

        private void Apply()
        {
            Quaternion rotation = Quaternion.Euler(pitch, 0f, 0f);
            if (viewCamera == null) viewCamera = GetComponent<Camera>();
            viewCamera.orthographic = orthographic;
            if (orthographic) viewCamera.orthographicSize = orthographicSize;
            transform.SetPositionAndRotation(focus - rotation * Vector3.forward * distance, rotation);
        }
    }
}
