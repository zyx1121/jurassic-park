using UnityEngine;

namespace JurassicPark.UI
{
    [CreateAssetMenu(menuName = "Jurassic Park/Selection Config", fileName = "Selection")]
    public sealed class SelectionConfig : ScriptableObject
    {
        [Min(1f)] public float maxPickDistance = 120f;
        public LayerMask selectionMask = ~0;
        [Range(0f, 1f)] public float fadedPickThreshold = 0.5f;
    }
}
