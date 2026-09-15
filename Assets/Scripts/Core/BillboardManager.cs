using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Core
{
    /// <summary>Rotates every registered Billboard toward the main camera once per frame.</summary>
    [ExecuteAlways]
    public sealed class BillboardManager : MonoBehaviour
    {
        private static readonly List<Billboard> Billboards = new List<Billboard>(1024);
        private static BillboardManager instance;

        public static int Count => Billboards.Count;

        public static void Register(Billboard b)
        {
            if (!Billboards.Contains(b)) Billboards.Add(b);
            if (instance == null && Application.isPlaying)
            {
                GameObject go = new GameObject("BillboardManager") { hideFlags = HideFlags.HideAndDontSave };
                instance = go.AddComponent<BillboardManager>();
            }
        }

        public static void Unregister(Billboard b) => Billboards.Remove(b);

        /// <summary>Also usable outside Play mode (scene building, captures) to orient everything once.</summary>
        public static void FaceAll(Camera cam)
        {
            if (cam == null) return;
            Vector3 forward = cam.transform.forward;
            Quaternion full = Quaternion.LookRotation(forward, Vector3.up);
            Vector3 flat = forward;
            flat.y = 0f;
            Quaternion yawOnly = flat.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(flat.normalized, Vector3.up) : full;
            for (int i = Billboards.Count - 1; i >= 0; i--)
            {
                Billboard b = Billboards[i];
                if (b == null) { Billboards.RemoveAt(i); continue; }
                b.Face(full, yawOnly);
            }
        }

        private void LateUpdate()
        {
            FaceAll(Camera.main);
        }
    }
}
