using UnityEngine;

namespace JurassicPark.UI
{
    [CreateAssetMenu(menuName = "Jurassic Park/HUD Config", fileName = "Hud")]
    public sealed class HudConfig : ScriptableObject
    {
        public Color panel = new Color32(17, 25, 26, 235);
        public Color text = new Color32(239, 231, 209, 255);
        public Color muted = new Color32(183, 192, 180, 255);
        public Color accent = new Color32(232, 183, 91, 255);
        public Color healthy = new Color32(129, 181, 133, 255);
        public Color danger = new Color32(244, 116, 101, 255);
        public Color track = new Color32(51, 65, 61, 255);
        [Range(0f, 1f)] public float lowHealthThreshold = 0.3f;
        [Min(0.05f)] public float refreshInterval = 0.1f;
        [Range(12, 24)] public int bodyFontSize = 16;
        [Range(16, 30)] public int headingFontSize = 20;
    }
}
