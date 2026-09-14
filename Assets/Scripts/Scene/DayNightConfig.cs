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
                new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1.6f), new Keyframe(0.55f, 1.2f),
                new Keyframe(0.68f, 0.35f), new Keyframe(0.95f, 0.35f), new Keyframe(1f, 0.5f));
        }

        private static Gradient DefaultAmbient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.45f, 0.38f, 0.45f), 0f),
                    new GradientColorKey(new Color(0.6f, 0.62f, 0.7f), 0.3f),
                    new GradientColorKey(new Color(0.4f, 0.32f, 0.42f), 0.6f),
                    new GradientColorKey(new Color(0.16f, 0.14f, 0.26f), 0.7f),
                    new GradientColorKey(new Color(0.16f, 0.14f, 0.26f), 0.95f),
                    new GradientColorKey(new Color(0.45f, 0.38f, 0.45f), 1f),
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
                    new GradientColorKey(new Color(0.75f, 0.55f, 0.5f), 0f),
                    new GradientColorKey(new Color(0.65f, 0.75f, 0.7f), 0.3f),
                    new GradientColorKey(new Color(0.6f, 0.35f, 0.4f), 0.6f),
                    new GradientColorKey(new Color(0.13f, 0.1f, 0.22f), 0.7f),
                    new GradientColorKey(new Color(0.13f, 0.1f, 0.22f), 0.95f),
                    new GradientColorKey(new Color(0.75f, 0.55f, 0.5f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        private static AnimationCurve DefaultFogEnd()
        {
            return new AnimationCurve(
                new Keyframe(0f, 60f), new Keyframe(0.3f, 110f), new Keyframe(0.6f, 70f),
                new Keyframe(0.7f, 42f), new Keyframe(0.95f, 42f), new Keyframe(1f, 60f));
        }
    }
}
