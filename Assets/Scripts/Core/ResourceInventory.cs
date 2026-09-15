using System;
using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.Core
{
    /// <summary>Counts of each resource a player carries. The full item inventory (#20) builds on this.</summary>
    public sealed class ResourceInventory : MonoBehaviour
    {
        private readonly Dictionary<ResourceKind, int> counts = new Dictionary<ResourceKind, int>();

        public event Action<ResourceKind, int, int> Changed; // kind, delta, new total

        public int Get(ResourceKind kind) => counts.TryGetValue(kind, out int n) ? n : 0;

        public void Add(ResourceKind kind, int amount)
        {
            if (kind == ResourceKind.None || amount <= 0)
            {
                return;
            }

            counts[kind] = Get(kind) + amount;
            Changed?.Invoke(kind, amount, counts[kind]);
        }

        public bool TryTake(ResourceKind kind, int amount)
        {
            if (amount <= 0 || Get(kind) < amount)
            {
                return false;
            }

            counts[kind] -= amount;
            Changed?.Invoke(kind, -amount, counts[kind]);
            return true;
        }

        public bool Has(ResourceKind kind, int amount) => Get(kind) >= amount;
    }
}
