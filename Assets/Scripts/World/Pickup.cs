using JurassicPark.Core;
using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>
    /// A resource lying on the ground. Walks into a player's trigger radius and it is collected
    /// automatically; whatever does not fit stays on the ground. Boat parts carry a unique id.
    /// </summary>
    public sealed class Pickup : MonoBehaviour, IInteractable
    {
        [SerializeField] private ResourceKind kind = ResourceKind.Wood;
        [SerializeField] private int amount = 1;
        [SerializeField] private int boatPartId = -1;
        [SerializeField] private bool autoCollect = true;
        [SerializeField] private float bobHeight = 0.08f;
        [SerializeField] private float bobSpeed = 2.5f;

        public ResourceKind Kind => kind;
        public int Amount => amount;
        public int BoatPartId => boatPartId;
        public string Prompt => "Take";

        private Vector3 restPosition;
        private float phase;

        public void Configure(ResourceKind newKind, int newAmount, int partId = -1)
        {
            kind = newKind;
            amount = newAmount;
            boatPartId = partId;
        }

        private void Awake()
        {
            restPosition = transform.position;
            phase = Random.value * 6.28f;
        }

        private void Update()
        {
            transform.position = restPosition + Vector3.up * (Mathf.Sin(Time.time * bobSpeed + phase) * bobHeight + bobHeight);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (autoCollect && other.TryGetComponent(out ResourceInventory inv))
            {
                TryCollect(inv);
            }
        }

        public bool CanInteract(GameObject actor)
        {
            ResourceInventory inv = actor.GetComponent<ResourceInventory>();
            return amount > 0 && inv != null && inv.Space(kind) > 0
                && (kind != ResourceKind.BoatPart || !inv.HasBoatPart(boatPartId));
        }

        public void Interact(GameObject actor)
        {
            if (actor.TryGetComponent(out ResourceInventory inv))
            {
                TryCollect(inv);
            }
        }

        /// <summary>Moves as much as fits into the inventory. Destroys the pickup when empty.</summary>
        public int TryCollect(ResourceInventory inv)
        {
            int taken;
            if (kind == ResourceKind.BoatPart)
            {
                taken = inv.TryAddBoatPart(boatPartId) ? 1 : 0;
                amount -= taken;
            }
            else
            {
                taken = inv.Add(kind, amount);
                amount -= taken;
            }

            if (amount <= 0)
            {
                Destroy(gameObject);
            }

            return taken;
        }
    }
}
