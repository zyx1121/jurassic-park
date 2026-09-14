using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>Marks a placed prop so gathering, saving and the generator can find its variant and seed values.</summary>
    public sealed class PropInstance : MonoBehaviour
    {
        public PropVariant variant;
        public float scale = 1f;
        public Color tint = Color.white;
    }
}
