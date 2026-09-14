using UnityEngine;

namespace JurassicPark.Core
{
    /// <summary>
    /// Keeps a sprite quad facing the camera around the world Y axis, so 2D characters
    /// stand upright inside the 3D scene (HD-2D). Tilting toward the camera pitch is
    /// optional and gives the sprite the same lean as the camera, which reads better
    /// with a tilted follow camera.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class Billboard : MonoBehaviour
    {
        [SerializeField] private bool matchCameraPitch = true;

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return;
            }

            Vector3 forward = cam.transform.forward;
            if (!matchCameraPitch)
            {
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f)
                {
                    return;
                }
            }

            transform.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
        }
    }
}
