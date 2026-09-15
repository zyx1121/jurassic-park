using UnityEngine;

namespace JurassicPark.Scene
{
    /// <summary>Length of a day and how sun, sky, fog and ambient change across it. Time 0 is dawn.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Day Night Config", fileName = "DayNight")]
    public sealed class DayNightConfig : ScriptableObject
    {
        [Tooltip("Real seconds for one full day-night cycle.")]
        [Min(10f)] public float dayLengthSeconds = 480f;

        [Tooltip("Normalized time where each phase starts: Dawn at 0, then Day, Dusk, Night.")]
        [Range(0f, 1f)] public float dayStart = 0.1f;
        [Range(0f, 1f)] public float duskStart = 0.55f;
        [Range(0f, 1f)] public float nightStart = 0.65f;

        [Tooltip("Sun pitch over the day; 0 = horizon at dawn, 90 = noon, 180 = horizon at dusk, then below.")]
        public AnimationCurve sunPitch = AnimationCurve.Linear(0f, 0f, 1f, 360f);
        public Gradient sunColor = DefaultSunColor();
        public AnimationCurve sunIntensity = DefaultSunIntensity();
        public Gradient ambientColor = DefaultAmbient();
        public Gradient fogColor = DefaultFog();
        [Tooltip("Fog end distance over the day; start distance is a third of it.")]
        public AnimationCurve fogEnd = DefaultFogEnd();

        private static Gradient DefaultSunColor()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.55f, 0.35f), 0f),
                    new GradientColorKey(new Color(1f, 0.95f, 0.85f), 0.3f),
                    new GradientColorKey(new Color(1f, 0.6f, 0.4f), 0.6f),
                    new GradientColorKey(new Color(0.45f, 0.45f, 0.75f), 0.7f),
                    new GradientColorKey(new Color(0.45f, 0.45f, 0.75f), 0.95f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.35f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        private static AnimationCurve DefaultSunIntensity()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0.45f), new Keyframe(0.3f, 1.15f), new Keyframe(0.55f, 0.9f),
                new Keyframe(0.68f, 0.22f), new Keyframe(0.95f, 0.22f), new Keyframe(1f, 0.45f));
        }

        private static Gradient DefaultAmbient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.3f, 0.26f, 0.34f), 0f),
                    new GradientColorKey(new Color(0.42f, 0.45f, 0.52f), 0.3f),
                    new GradientColorKey(new Color(0.3f, 0.22f, 0.32f), 0.6f),
                    new GradientColorKey(new Color(0.07f, 0.06f, 0.14f), 0.7f),
                    new GradientColorKey(new Color(0.07f, 0.06f, 0.14f), 0.95f),
                    new GradientColorKey(new Color(0.3f, 0.26f, 0.34f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        private static Gradient DefaultFog()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.55f, 0.42f, 0.4f), 0f),
                    new GradientColorKey(new Color(0.45f, 0.55f, 0.52f), 0.3f),
                    new GradientColorKey(new Color(0.34f, 0.2f, 0.27f), 0.6f),
                    new GradientColorKey(new Color(0.05f, 0.04f, 0.1f), 0.7f),
                    new GradientColorKey(new Color(0.05f, 0.04f, 0.1f), 0.95f),
                    new GradientColorKey(new Color(0.55f, 0.42f, 0.4f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        private static AnimationCurve DefaultFogEnd()
        {
            return new AnimationCurve(
                new Keyframe(0f, 55f), new Keyframe(0.3f, 80f), new Keyframe(0.6f, 60f),
                new Keyframe(0.7f, 40f), new Keyframe(0.95f, 40f), new Keyframe(1f, 55f));
        }
    }
}
