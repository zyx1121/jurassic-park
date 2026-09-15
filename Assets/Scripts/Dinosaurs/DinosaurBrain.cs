using System.Collections;
using JurassicPark.Building;
using JurassicPark.Combat;
using JurassicPark.Core;
using JurassicPark.Scene;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicPark.Dinosaurs
{
    public enum DinosaurState
    {
        Idle,
        Patrol,
        Investigate,
        Chase,
        Attack,
        Flee,
        Dead,
    }

    /// <summary>
    /// Generic dinosaur brain tuned by DinosaurStats. Idle waits, Patrol wanders around home,
    /// Investigate walks to a heard noise, Chase follows a seen target on the NavMesh (pack
    /// followers take flank slots), Attack bites with a wind-up (structures that block the way
    /// get bitten too), Flee runs when health is low. Runs on the host only in co-op.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    [RequireComponent(typeof(Health))]
    public sealed class DinosaurBrain : MonoBehaviour
    {
        [SerializeField] private DinosaurStats stats;
        [SerializeField] private SpriteSheetAnimator animator;
        [SerializeField] private string targetTag = "Player";

        public DinosaurState State { get; private set; } = DinosaurState.Idle;
        public Transform Target { get; private set; }
        public Structure BlockingStructure { get; private set; }
        public Vector3 NoisePoint { get; private set; }
        public Vector3 Home { get; private set; }
        public DinosaurStats Stats => stats;
        public Pack Pack { get; set; }
        public Health Health => health;

        private NavMeshAgent agent;
        private Health health;
        private DayNightCycle cycle;
        private float stateTimer;
        private float nextBiteTime;
        private float stuckTimer;
        private float lastProgressDistance;
        private Coroutine bite;
        private System.Random rng;

        private void Awake()
        {
            agent = GetComponent<NavMeshAgent>();
            health = GetComponent<Health>();
            health.Died += OnDied;
            health.Damaged += OnDamaged;
            Home = transform.position;
            rng = new System.Random(GetInstanceID());
            if (stats != null)
            {
                agent.speed = stats.walkSpeed;
                agent.acceleration = stats.acceleration;
                agent.angularSpeed = stats.angularSpeed;
                agent.stoppingDistance = Mathf.Max(0f, stats.attackRange - 0.3f);
            }
        }

        private void OnEnable()
        {
            NoiseBus.Emitted += OnNoise;
            cycle = FindFirstObjectByType<DayNightCycle>();
        }

        private void OnDisable()
        {
            NoiseBus.Emitted -= OnNoise;
        }

        private void Update()
        {
            if (State == DinosaurState.Dead || stats == null || !Authority.IsAuthority) return;

            stateTimer -= Time.deltaTime;
            Perceive();
            float dist = Target != null ? Vector3.Distance(transform.position, Target.position) : float.MaxValue;
            bool targetAlive = Target != null && (!Target.TryGetComponent(out IDamageable d) || d.IsAlive);
            bool lowHealth = stats.fleeHealthFraction > 0f && health.Normalized <= stats.fleeHealthFraction;
            DinosaurState next = NextState(State, dist, stats, targetAlive, lowHealth, stateTimer <= 0f, BlockingStructure != null);
            if (next != State) Enter(next);
            Tick(dist);
            Animate();
        }

        /// <summary>Pure transition table, unit tested.</summary>
        public static DinosaurState NextState(DinosaurState current, float distance, DinosaurStats s, bool targetAlive, bool lowHealth, bool timerDone, bool blocked)
        {
            if (current == DinosaurState.Dead) return DinosaurState.Dead;
            if (lowHealth && current != DinosaurState.Flee && targetAlive) return DinosaurState.Flee;

            switch (current)
            {
                case DinosaurState.Idle:
                    if (targetAlive && distance <= s.sightRange * s.firelightSightBonus) return DinosaurState.Chase;
                    return timerDone ? DinosaurState.Patrol : DinosaurState.Idle;
                case DinosaurState.Patrol:
                    if (targetAlive && distance <= s.sightRange * s.firelightSightBonus) return DinosaurState.Chase;
                    return timerDone ? DinosaurState.Idle : DinosaurState.Patrol;
                case DinosaurState.Investigate:
                    if (targetAlive && distance <= s.sightRange * s.firelightSightBonus) return DinosaurState.Chase;
                    return timerDone ? DinosaurState.Idle : DinosaurState.Investigate;
                case DinosaurState.Chase:
                    if (!targetAlive || distance > s.loseRange) return DinosaurState.Idle;
                    if (blocked || distance <= s.attackRange) return DinosaurState.Attack;
                    return DinosaurState.Chase;
                case DinosaurState.Attack:
                    if (!targetAlive) return DinosaurState.Idle;
                    if (blocked) return DinosaurState.Attack;
                    return distance <= s.attackRange * 1.25f ? DinosaurState.Attack : DinosaurState.Chase;
                case DinosaurState.Flee:
                    return timerDone ? DinosaurState.Idle : DinosaurState.Flee;
                default:
                    return current;
            }
        }

        private void Perceive()
        {
            // Followers inherit the pack's target
            if (Pack != null && Pack.Leader != this && Pack.SharedTarget != null && Target == null)
            {
                Target = Pack.SharedTarget;
                return;
            }

            if (Target == null || State == DinosaurState.Idle || State == DinosaurState.Patrol || State == DinosaurState.Investigate)
            {
                Transform seen = FindVisibleTarget();
                if (seen != null)
                {
                    Target = seen;
                    Pack?.ReportTarget(this, seen);
                }
            }
        }

        private Transform FindVisibleTarget()
        {
            GameObject[] candidates = GameObject.FindGameObjectsWithTag(targetTag);
            Transform best = null;
            float bestDist = float.MaxValue;
            Vector3 forward = agent.velocity.sqrMagnitude > 0.05f ? agent.velocity : transform.forward;
            bool night = cycle == null || cycle.Phase == DayPhase.Night || cycle.Phase == DayPhase.Dusk;
            for (int i = 0; i < candidates.Length; i++)
            {
                Transform t = candidates[i].transform;
                if (!candidates[i].TryGetComponent(out IDamageable dmg) || !dmg.IsAlive) continue;
                float mult = night && InFirelight(t.position) ? stats.firelightSightBonus : 1f;
                if (!Senses.CanSee(transform.position, forward, t.position, stats.sightRange, stats.sightHalfAngle, mult)) continue;
                float d = Vector3.Distance(transform.position, t.position);
                if (d < bestDist) { bestDist = d; best = t; }
            }

            return best;
        }

        private static bool InFirelight(Vector3 p)
        {
            foreach (CampfireLight fire in FindObjectsByType<CampfireLight>(FindObjectsSortMode.None))
            {
                Light l = fire.GetComponent<Light>();
                if (l != null && l.enabled && Vector3.Distance(fire.transform.position, p) <= l.range * 0.6f) return true;
            }

            return false;
        }

        private void OnNoise(NoiseBus.Noise noise)
        {
            if (State == DinosaurState.Dead || State == DinosaurState.Chase || State == DinosaurState.Attack || State == DinosaurState.Flee) return;
            if (noise.source == gameObject) return;
            if (!NoiseBus.Hears(transform.position, stats.hearingRadius, noise)) return;
            NoisePoint = noise.position;
            Enter(DinosaurState.Investigate);
        }

        private void OnDamaged(DamageInfo info, float applied)
        {
            // Being hit reveals the attacker even outside the sight cone
            if (info.Source != null && info.Source.CompareTag(targetTag) && Target == null)
            {
                Target = info.Source.transform;
                Pack?.ReportTarget(this, Target);
            }
        }

        private void Enter(DinosaurState next)
        {
            State = next;
            BlockingStructure = null;
            stuckTimer = 0f;
            switch (next)
            {
                case DinosaurState.Idle:
                    Target = null;
                    if (Pack != null && Pack.Leader == this) Pack.ReportTarget(this, null);
                    if (agent.isOnNavMesh) agent.ResetPath();
                    agent.speed = stats.walkSpeed;
                    stateTimer = Mathf.Lerp(stats.idleTimeMin, stats.idleTimeMax, (float)rng.NextDouble());
                    break;
                case DinosaurState.Patrol:
                    agent.speed = stats.walkSpeed;
                    agent.isStopped = false;
                    stateTimer = 12f;
                    Vector3 p = Home + new Vector3((float)(rng.NextDouble() * 2 - 1), 0f, (float)(rng.NextDouble() * 2 - 1)) * stats.patrolRadius;
                    if (agent.isOnNavMesh && NavMesh.SamplePosition(p, out NavMeshHit hit, 6f, NavMesh.AllAreas)) agent.SetDestination(hit.position);
                    else stateTimer = 0f;
                    break;
                case DinosaurState.Investigate:
                    agent.speed = stats.walkSpeed * 1.4f;
                    agent.isStopped = false;
                    stateTimer = stats.investigateTime + Vector3.Distance(transform.position, NoisePoint) / Mathf.Max(0.1f, agent.speed);
                    if (agent.isOnNavMesh) agent.SetDestination(NoisePoint);
                    break;
                case DinosaurState.Chase:
                    agent.speed = stats.chaseSpeed;
                    agent.isStopped = false;
                    lastProgressDistance = float.MaxValue;
                    break;
                case DinosaurState.Attack:
                    if (agent.isOnNavMesh) agent.ResetPath();
                    break;
                case DinosaurState.Flee:
                    agent.speed = stats.chaseSpeed;
                    agent.isStopped = false;
                    stateTimer = stats.fleeTime;
                    Vector3 away = Target != null ? (transform.position - Target.position).normalized : Random.insideUnitSphere;
                    away.y = 0f;
                    Vector3 fleeTo = transform.position + away.normalized * stats.fleeDistance;
                    if (agent.isOnNavMesh && NavMesh.SamplePosition(fleeTo, out NavMeshHit fh, 8f, NavMesh.AllAreas)) agent.SetDestination(fh.position);
                    break;
            }
        }

        private void Tick(float dist)
        {
            switch (State)
            {
                case DinosaurState.Patrol:
                    if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance < 0.5f) stateTimer = 0f;
                    break;
                case DinosaurState.Investigate:
                    if (agent.isOnNavMesh && !agent.pathPending && agent.remainingDistance < 0.8f && agent.hasPath) agent.ResetPath();
                    break;
                case DinosaurState.Chase:
                    if (!agent.isOnNavMesh || Target == null) break;
                    Vector3 goal = Target.position;
                    if (Pack != null && Pack.Leader != this && dist > stats.flankRadius + 0.5f)
                    {
                        int idx = Pack.IndexOf(this);
                        Vector3 approach = Pack.Leader != null ? (Target.position - Pack.Leader.transform.position) : (Target.position - transform.position);
                        goal = Senses.FlankSlot(idx, Pack.Members.Count, Target.position, approach, stats.flankRadius, stats.flankAngle);
                    }

                    agent.SetDestination(goal);
                    DetectBlocked(dist);
                    break;
                case DinosaurState.Attack:
                    FaceTarget(BlockingStructure != null ? BlockingStructure.transform.position : (Target != null ? Target.position : transform.position));
                    if (BlockingStructure != null && (BlockingStructure.Health == null || !BlockingStructure.Health.IsAlive)) BlockingStructure = null;
                    if (bite == null && Time.time >= nextBiteTime) bite = StartCoroutine(Bite());
                    break;
            }
        }

        /// <summary>No progress toward the target for a while: look for a structure in the way and bite it.</summary>
        private void DetectBlocked(float dist)
        {
            if (dist < lastProgressDistance - 0.15f)
            {
                lastProgressDistance = dist;
                stuckTimer = 0f;
                return;
            }

            stuckTimer += Time.deltaTime;
            bool partial = agent.pathStatus != NavMeshPathStatus.PathComplete;
            if (stuckTimer < stats.stuckTime && !partial) return;
            if (stuckTimer < stats.stuckTime * 0.5f) return;

            Vector3 dir = (Target.position - transform.position).normalized;
            if (Physics.SphereCast(transform.position + Vector3.up * 0.6f, 0.5f, dir, out RaycastHit hit, 2.5f, ~0, QueryTriggerInteraction.Ignore))
            {
                Structure s = hit.collider.GetComponentInParent<Structure>();
                if (s != null && s.Health != null && s.Health.IsAlive)
                {
                    BlockingStructure = s;
                }
            }
        }

        private IEnumerator Bite()
        {
            animator?.Play("Bite");
            yield return new WaitForSeconds(stats.biteWindUp);
            if (State == DinosaurState.Attack)
            {
                if (BlockingStructure != null)
                {
                    if (Vector3.Distance(transform.position, BlockingStructure.transform.position) <= stats.attackRange * 2f)
                    {
                        BlockingStructure.Health.TakeDamage(new DamageInfo(stats.biteDamage * stats.structureDamageMultiplier, DamageType.Bite, gameObject, BlockingStructure.transform.position));
                    }
                }
                else if (Target != null && Vector3.Distance(transform.position, Target.position) <= stats.attackRange * 1.25f && Target.TryGetComponent(out IDamageable victim))
                {
                    Vector3 dir = (Target.position - transform.position).normalized;
                    victim.TakeDamage(new DamageInfo(stats.biteDamage, DamageType.Bite, gameObject, Target.position, dir * stats.knockback));
                }
            }

            nextBiteTime = Time.time + stats.biteCooldown;
            yield return new WaitForSeconds(Mathf.Max(0f, 0.4f - stats.biteWindUp));
            bite = null;
        }

        private void FaceTarget(Vector3 point)
        {
            Vector3 d = point - transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f) transform.rotation = Quaternion.LookRotation(d);
        }

        private void Animate()
        {
            if (animator == null) return;
            Vector3 v = agent.velocity;
            Vector3 f = v.sqrMagnitude > 0.05f ? v : transform.forward;
            animator.Row = RowFor(new Vector2(f.x, f.z));
            if (bite == null) animator.Play(v.sqrMagnitude > 0.05f ? "Walk" : "Idle");
        }

        public static int RowFor(Vector2 xz)
        {
            if (Mathf.Abs(xz.x) >= Mathf.Abs(xz.y)) return xz.x < 0f ? 1 : 2;
            return xz.y < 0f ? 0 : 3;
        }

        private void OnDied(DamageInfo info)
        {
            State = DinosaurState.Dead;
            if (bite != null) StopCoroutine(bite);
            if (agent.isOnNavMesh) agent.ResetPath();
            agent.enabled = false;
            foreach (Collider c in GetComponentsInChildren<Collider>()) c.enabled = false;
            animator?.Play("Death");
            Pack?.Prune();
        }

        /// <summary>Test and tooling hook: force a state with an optional target.</summary>
        public void ForceState(DinosaurState state, Transform target = null)
        {
            Target = target;
            Enter(state);
        }
    }
}
