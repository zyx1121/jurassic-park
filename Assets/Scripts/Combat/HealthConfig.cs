using UnityEngine;

namespace JurassicPark.Combat
{
    [CreateAssetMenu(menuName = "Jurassic Park/Health Config", fileName = "Health")]
    public sealed class HealthConfig : ScriptableObject
    {
        [Min(1f)] public float maxHealth = 100f;
        [Tooltip("Flat damage removed from every hit before it lands.")]
        [Min(0f)] public float armor = 0f;
        [Tooltip("Seconds after a hit during which further hits are ignored. 0 disables.")]
        [Min(0f)] public float invulnerabilityAfterHit = 0f;
        [Tooltip("Damage types this thing ignores entirely, e.g. stone walls ignore bites.")]
        public DamageType[] immuneTo = new DamageType[0];
    }
}
