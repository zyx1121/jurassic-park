using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Scene
{
    /// <summary>
    /// Fades props that stand between the camera and its follow target. A sphere cast from the
    /// camera to the target collects Occluders each frame; those not hit this frame return to
    /// their normal material. Lives on the camera next to FollowCamera.
    /// </summary>
    [RequireComponent(typeof(FollowCamera))]
    public sealed class SeeThrough : MonoBehaviour
    {
        [SerializeField] private float radius = 0.9f;
        [SerializeField] private LayerMask mask = ~0;
        [Tooltip("Frames an occluder stays faded after the cast stops hitting it, to avoid flicker.")]
        [SerializeField] private int holdFrames = 6;

        private FollowCamera follow;
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private readonly List<Occluder> faded = new List<Occluder>();

        private void Awake() => follow = GetComponent<FollowCamera>();

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

            int n = Physics.SphereCastNonAlloc(from, radius, dir, hits, dist - 1.2f, mask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Occluder o = hits[i].collider.GetComponentInParent<Occluder>();
                if (o == null) continue;
                o.LastSeenFrame = Time.frameCount;
                if (!o.Faded)
                {
                    o.SetFaded(true);
                    faded.Add(o);
                }
            }

            for (int i = faded.Count - 1; i >= 0; i--)
            {
                Occluder o = faded[i];
                if (o == null) { faded.RemoveAt(i); continue; }
                if (Time.frameCount - o.LastSeenFrame > holdFrames)
                {
                    o.SetFaded(false);
                    faded.RemoveAt(i);
                }
            }
        }
    }
}
