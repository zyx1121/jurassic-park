using UnityEngine;

namespace JurassicPark.Dinosaurs
{
    [CreateAssetMenu(menuName = "Jurassic Park/Dinosaur Stats", fileName = "Dinosaur")]
    public sealed class DinosaurStats : ScriptableObject
    {
        [Header("Movement")]
        [Min(0f)] public float walkSpeed = 2f;
        [Min(0f)] public float chaseSpeed = 6f;
        [Min(0f)] public float acceleration = 20f;
        [Min(0f)] public float angularSpeed = 360f;

        [Header("Senses")]
        [Tooltip("Distance at which a target is noticed.")]
        [Min(0f)] public float sightRange = 12f;
        [Tooltip("Distance beyond which a chased target is lost.")]
        [Min(0f)] public float loseRange = 20f;

        [Header("Attack")]
        [Min(0f)] public float attackRange = 1.6f;
        [Min(0f)] public float biteDamage = 15f;
        [Tooltip("Seconds between starting the bite and the damage landing.")]
        [Min(0f)] public float biteWindUp = 0.35f;
        [Tooltip("Seconds after a bite before the next can start.")]
        [Min(0f)] public float biteCooldown = 1.2f;
        [Min(0f)] public float knockback = 3f;
    }
}
