using JurassicPark.Core;
using JurassicPark.Scene;
using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>
    /// A prop that yields a resource when the player interacts with it. Progress is per node so
    /// two players can work the same tree. Depleted nodes tint or hide and come back after the
    /// configured number of days, driven by the DayNightCycle's NewDay event.
    /// </summary>
    public sealed class ResourceNode : MonoBehaviour, IInteractable
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        // Serialized so nodes placed by the generator survive the scene save/load round trip.
        [SerializeField] private ResourceKind kind;
        [SerializeField] private int amount;
        [SerializeField] private GatherRule rule;
        [SerializeField] private Color baseTint = Color.white;
        [SerializeField] private PickupLibrary pickups;

        public ResourceKind Kind => kind;
        public ResourceStock Stock { get; private set; }
        public GatherRule Rule => rule;
        public int DepletedOnDay { get; private set; } = -1;

        public string Prompt => Kind == ResourceKind.Food ? "Pick" : "Gather";

        private Renderer spriteRenderer;
        private MaterialPropertyBlock block;
        private DayNightCycle cycle;
        private Vector3 spriteScale;
        private float punchUntil;

        public void Configure(ResourceKind newKind, int newAmount, GatherRule newRule, Color tint, PickupLibrary pickupLibrary = null)
        {
            pickups = pickupLibrary;
            kind = newKind;
            amount = newAmount;
            rule = newRule;
            baseTint = tint;
            Stock = new ResourceStock(amount, rule.hitsPerUnit);
        }

        private void Awake()
        {
            if (Stock == null)
            {
                Stock = new ResourceStock(amount, rule.hitsPerUnit);
            }

            spriteRenderer = GetComponentInChildren<MeshRenderer>();
            block = new MaterialPropertyBlock();
            if (spriteRenderer != null)
            {
                spriteScale = spriteRenderer.transform.localScale;
            }
        }

        private void OnEnable()
        {
            cycle = FindFirstObjectByType<DayNightCycle>();
            if (cycle != null)
            {
                cycle.NewDay += OnNewDay;
            }
        }

        private void OnDisable()
        {
            if (cycle != null)
            {
                cycle.NewDay -= OnNewDay;
            }
        }

        public bool CanInteract(GameObject actor)
        {
            ResourceInventory inv = actor.GetComponent<ResourceInventory>();
            return Stock != null && !Stock.Depleted && inv != null && inv.Space(Kind) > 0;
        }

        public void Interact(GameObject actor)
        {
            if (!CanInteract(actor))
            {
                return;
            }

            int units = Stock.Hit();
            Punch();
            NoiseBus.Emit(transform.position, 12f, actor);
            if (units > 0)
            {
                int accepted = actor.GetComponent<ResourceInventory>().Add(Kind, units);
                if (accepted < units && pickups != null)
                {
                    PickupFactory.Spawn(pickups, Kind, units - accepted, actor.transform.position + (transform.position - actor.transform.position).normalized * 0.6f);
                }
            }

            if (Stock.Depleted)
            {
                DepletedOnDay = cycle != null ? cycle.DayNumber : 0;
                ApplyDepletedLook(true);
            }
        }

        private void OnNewDay(int day)
        {
            if (Stock.Depleted && Rule.respawnDays > 0 && day - DepletedOnDay >= Rule.respawnDays)
            {
                Respawn();
            }
        }

        public void Respawn()
        {
            Stock.Refill();
            DepletedOnDay = -1;
            ApplyDepletedLook(false);
        }

        private void ApplyDepletedLook(bool depleted)
        {
            if (spriteRenderer == null)
            {
                return;
            }

            if (Rule.hideWhenDepleted)
            {
                spriteRenderer.enabled = !depleted;
                foreach (Collider c in GetComponents<Collider>())
                {
                    c.enabled = !depleted;
                }

                return;
            }

            spriteRenderer.GetPropertyBlock(block);
            block.SetColor(BaseColor, depleted ? baseTint * Rule.depletedTint : baseTint);
            spriteRenderer.SetPropertyBlock(block);
        }

        private void Punch()
        {
            punchUntil = Time.time + 0.12f;
            if (spriteRenderer != null)
            {
                spriteRenderer.transform.localScale = new Vector3(spriteScale.x * 1.06f, spriteScale.y * 0.94f, spriteScale.z);
            }
        }

        private void Update()
        {
            if (punchUntil > 0f && Time.time >= punchUntil && spriteRenderer != null)
            {
                spriteRenderer.transform.localScale = spriteScale;
                punchUntil = 0f;
            }
        }
    }
}
