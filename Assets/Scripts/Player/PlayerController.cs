using System;
using JurassicPark.Core;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JurassicPark.Player
{
    /// <summary>
    /// WASD movement on a CharacterController, camera-relative on the XZ plane, with a
    /// four-way facing for the sprite. Attack and Build are raised as events for the combat
    /// and building systems; Interact finds the nearest IInteractable in range.
    /// Runs identically with or without a NetworkManager (single-player path).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private PlayerMovementConfig config;
        [SerializeField] private InputActionAsset controls;
        [SerializeField] private LayerMask interactMask = ~0;

        public event Action AttackPressed;
        public event Action BuildPressed;
        public event Action RotatePressed;
        public event Action<IInteractable> Interacted;

        public Facing Facing { get; private set; } = Facing.Down;
        public Vector3 Velocity => velocity;
        public bool IsMoving => new Vector2(velocity.x, velocity.z).sqrMagnitude > 0.01f;
        public bool IsSprinting { get; private set; }
        public PlayerMovementConfig Config => config;
        public float InteractionRange => config != null ? config.interactRadius : 1.5f;
        public Component InteractionTarget { get; set; }
        public bool InteractionSuppressed { get; set; }

        /// <summary>When set, replaces device input. Used by tests and by AI or network drivers.</summary>
        public Vector2? OverrideMove { get; set; }

        private CharacterController controller;
        private InputAction move;
        private InputAction attack;
        private InputAction interact;
        private InputAction build;
        private InputAction sprint;
        private InputAction rotate;
        private Vector3 velocity;
        private bool interactionRequested;
        private readonly Collider[] overlap = new Collider[64];
        private readonly RaycastHit[] interactionHits = new RaycastHit[32];
        private readonly List<Collider> targetColliders = new List<Collider>(8);

        /// <summary>Every player in the scene, local or remote, for HUD and minimap lookups.</summary>
        public static readonly List<PlayerController> All = new List<PlayerController>();

        private void OnDestroy() => All.Remove(this);

        private void Awake()
        {
            All.Add(this);
            controller = GetComponent<CharacterController>();
            if (controls != null)
            {
                InputActionMap map = controls.FindActionMap("Player", throwIfNotFound: true);
                move = map.FindAction("Move", true);
                attack = map.FindAction("Attack", true);
                interact = map.FindAction("Interact", true);
                build = map.FindAction("Build", true);
                sprint = map.FindAction("Sprint", true);
                rotate = map.FindAction("Rotate", false);
            }
        }

        private void OnEnable()
        {
            if (controls == null)
            {
                return;
            }

            controls.FindActionMap("Player").Enable();
            attack.performed += OnAttack;
            interact.performed += OnInteract;
            build.performed += OnBuild;
            if (rotate != null) rotate.performed += OnRotate;
        }

        private void OnDisable()
        {
            interactionRequested = false;
            if (controls == null)
            {
                return;
            }

            attack.performed -= OnAttack;
            interact.performed -= OnInteract;
            build.performed -= OnBuild;
            if (rotate != null) rotate.performed -= OnRotate;
            controls.FindActionMap("Player").Disable();
        }

        private void Update()
        {
            Vector2 input = OverrideMove ?? (move != null ? move.ReadValue<Vector2>() : Vector2.zero);
            IsSprinting = sprint != null && sprint.IsPressed();
            Step(input, Time.deltaTime);
            if (interactionRequested)
            {
                interactionRequested = false;
                TryInteract();
            }
        }

        /// <summary>One movement step. Public so tests and drivers can advance the player deterministically.</summary>
        public void Step(Vector2 input, float dt)
        {
            if (config == null || dt <= 0f)
            {
                return;
            }

            input = Vector2.ClampMagnitude(input, 1f);
            bool blocked = WorldInputBlockers.BlocksWorldInput;
            if (blocked) input = Vector2.zero;
            Vector3 wish = CameraRelative(input);
            float speed = IsSprinting ? config.sprintSpeed : config.walkSpeed;
            Vector3 target = wish * speed;

            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            horizontal = blocked ? Vector3.zero : Vector3.MoveTowards(horizontal, target, config.acceleration * dt);

            float vertical = controller.isGrounded && velocity.y < 0f ? -2f : velocity.y - config.gravity * dt;
            velocity = new Vector3(horizontal.x, vertical, horizontal.z);
            controller.Move(velocity * dt);

            Facing = FacingUtil.FromDirection(new Vector2(wish.x, wish.z), Facing);
        }

        private static Vector3 CameraRelative(Vector2 input)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                return new Vector3(input.x, 0f, input.y);
            }

            Vector3 forward = cam.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            Vector3 right = cam.transform.right;
            right.y = 0f;
            right.Normalize();
            return right * input.x + forward * input.y;
        }

        private void OnAttack(InputAction.CallbackContext _)
        {
            if (WorldInputBlockers.BlocksWorldInput ||
                (Mouse.current != null && WorldInputBlockers.BlocksPointer(Mouse.current.position.ReadValue()))) return;
            AttackPressed?.Invoke();
        }

        private void OnBuild(InputAction.CallbackContext _)
        {
            if (!WorldInputBlockers.BlocksWorldInput) BuildPressed?.Invoke();
        }

        private void OnRotate(InputAction.CallbackContext _)
        {
            if (!WorldInputBlockers.BlocksWorldInput) RotatePressed?.Invoke();
        }

        // Input callbacks precede Update; wait until WorldSelection has resolved this frame's pointer.
        private void OnInteract(InputAction.CallbackContext _)
        {
            if (!InteractionSuppressed && !WorldInputBlockers.BlocksWorldInput) interactionRequested = true;
        }

        /// <summary>Uses the displayed target, or the nearest usable object when nothing is pointed at.</summary>
        public IInteractable TryInteract()
        {
            if (InteractionSuppressed || WorldInputBlockers.BlocksWorldInput) return null;
            Component target = InteractionTarget != null ? InteractionTarget : FindNearestInteractable();
            if (!CanInteractWith(target)) return null;
            var interactable = (IInteractable)target;
            interactable.Interact(gameObject);
            Interacted?.Invoke(interactable);
            return interactable;
        }

        public Component FindNearestInteractable()
        {
            int n = Physics.OverlapSphereNonAlloc(transform.position, InteractionRange, overlap, interactMask, QueryTriggerInteraction.Collide);
            Collider[] candidates = overlap;
            if (n == overlap.Length)
            {
                candidates = Physics.OverlapSphere(transform.position, InteractionRange, interactMask, QueryTriggerInteraction.Collide);
                n = candidates.Length;
            }
            Component best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (!(candidates[i].GetComponentInParent<IInteractable>() is Component candidate) || !CanInteractWith(candidate)) continue;
                float d = InteractionDistance(candidate);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = candidate;
                }
            }
            return best;
        }

        public float InteractionDistance(Component target) => target == null
            ? float.PositiveInfinity
            : Vector3.Distance(transform.position, ClosestInteractionPoint(target, transform.position));

        public bool CanInteractWith(Component target)
        {
            if (target == null || !target.gameObject.activeInHierarchy ||
                !(target is IInteractable interactable) ||
                (target is Behaviour behaviour && !behaviour.isActiveAndEnabled)) return false;
            return InteractionDistance(target) <= InteractionRange &&
                interactable.CanInteract(gameObject) && HasClearInteractionPath(target);
        }

        public bool HasClearInteractionPath(Component target)
        {
            if (target == null) return false;
            float eyeHeight = config != null ? config.interactionEyeHeight : 0.9f;
            Vector3 from = transform.position + Vector3.up * eyeHeight;
            Vector3 delta = ClosestInteractionPoint(target, from) - from;
            if (delta.sqrMagnitude < 0.0001f) return true;
            Ray ray = new Ray(from, delta.normalized);
            int count = Physics.RaycastNonAlloc(ray, interactionHits, delta.magnitude, interactMask, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = interactionHits;
            if (count == hits.Length)
            {
                hits = Physics.RaycastAll(ray, delta.magnitude, interactMask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            for (int i = 0; i < count; i++)
            {
                Transform hit = hits[i].collider.transform;
                if (hit.IsChildOf(transform) || hit.IsChildOf(target.transform)) continue;
                return false;
            }
            return true;
        }

        private Vector3 ClosestInteractionPoint(Component target, Vector3 from)
        {
            // Canopy/see-through triggers must not extend a tree's interaction range.
            target.GetComponentsInChildren(false, targetColliders);
            Vector3 point = target.transform.position;
            float distance = float.PositiveInfinity;
            foreach (Collider c in targetColliders)
            {
                if (!c.enabled || c.isTrigger) continue;
                Vector3 candidate = c.ClosestPoint(from);
                float d = (candidate - from).sqrMagnitude;
                if (d < distance) { distance = d; point = candidate; }
            }
            return point;
        }
    }
}
