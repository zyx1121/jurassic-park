using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// A place goods physically are: a unit's pack, a depot, a pile on the ground. A unit of goods is in exactly one container
    /// at any moment. Only <see cref="Logistics"/> changes the contents, so every change is a checked transfer.
    /// </summary>
    public sealed class Container
    {
        private readonly SortedDictionary<string, int> amounts = new SortedDictionary<string, int>(StringComparer.Ordinal);

        public EntityId Holder { get; }

        /// <summary>Total units of all resources it can hold. int.MaxValue for a ground pile.</summary>
        public int Capacity { get; }

        public int Total { get; private set; }

        internal int ReservedForDeposit { get; set; }

        internal Container(EntityId holder, int capacity)
        {
            Holder = holder;
            Capacity = capacity;
        }

        public int AmountOf(string resource) => amounts.TryGetValue(resource, out int amount) ? amount : 0;

        /// <summary>Resources present, in ordinal name order so iteration is the same on every machine.</summary>
        public IEnumerable<KeyValuePair<string, int>> Contents => amounts;

        /// <summary>Room not yet taken by goods or promised to a deposit reservation.</summary>
        public int FreeCapacity => Capacity == int.MaxValue ? int.MaxValue : Capacity - Total - ReservedForDeposit;

        internal void Add(string resource, int amount)
        {
            amounts[resource] = AmountOf(resource) + amount;
            Total += amount;
        }

        internal void Remove(string resource, int amount)
        {
            int left = AmountOf(resource) - amount;
            if (left == 0) amounts.Remove(resource);
            else amounts[resource] = left;
            Total -= amount;
        }
    }
}
