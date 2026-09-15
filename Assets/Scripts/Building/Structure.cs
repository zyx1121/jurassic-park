using JurassicPark.Combat;
using JurassicPark.Core;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicPark.Building
{
    /// <summary>
    /// A placed structure: knows its definition and grid pose, carries Health, carves the NavMesh
    /// while solid, can be repaired with Interact, and gates toggle open and closed.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class Structure : MonoBehaviour, IInteractable
    {
        [SerializeField] private StructureDef def;
        [SerializeField] private int rotationSteps;
        [SerializeField] private Vector3 gridCenter;
        [SerializeField] private bool open;

        public StructureDef Def => def;
        public int RotationSteps => rotationSteps;
        public Vector3 GridCenter => gridCenter;
        public bool IsOpen => open;
        public Health Health { get; private set; }
        public string Prompt => def != null && def.isGate ? (open ? "Close" : "Open") : "Repair";

        private NavMeshObstacle obstacle;
        private Collider[] colliders;
        private Renderer[] renderers;

        public void Configure(StructureDef newDef, Vector3 center, int rotation)
        {
            def = newDef;
            gridCenter = center;
            rotationSteps = rotation;
        }

        private void Awake()
        {
            Health = GetComponent<Health>();
            Health.Died += OnDied;
            obstacle = GetComponent<NavMeshObstacle>();
            colliders = GetComponentsInChildren<Collider>();
            renderers = GetComponentsInChildren<Renderer>();
            ApplyOpenState();
        }

        public bool CanInteract(GameObject actor)
        {
            if (def == null || !Health.IsAlive) return false;
            if (def.isGate) return true;
            ResourceInventory inv = actor.GetComponent<ResourceInventory>();
            return Health.Current < Health.Max && inv != null;
        }

        public void Interact(GameObject actor)
        {
            if (def.isGate)
            {
                open = !open;
                ApplyOpenState();
                return;
            }

            ResourceInventory inv = actor.GetComponent<ResourceInventory>();
            if (inv != null && Health.Current < Health.Max && BuildGrid.Pay(inv, def, def.repairCostFraction))
            {
                Health.Heal(Health.Max);
            }
        }

        private void ApplyOpenState()
        {
            bool blocks = def != null && def.solid && !open;
            if (obstacle != null) obstacle.enabled = blocks;
            foreach (Collider c in colliders)
            {
                if (!c.isTrigger) c.enabled = blocks || !def.isGate;
            }

            if (def != null && def.isGate)
            {
                // Swing the gate leaf: rotate the visual 80 degrees when open
                Transform leaf = transform.Find("Leaf");
                if (leaf != null) leaf.localRotation = Quaternion.Euler(0f, open ? 80f : 0f, 0f);
            }
        }

        private void OnDied(DamageInfo info)
        {
            Destroy(gameObject, 0.05f);
        }
    }
}
