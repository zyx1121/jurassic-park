using System.Collections;
using JurassicPark.Combat;
using JurassicPark.Core;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicPark.Dinosaurs
{
    public enum RaptorState
    {
        Idle,
        Chase,
        Attack,
        Dead,
    }

    /// <summary>
    /// Minimal raptor brain for the vertical slice: idle until a target enters sight, chase it on
    /// the NavMesh, bite with a wind-up when in range, lose it past the lose range. Patrol,
    /// senses and pack behavior land in M3. Runs on the host only in co-op.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public sealed class RaptorBrain : MonoBehaviour
    {
        [SerializeField] private DinosaurStats stats;
        [SerializeField] private SpriteSheetAnimator animator;
        [SerializeField] private string targetTag = "Player";

        public RaptorState State { get; private set; } = RaptorState.Idle;
        public Transform Target { get; private set; }
        public DinosaurStats Stats => stats;

        private NavMeshAgent agent;
        private Health health;
        private float nextBiteTime;
        private Coroutine bite;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            health.Died += OnDied;
            if (stats != null)
            {
                agent.speed = stats.walkSpeed;
                agent.acceleration = stats.acceleration;
                agent.angularSpeed = stats.angularSpeed;
                agent.stoppingDistance = Mathf.Max(0f, stats.attackRange - 0.3f);
            }
        }

        private void Update()
        {
            if (State == RaptorState.Dead || stats == null)
            {
                return;
            }

            if (Target == null)
            {
                Target = FindTarget();
            }

            float dist = Target != null ? Vector3.Distance(transform.position, Target.position) : float.MaxValue;
            RaptorState next = NextState(State, dist, stats, Target != null && TargetAlive());
            if (next != State)
            {
                Enter(next);
            }

            Tick(dist);
            Animate();
        }

        /// <summary>Pure transition table, so it can be unit tested without a scene.</summary>
        public static RaptorState NextState(RaptorState current, float distance, DinosaurStats s, bool targetAlive)
        {
            if (current == RaptorState.Dead)
            {
                return RaptorState.Dead;
            }

            if (!targetAlive)
            {
                return RaptorState.Idle;
            }

            switch (current)
            {
                case RaptorState.Idle:
                    return distance <= s.sightRange ? RaptorState.Chase : RaptorState.Idle;
                case RaptorState.Chase:
                    if (distance > s.loseRange) return RaptorState.Idle;
                    return distance <= s.attackRange ? RaptorState.Attack : RaptorState.Chase;
                case RaptorState.Attack:
                    return distance <= s.attackRange * 1.25f ? RaptorState.Attack : RaptorState.Chase;
                default:
                    return current;
            }
        }

        private void Enter(RaptorState next)
        {
            State = next;
            switch (next)
            {
                case RaptorState.Idle:
                    Target = null;
                    if (agent.isOnNavMesh) agent.ResetPath();
                    agent.speed = stats.walkSpeed;
                    break;
                case RaptorState.Chase:
                    agent.speed = stats.chaseSpeed;
                    agent.isStopped = false;
                    break;
                case RaptorState.Attack:
                    if (agent.isOnNavMesh) agent.ResetPath();
                    break;
            }
        }

        private void Tick(float dist)
        {
            switch (State)
            {
                case RaptorState.Chase:
                    if (agent.isOnNavMesh && Target != null)
                    {
                        agent.SetDestination(Target.position);
                    }
                    break;
                case RaptorState.Attack:
                    FaceTarget();
                    if (bite == null && Time.time >= nextBiteTime)
                    {
                        bite = StartCoroutine(Bite());
                    }
                    break;
            }
        }

        private IEnumerator Bite()
        {
            animator?.Play("Bite");
            yield return new WaitForSeconds(stats.biteWindUp);
            if (State == RaptorState.Attack && Target != null && Vector3.Distance(transform.position, Target.position) <= stats.attackRange * 1.25f)
            {
                if (Target.TryGetComponent(out IDamageable victim))
                {
                    Vector3 dir = (Target.position - transform.position).normalized;
                    victim.TakeDamage(new DamageInfo(stats.biteDamage, DamageType.Bite, gameObject, Target.position, dir * stats.knockback));
                }
            }

            nextBiteTime = Time.time + stats.biteCooldown;
            yield return new WaitForSeconds(Mathf.Max(0f, 0.4f - stats.biteWindUp));
            bite = null;
        }

        private void FaceTarget()
        {
            if (Target == null) return;
            Vector3 d = Target.position - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(d);
            }
        }

        private void Animate()
        {
            if (animator == null) return;
            Vector3 v = agent.velocity;
            Vector3 f = v.sqrMagnitude > 0.05f ? v : transform.forward;
            animator.Row = RowFor(new Vector2(f.x, f.z));
            if (bite == null)
            {
                animator.Play(v.sqrMagnitude > 0.05f ? "Walk" : "Idle");
            }
        }

        /// <summary>Sheet rows are Down, Left, Right, Up: pick by the dominant XZ axis of the facing.</summary>
        public static int RowFor(Vector2 xz)
        {
            if (Mathf.Abs(xz.x) >= Mathf.Abs(xz.y))
            {
                return xz.x < 0f ? 1 : 2;
            }

            return xz.y < 0f ? 0 : 3;
        }

        private Transform FindTarget()
        {
            GameObject[] candidates = GameObject.FindGameObjectsWithTag(targetTag);
            Transform best = null;
            float bestDist = stats.sightRange;
            for (int i = 0; i < candidates.Length; i++)
            {
                float d = Vector3.Distance(transform.position, candidates[i].transform.position);
                if (d <= bestDist && candidates[i].TryGetComponent(out IDamageable dmg) && dmg.IsAlive)
                {
                    bestDist = d;
                    best = candidates[i].transform;
                }
            }

            return best;
        }

        private bool TargetAlive()
        {
            return Target != null && (!Target.TryGetComponent(out IDamageable d) || d.IsAlive);
        }

        private void OnDied(DamageInfo info)
        {
            State = RaptorState.Dead;
            if (bite != null) StopCoroutine(bite);
            if (agent.isOnNavMesh) agent.ResetPath();
            agent.enabled = false;
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
            animator?.Play("Death");
        }
    }
}
