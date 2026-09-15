using System;
using JurassicPark.Core;
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
        public event Action<IInteractable> Interacted;

        public Facing Facing { get; private set; } = Facing.Down;
        public Vector3 Velocity => velocity;
        public bool IsMoving => new Vector2(velocity.x, velocity.z).sqrMagnitude > 0.01f;
        public bool IsSprinting { get; private set; }
        public PlayerMovementConfig Config => config;

        /// <summary>When set, replaces device input. Used by tests and by AI or network drivers.</summary>
        public Vector2? OverrideMove { get; set; }

        private CharacterController controller;
        private InputAction move;
        private InputAction attack;
        private InputAction interact;
        private InputAction build;
        private InputAction sprint;
        private Vector3 velocity;
        private readonly Collider[] overlap = new Collider[16];

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            if (controls != null)
            {
                InputActionMap map = controls.FindActionMap("Player", throwIfNotFound: true);
                move = map.FindAction("Move", true);
                attack = map.FindAction("Attack", true);
                interact = map.FindAction("Interact", true);
                build = map.FindAction("Build", true);
                sprint = map.FindAction("Sprint", true);
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
        }

        private void OnDisable()
        {
            if (controls == null)
            {
                return;
            }

            attack.performed -= OnAttack;
            interact.performed -= OnInteract;
            build.performed -= OnBuild;
            controls.FindActionMap("Player").Disable();
        }

        private void Update()
        {
            Vector2 input = OverrideMove ?? (move != null ? move.ReadValue<Vector2>() : Vector2.zero);
            IsSprinting = sprint != null && sprint.IsPressed();
            Step(input, Time.deltaTime);
        }

        /// <summary>One movement step. Public so tests and drivers can advance the player deterministically.</summary>
        public void Step(Vector2 input, float dt)
        {
            if (config == null || dt <= 0f)
            {
                return;
            }

            input = Vector2.ClampMagnitude(input, 1f);
            Vector3 wish = CameraRelative(input);
            float speed = IsSprinting ? config.sprintSpeed : config.walkSpeed;
            Vector3 target = wish * speed;

            Vector3 horizontal = new Vector3(velocity.x, 0f, velocity.z);
            horizontal = Vector3.MoveTowards(horizontal, target, config.acceleration * dt);

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

        private void OnAttack(InputAction.CallbackContext _) => AttackPressed?.Invoke();

        private void OnBuild(InputAction.CallbackContext _) => BuildPressed?.Invoke();

        private void OnInteract(InputAction.CallbackContext _) => TryInteract();

        /// <summary>Interacts with the nearest interactable in range. Returns the target, or null.</summary>
        public IInteractable TryInteract()
        {
            float radius = config != null ? config.interactRadius : 1.5f;
            int n = Physics.OverlapSphereNonAlloc(transform.position, radius, overlap, interactMask, QueryTriggerInteraction.Collide);
            IInteractable best = null;
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (!overlap[i].TryGetComponent(out IInteractable candidate) || !candidate.CanInteract(gameObject))
                {
                    continue;
                }

                float d = (overlap[i].transform.position - transform.position).sqrMagnitude;
                if (d < bestDist)
                {
                    bestDist = d;
                    best = candidate;
                }
            }

            if (best != null)
            {
                best.Interact(gameObject);
                Interacted?.Invoke(best);
            }

            return best;
        }
    }
}
