using UnityEngine;

namespace JurassicPark.Scene
{
    /// <summary>
    /// Fixed-angle follow camera for the HD-2D view. Keeps a constant pitch and offset from the
    /// target, damps toward it, and softly clamps the look-at point inside the island bounds so
    /// the camera never shows the void past the shoreline.
    /// </summary>
    [ExecuteAlways]
    public sealed class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private CameraViewConfig config;
        [Tooltip("Camera pitch in degrees, looking down.")]
        [Range(10f, 80f)] [SerializeField] private float pitch = 38f;
        [Tooltip("Distance from the look-at point along the camera's back axis.")]
        [Min(1f)] [SerializeField] private float distance = 22f;
        [Tooltip("Height of the look-at point above the target's feet.")]
        [SerializeField] private float lookHeight = 1f;
        [Tooltip("Seconds to reach the target. 0 snaps.")]
        [Min(0f)] [SerializeField] private float smoothTime = 0.15f;
        [Tooltip("XZ rectangle the look-at point is kept inside (x, z, width, depth).")]
        [SerializeField] private Rect bounds = new Rect(-28f, -28f, 56f, 56f);
        [Tooltip("Distance over which the clamp eases in, in world units.")]
        [Min(0f)] [SerializeField] private float softEdge = 4f;

        public Transform Target { get => target; set => target = value; }
        public Rect Bounds { get => bounds; set => bounds = value; }
        public CameraViewConfig Config => config;
        public float Pitch => config != null ? config.pitch : pitch;
        public float Distance => config != null ? config.distance : distance;

        private Vector3 lookPoint;
        private Vector3 velocity;
        private Camera cameraComponent;

        public void Configure(CameraViewConfig value)
        {
            config = value;
            cameraComponent = GetComponent<Camera>();
            if (target != null) lookPoint = ClampLook(target.position + Vector3.up * LookHeight);
            Apply();
        }

        private float LookHeight => config != null ? config.lookHeight : lookHeight;
        private float SmoothTime => config != null ? config.smoothTime : smoothTime;

        public void FramePoint(Vector3 groundPoint)
        {
            lookPoint = ClampLook(groundPoint + Vector3.up * LookHeight);
            velocity = Vector3.zero;
            Apply();
        }

        private void OnEnable()
        {
            if (target != null)
            {
                lookPoint = ClampLook(target.position + Vector3.up * LookHeight);
                Apply();
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desired = ClampLook(target.position + Vector3.up * LookHeight);
            lookPoint = Application.isPlaying && SmoothTime > 0f
                ? Vector3.SmoothDamp(lookPoint, desired, ref velocity, SmoothTime)
                : desired;
            Apply();
        }

        private void Apply()
        {
            Quaternion rot = Quaternion.Euler(Pitch, 0f, 0f);
            transform.rotation = rot;
            transform.position = lookPoint - rot * Vector3.forward * Distance;
            if (config == null) return;
            if (cameraComponent == null) cameraComponent = GetComponent<Camera>();
            if (cameraComponent != null)
            {
                cameraComponent.fieldOfView = config.fieldOfView;
                cameraComponent.nearClipPlane = config.nearClip;
                cameraComponent.farClipPlane = config.farClip;
            }
        }

        /// <summary>Soft clamp: inside the bounds minus the edge the point is untouched, then it eases to the border.</summary>
        public Vector3 ClampLook(Vector3 p)
        {
            float edge = config != null ? config.softEdge : softEdge;
            p.x = SoftClamp(p.x, bounds.xMin, bounds.xMax, edge);
            p.z = SoftClamp(p.z, bounds.yMin, bounds.yMax, edge);
            return p;
        }

        public static float SoftClamp(float v, float min, float max, float edge)
        {
            if (edge <= 0f || max - min <= edge * 2f)
            {
                return Mathf.Clamp(v, min, max);
            }

            if (v < min + edge)
            {
                float t = Mathf.Clamp01((min + edge - v) / edge);
                return Mathf.Lerp(min + edge, min, t * t);
            }

            if (v > max - edge)
            {
                float t = Mathf.Clamp01((v - (max - edge)) / edge);
                return Mathf.Lerp(max - edge, max, t * t);
            }

            return v;
        }
    }
}
