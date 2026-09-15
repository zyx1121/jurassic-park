using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Core
{
    /// <summary>Rotates every registered Billboard toward the main camera once per frame.</summary>
    [ExecuteAlways]
    public sealed class BillboardManager : MonoBehaviour
    {
        private static readonly List<Billboard> Billboards = new List<Billboard>(16384);
        private static readonly HashSet<Billboard> Registered = new HashSet<Billboard>();
        private static BillboardManager instance;
        private static Quaternion lastCameraRotation;
        private static bool dirty = true;

        public static int Count => Billboards.Count;

        public static void Register(Billboard b)
        {
            // HashSet membership: List.Contains made registration O(n^2), about 40 s for 12k props
            if (Registered.Add(b)) { Billboards.Add(b); dirty = true; }
            if (instance == null && Application.isPlaying)
            {
                GameObject go = new GameObject("BillboardManager") { hideFlags = HideFlags.HideAndDontSave };
                instance = go.AddComponent<BillboardManager>();
            }
        }

        public static void Unregister(Billboard b)
        {
            if (Registered.Remove(b)) Billboards.Remove(b);
        }

        /// <summary>Also usable outside Play mode (scene building, captures) to orient everything once.</summary>
        public static void FaceAll(Camera cam)
        {
            if (cam == null) return;
            // The HD-2D camera keeps a fixed orientation, so thousands of transform writes per frame
            // are only needed when it actually turns or a billboard was added.
            Quaternion camRot = cam.transform.rotation;
            if (!dirty && Quaternion.Angle(camRot, lastCameraRotation) < 0.01f) return;
            lastCameraRotation = camRot;
            dirty = false;
            Vector3 forward = cam.transform.forward;
            Quaternion full = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 flat = forward;
            flat.y = 0f;
            Quaternion yawOnly = flat.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : full;
            for (int i = Billboards.Count - 1; i >= 0; i--)
            {
                Billboard b = Billboards[i];
                if (b == null) { Billboards.RemoveAt(i); Registered.Remove(b); continue; }
                b.Face(full, yawOnly);
            }
        }

        private void LateUpdate()
        {
            FaceAll(Camera.main);
        }
    }
}
