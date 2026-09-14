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
        [Tooltip("Camera pitch in degrees, looking down.")]
        [Range(10f, 80f)] [SerializeField] private float pitch = 30f;
        [Tooltip("Distance from the look-at point along the camera's back axis.")]
        [Min(1f)] [SerializeField] private float distance = 16f;
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

        private Vector3 lookPoint;
        private Vector3 velocity;

        private void OnEnable()
        {
            if (target != null)
            {
                lookPoint = ClampLook(target.position + Vector3.up * lookHeight);
                Apply();
            }
        }

        private void LateUpdate()
        {
            if (target == null)
            {
                return;
            }

            Vector3 desired = ClampLook(target.position + Vector3.up * lookHeight);
            lookPoint = Application.isPlaying && smoothTime > 0f
                ? Vector3.SmoothDamp(lookPoint, desired, ref velocity, smoothTime)
                : desired;
            Apply();
        }

        private void Apply()
        {
            Quaternion rot = Quaternion.Euler(pitch, 0f, 0f);
            transform.rotation = rot;
            transform.position = lookPoint - rot * Vector3.forward * distance;
        }

        /// <summary>Soft clamp: inside the bounds minus the edge the point is untouched, then it eases to the border.</summary>
        public Vector3 ClampLook(Vector3 p)
        {
            p.x = SoftClamp(p.x, bounds.xMin, bounds.xMax, softEdge);
            p.z = SoftClamp(p.z, bounds.yMin, bounds.yMax, softEdge);
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
