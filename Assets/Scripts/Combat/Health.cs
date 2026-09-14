using System;
using UnityEngine;

namespace JurassicPark.Combat
{
    /// <summary>
    /// Hit points for players, dinosaurs and structures. Owners subscribe to Damaged and Died and
    /// decide what death means (ragdoll, downed state, rubble). Host-authoritative later: only the
    /// host applies damage, clients mirror Current.
    /// </summary>
    public sealed class Health : MonoBehaviour, IDamageable
    {
        [SerializeField] private HealthConfig config;

        public event Action<DamageInfo, float> Damaged;
        public event Action<DamageInfo> Died;
        public event Action<float> Healed;

        public float Current { get; private set; }
        public float Max => config != null ? config.maxHealth : 1f;
        public float Normalized => Max > 0f ? Current / Max : 0f;
        public bool IsAlive => Current > 0f;
        public HealthConfig Config => config;

        /// <summary>Clock used for the invulnerability window; tests inject their own.</summary>
        public Func<float> Clock = () => Time.time;

        private float lastHitTime = float.NegativeInfinity;

        private void Awake()
        {
            ResetToMax();
        }

        public void Configure(HealthConfig newConfig)
        {
            config = newConfig;
            ResetToMax();
        }

        public void ResetToMax()
        {
            Current = Max;
            lastHitTime = float.NegativeInfinity;
        }

        public float TakeDamage(in DamageInfo info)
        {
            if (!IsAlive || config == null || info.Amount <= 0f)
            {
                return 0f;
            }

            if (Array.IndexOf(config.immuneTo, info.Type) >= 0)
            {
                return 0f;
            }

            float now = Clock();
            if (config.invulnerabilityAfterHit > 0f && now - lastHitTime < config.invulnerabilityAfterHit)
            {
                return 0f;
            }

            float applied = Mathf.Max(0f, info.Amount - config.armor);
            if (applied <= 0f)
            {
                return 0f;
            }

            lastHitTime = now;
            Current = Mathf.Max(0f, Current - applied);
            Damaged?.Invoke(info, applied);
            if (Current <= 0f)
            {
                Died?.Invoke(info);
            }

            return applied;
        }

        public float Heal(float amount)
        {
            if (!IsAlive || amount <= 0f)
            {
                return 0f;
            }

            float before = Current;
            Current = Mathf.Min(Max, Current + amount);
            float healed = Current - before;
            if (healed > 0f)
            {
                Healed?.Invoke(healed);
            }

            return healed;
        }
    }
}
