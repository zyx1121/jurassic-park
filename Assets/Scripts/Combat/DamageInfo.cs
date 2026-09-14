using UnityEngine;

namespace JurassicPark.Combat
{
    public enum DamageType
    {
        Melee,
        Bite,
        Thrown,
        Fire,
        Fall,
    }

    /// <summary>One hit. Structures ignore some types (a bite cannot hurt stone), so the type travels with the hit.</summary>
    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly DamageType Type;
        public readonly GameObject Source;
        public readonly Vector3 HitPoint;
        public readonly Vector3 Knockback;

        public DamageInfo(float amount, DamageType type, GameObject source, Vector3 hitPoint, Vector3 knockback = default)
        {
            Amount = amount;
            Type = type;
            Source = source;
            HitPoint = hitPoint;
            Knockback = knockback;
        }
    }

    public interface IDamageable
    {
        bool IsAlive { get; }

        /// <summary>Returns the damage actually applied after armor and invulnerability.</summary>
        float TakeDamage(in DamageInfo info);
    }
}
