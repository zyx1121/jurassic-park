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
        [Tooltip("Distance at which a target in the sight cone is noticed.")]
        [Min(0f)] public float sightRange = 12f;
        [Tooltip("Half angle of the sight cone in degrees; 180 sees all around.")]
        [Range(10f, 180f)] public float sightHalfAngle = 70f;
        [Tooltip("Sight range multiplier for targets standing in firelight at night.")]
        [Min(1f)] public float firelightSightBonus = 1.6f;
        [Tooltip("Noises inside this radius are investigated.")]
        [Min(0f)] public float hearingRadius = 18f;
        [Tooltip("Distance beyond which a chased target is lost.")]
        [Min(0f)] public float loseRange = 22f;

        [Header("Patrol")]
        [Min(0f)] public float patrolRadius = 15f;
        [Min(0f)] public float idleTimeMin = 2f;
        [Min(0f)] public float idleTimeMax = 5f;
        [Tooltip("Seconds spent looking around at a noise before giving up.")]
        [Min(0f)] public float investigateTime = 4f;

        [Header("Attack")]
        [Min(0f)] public float attackRange = 1.6f;
        [Min(0f)] public float biteDamage = 15f;
        [Tooltip("Seconds between starting the bite and the damage landing.")]
        [Min(0f)] public float biteWindUp = 0.35f;
        [Tooltip("Seconds after a bite before the next can start.")]
        [Min(0f)] public float biteCooldown = 1.2f;
        [Min(0f)] public float knockback = 3f;
        [Tooltip("Damage multiplier against structures blocking the way.")]
        [Min(0f)] public float structureDamageMultiplier = 1f;
        [Tooltip("Seconds of no progress toward the target before attacking whatever blocks the path.")]
        [Min(0.2f)] public float stuckTime = 1.2f;

        [Header("Flee")]
        [Tooltip("Health fraction below which the dinosaur runs away. 0 never flees.")]
        [Range(0f, 1f)] public float fleeHealthFraction = 0.25f;
        [Min(0f)] public float fleeDistance = 20f;
        [Min(0f)] public float fleeTime = 6f;

        [Header("Pack")]
        [Tooltip("Distance from the target at which followers hold their flank slot before closing in.")]
        [Min(0f)] public float flankRadius = 4f;
        [Tooltip("Angle between the leader's approach and each follower's slot, in degrees.")]
        [Range(0f, 180f)] public float flankAngle = 110f;
    }
}
