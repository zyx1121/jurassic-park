using UnityEngine;

namespace JurassicPark.Scene
{
    [CreateAssetMenu(menuName = "Jurassic Park/Camera View", fileName = "CameraView")]
    public sealed class CameraViewConfig : ScriptableObject
    {
        [Header("Tactical overview")]
        [Range(10f, 80f)] public float pitch = 60f;
        [Min(1f)] public float distance = 28f;
        [Range(20f, 75f)] public float fieldOfView = 40f;
        public float lookHeight = 0.5f;
        [Min(0f)] public float smoothTime = 0.15f;
        [Min(0f)] public float softEdge = 4f;
        [Min(0.01f)] public float nearClip = 0.3f;
        [Min(10f)] public float farClip = 160f;

        [Header("Readable HD-2D presentation")]
        public bool depthOfField;
        [Min(0f)] public float blurStart = 60f;
        [Min(0f)] public float blurEnd = 90f;
        [Range(0f, 1.5f)] public float blurRadius = 0.15f;
        [Min(0f)] public float bloomThreshold = 1.1f;
        [Min(0f)] public float bloomIntensity = 0.35f;
        [Range(0f, 1f)] public float bloomScatter = 0.5f;
        [Min(0f)] public float bloomClamp = 6f;
        public Color bloomTint = new Color(1f, 0.9f, 0.75f);
        [Range(0f, 1f)] public float vignette = 0.16f;
        [Range(0f, 1f)] public float vignetteSoftness = 0.45f;
        public Color vignetteColor = new Color(0.04f, 0.08f, 0.06f);
        public float exposure;
        [Range(-100f, 100f)] public float contrast = 12f;
        [Range(-100f, 100f)] public float saturation = -5f;
        public Color colorFilter = new Color(0.94f, 1f, 0.94f);
    }
}
