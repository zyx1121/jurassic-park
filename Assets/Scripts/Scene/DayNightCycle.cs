using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace JurassicPark.Scene
{
    public enum DayPhase
    {
        Dawn,
        Day,
        Dusk,
        Night,
    }

    /// <summary>
    /// One clock for the whole world. Drives the sun, ambient light and fog every frame and
    /// exposes normalized time and the current phase for spawn tables, music and the HUD.
    /// Host-authoritative later: only the host advances time, clients receive it.
    /// </summary>
    public sealed class DayNightCycle : MonoBehaviour
    {
        [SerializeField] private DayNightConfig config;
        [SerializeField] private Light sun;
        [Tooltip("Normalized start time; 0 = dawn, 0.25 = noon, 0.5 = dusk, 0.75 = midnight.")]
        [Range(0f, 1f)] [SerializeField] private float startTime = 0.15f;
        [SerializeField] private bool advance = true;

        /// <summary>0 at dawn, wraps at 1.</summary>
        public float NormalizedTime { get; private set; }
        public int DayNumber { get; private set; } = 1;
        public DayPhase Phase { get; private set; }
        public DayNightConfig Config => config;

        public event Action<DayPhase> PhaseChanged;
        public event Action<int> NewDay;

        private void Awake()
        {
            NormalizedTime = startTime;
            Phase = PhaseAt(NormalizedTime, config);
            Apply();
        }

        private void Update()
        {
            if (config == null)
            {
                return;
            }

            if (advance && Application.isPlaying && JurassicPark.Core.Authority.IsAuthority)
            {
                Advance(Time.deltaTime / config.dayLengthSeconds);
            }

            Apply();
        }

        /// <summary>Moves the clock by a normalized amount. Public so tests and the host can drive it.</summary>
        public void Advance(float normalizedDelta)
        {
            float t = NormalizedTime + normalizedDelta;
            if (t >= 1f)
            {
                t -= Mathf.Floor(t);
                DayNumber++;
                NewDay?.Invoke(DayNumber);
            }

            NormalizedTime = t;
            DayPhase phase = PhaseAt(t, config);
            if (phase != Phase)
            {
                Phase = phase;
                PhaseChanged?.Invoke(phase);
            }
        }

        public void SetTime(float normalized)
        {
            NormalizedTime = Mathf.Repeat(normalized, 1f);
            Phase = PhaseAt(NormalizedTime, config);
            Apply();
        }

        public static DayPhase PhaseAt(float t, DayNightConfig c)
        {
            if (c == null)
            {
                return DayPhase.Day;
            }

            t = Mathf.Repeat(t, 1f);
            if (t < c.dayStart) return DayPhase.Dawn;
            if (t < c.duskStart) return DayPhase.Day;
            if (t < c.nightStart) return DayPhase.Dusk;
            return DayPhase.Night;
        }

        private void Apply()
        {
            if (config == null)
            {
                return;
            }

            float t = NormalizedTime;
            if (sun != null)
            {
                sun.transform.rotation = Quaternion.Euler(config.sunPitch.Evaluate(t), -35f, 0f);
                sun.color = config.sunColor.Evaluate(t);
                sun.intensity = config.sunIntensity.Evaluate(t);
                sun.shadowStrength = 0.72f;
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = config.ambientColor.Evaluate(t);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = config.fogColor.Evaluate(t);
            float end = config.fogEnd.Evaluate(t);
            RenderSettings.fogEndDistance = end;
            RenderSettings.fogStartDistance = end * 0.4f; // 28/65 day, 22/55 dusk, 16/44 night per the art direction
            Camera cam = Camera.main;
            if (cam != null)
            {
                cam.backgroundColor = RenderSettings.fogColor;
            }
        }
    }
}
