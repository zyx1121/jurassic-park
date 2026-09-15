using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Core
{
    /// <summary>
    /// What one player carries: stacked resources with per-kind caps, plus the ids of boat parts
    /// (unique items, one at a time). Add returns how much fit so callers can drop the rest.
    /// </summary>
    public sealed class ResourceInventory : MonoBehaviour
    {
        [SerializeField] private InventoryConfig config;

        private readonly Dictionary<ResourceKind, int> counts = new Dictionary<ResourceKind, int>();
        private readonly List<int> boatParts = new List<int>();

        public event Action<ResourceKind, int, int> Changed; // kind, delta, new total
        public event Action<int> BoatPartChanged; // part id, -1 when removed

        public InventoryConfig Config => config;
        public IReadOnlyList<int> BoatParts => boatParts;

        public void Configure(InventoryConfig cfg) => config = cfg;

        public int Get(ResourceKind kind) => kind == ResourceKind.BoatPart ? boatParts.Count : counts.TryGetValue(kind, out int n) ? n : 0;

        public int Cap(ResourceKind kind) => config != null ? config.Cap(kind) : int.MaxValue;

        public int Space(ResourceKind kind) => Mathf.Max(0, Cap(kind) - Get(kind));

        /// <summary>Adds up to the cap. Returns the amount accepted.</summary>
        public int Add(ResourceKind kind, int amount)
        {
            if (kind == ResourceKind.None || kind == ResourceKind.BoatPart || amount <= 0)
            {
                return 0;
            }

            int accepted = Mathf.Min(amount, Space(kind));
            if (accepted <= 0)
            {
                return 0;
            }

            counts[kind] = Get(kind) + accepted;
            Changed?.Invoke(kind, accepted, counts[kind]);
            return accepted;
        }

        public bool TryTake(ResourceKind kind, int amount)
        {
            if (kind == ResourceKind.BoatPart || amount <= 0 || Get(kind) < amount)
            {
                return false;
            }

            counts[kind] -= amount;
            Changed?.Invoke(kind, -amount, counts[kind]);
            return true;
        }

        public bool Has(ResourceKind kind, int amount) => Get(kind) >= amount;

        public bool HasBoatPart(int partId) => boatParts.Contains(partId);

        /// <summary>Boat parts are unique: the same id can never be carried twice, and the cap limits how many at once.</summary>
        public bool TryAddBoatPart(int partId)
        {
            if (boatParts.Contains(partId) || boatParts.Count >= Cap(ResourceKind.BoatPart))
            {
                return false;
            }

            boatParts.Add(partId);
            BoatPartChanged?.Invoke(partId);
            Changed?.Invoke(ResourceKind.BoatPart, 1, boatParts.Count);
            return true;
        }

        public bool TryRemoveBoatPart(int partId)
        {
            if (!boatParts.Remove(partId))
            {
                return false;
            }

            BoatPartChanged?.Invoke(-1);
            Changed?.Invoke(ResourceKind.BoatPart, -1, boatParts.Count);
            return true;
        }

        /// <summary>Empties the inventory and reports what was carried, so the caller can spawn pickups.</summary>
        public List<(ResourceKind kind, int amount)> TakeEverything()
        {
            var dropped = new List<(ResourceKind, int)>();
            foreach (var kv in counts)
            {
                if (kv.Value > 0) dropped.Add((kv.Key, kv.Value));
            }

            foreach (int id in boatParts)
            {
                dropped.Add((ResourceKind.BoatPart, id));
            }

            counts.Clear();
            boatParts.Clear();
            foreach (var d in dropped)
            {
                Changed?.Invoke(d.Item1, -d.Item2, 0);
            }

            return dropped;
        }
    }
}
