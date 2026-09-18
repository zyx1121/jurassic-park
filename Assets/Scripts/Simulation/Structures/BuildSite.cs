using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// A building that is not built yet. It stands on its cells from the moment it is accepted, holds the materials brought to it in
    /// its own container, and counts work. It is the same entity as the finished building: completion changes state, not identity.
    /// </summary>
    public sealed class BuildSite
    {
        public EntityId Entity { get; }
        public EntityDefinition Definition { get; }
        public IReadOnlyList<Cell> Footprint { get; }

        /// <summary>Seconds of builder work done. Complete at <see cref="EntityDefinition.BuildWorkSeconds"/>.</summary>
        public float Work { get; internal set; }

        internal BuildSite(EntityId entity, EntityDefinition definition, IReadOnlyList<Cell> footprint)
        {
            Entity = entity;
            Definition = definition;
            Footprint = footprint;
        }

        public float Progress => Definition.BuildWorkSeconds > 0f ? System.Math.Min(1f, Work / Definition.BuildWorkSeconds) : 1f;

        /// <summary>Units of goods still to be brought for this resource, counting what is already on site.</summary>
        public int Missing(Logistics goods, string resource)
        {
            int required = Definition.BuildCost.TryGetValue(resource, out int amount) ? amount : 0;
            int delivered = goods.TryGetContainer(Entity, out Container materials) ? materials.AmountOf(resource) : 0;
            return System.Math.Max(0, required - delivered);
        }

        public bool HasAllMaterials(Logistics goods)
        {
            foreach (KeyValuePair<string, int> need in Definition.BuildCost)
                if (Missing(goods, need.Key) > 0) return false;
            return true;
        }
    }
}
