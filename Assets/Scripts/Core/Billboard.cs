using UnityEngine;

namespace JurassicPark.Core
{
    /// <summary>
    /// Keeps a sprite quad facing the camera around the world Y axis, so 2D characters and props
    /// stand upright inside the 3D scene (HD-2D). Instances register with BillboardManager, which
    /// rotates all of them in one LateUpdate instead of one call per prop.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class Billboard : MonoBehaviour
    {
        [SerializeField] private bool matchCameraPitch = true;

        public bool MatchCameraPitch => matchCameraPitch;

        private void OnEnable() => BillboardManager.Register(this);

        private void OnDisable() => BillboardManager.Unregister(this);

        public void Face(Quaternion full, Quaternion yawOnly)
        {
            transform.rotation = matchCameraPitch ? full : yawOnly;
        }
    }
}
