using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Scene
{
    /// <summary>
    /// Fades props that stand between the camera and its follow target: a sphere cast collects
    /// Occluders each frame, every tracked occluder eases its fade toward "hit" or "clear", and the
    /// see-through shader opens a soft hole around the player's screen position so the edges of the
    /// prop stay readable while the middle clears.
    /// </summary>
    [RequireComponent(typeof(FollowCamera))]
    public sealed class SeeThrough : MonoBehaviour
    {
        private static readonly int PlayerScreenId = Shader.PropertyToID("_SeeThroughPlayerScreen");

        [SerializeField] private float radius = 0.9f;
        [SerializeField] private LayerMask mask = ~0;
        [Tooltip("Frames an occluder stays wanted after the cast stops hitting it, to avoid flicker.")]
        [SerializeField] private int holdFrames = 4;

        private FollowCamera follow;
        private Camera cam;
        private readonly RaycastHit[] hits = new RaycastHit[48];
        private readonly List<Occluder> tracked = new List<Occluder>();

        private void Awake()
        {
            follow = GetComponent<FollowCamera>();
            cam = GetComponent<Camera>();
        }

        private void LateUpdate()
        {
            Transform t = follow.Target;
            if (t == null) return;
            Vector3 from = transform.position;
            Vector3 to = t.position + Vector3.up * 1f;
            Vector3 dir = to - from;
            float dist = dir.magnitude;
            if (dist < 0.01f) return;
            dir /= dist;

            Vector3 vp = cam.WorldToViewportPoint(to);
            Shader.SetGlobalVector(PlayerScreenId, new Vector4(vp.x, vp.y, cam.aspect, 0f));

            int n = Physics.SphereCastNonAlloc(from, radius, dir, hits, dist - 1.2f, mask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Occluder o = hits[i].collider.GetComponentInParent<Occluder>();
                if (o == null) continue;
                o.LastSeenFrame = Time.frameCount;
                if (!tracked.Contains(o)) tracked.Add(o);
            }

            for (int i = tracked.Count - 1; i >= 0; i--)
            {
                Occluder o = tracked[i];
                if (o == null) { tracked.RemoveAt(i); continue; }
                bool wanted = Time.frameCount - o.LastSeenFrame <= holdFrames;
                if (!o.Tick(wanted)) tracked.RemoveAt(i);
            }
        }
    }
}
