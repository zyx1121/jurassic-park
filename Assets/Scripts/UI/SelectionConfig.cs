using UnityEngine;

namespace JurassicPark.UI
{
    [CreateAssetMenu(menuName = "Jurassic Park/Selection Config", fileName = "Selection")]
    public sealed class SelectionConfig : ScriptableObject
    {
        [Min(1f)] public float maxPickDistance = 120f;
        public LayerMask selectionMask = ~0;
        [Range(16, 64)] public int cursorSize = 24;
        public Color normalCursorColor = new Color32(226, 217, 193, 255);
        public Color readyCursorColor = new Color32(155, 213, 165, 255);
        public Color unavailableCursorColor = new Color32(232, 180, 93, 255);
    }
}
