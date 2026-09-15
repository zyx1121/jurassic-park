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
                    new GradientColorKey(new Color(1.000f, 0.620f, 0.404f), 0f),
                    new GradientColorKey(new Color(1.000f, 0.878f, 0.659f), 0.3f),
                    new GradientColorKey(new Color(1.000f, 0.620f, 0.404f), 0.6f),
                    new GradientColorKey(new Color(0.659f, 0.718f, 1.000f), 0.7f),
                    new GradientColorKey(new Color(0.659f, 0.718f, 1.000f), 0.95f),
                    new GradientColorKey(new Color(1.000f, 0.620f, 0.404f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        private static AnimationCurve DefaultSunIntensity()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0.85f), new Keyframe(0.3f, 1.25f), new Keyframe(0.55f, 1.0f),
                new Keyframe(0.68f, 0.35f), new Keyframe(0.95f, 0.35f), new Keyframe(1f, 0.85f));
        }

        private static Gradient DefaultAmbient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.188f, 0.165f, 0.282f), 0f),
                    new GradientColorKey(new Color(0.275f, 0.337f, 0.365f), 0.3f),
                    new GradientColorKey(new Color(0.188f, 0.165f, 0.282f), 0.6f),
                    new GradientColorKey(new Color(0.090f, 0.102f, 0.192f), 0.7f),
                    new GradientColorKey(new Color(0.090f, 0.102f, 0.192f), 0.95f),
                    new GradientColorKey(new Color(0.188f, 0.165f, 0.282f), 1f),
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
                    new GradientColorKey(new Color(0.216f, 0.192f, 0.286f), 0f),
                    new GradientColorKey(new Color(0.388f, 0.447f, 0.471f), 0.3f),
                    new GradientColorKey(new Color(0.216f, 0.192f, 0.286f), 0.6f),
                    new GradientColorKey(new Color(0.086f, 0.098f, 0.169f), 0.7f),
                    new GradientColorKey(new Color(0.086f, 0.098f, 0.169f), 0.95f),
                    new GradientColorKey(new Color(0.216f, 0.192f, 0.286f), 1f),
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }

        private static AnimationCurve DefaultFogEnd()
        {
            return new AnimationCurve(
                new Keyframe(0f, 55f), new Keyframe(0.3f, 65f), new Keyframe(0.6f, 55f),
                new Keyframe(0.7f, 44f), new Keyframe(0.95f, 44f), new Keyframe(1f, 55f));
        }
    }
}
