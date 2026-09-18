using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>Lookup from definition id to content data. Built once at load and read-only during a match.</summary>
    public sealed class DefinitionCatalog
    {
        private readonly Dictionary<string, EntityDefinition> byId = new Dictionary<string, EntityDefinition>(StringComparer.Ordinal);
        private readonly List<EntityDefinition> ordered = new List<EntityDefinition>();

        public void Add(EntityDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (byId.ContainsKey(definition.Id)) throw new ArgumentException($"Definition '{definition.Id}' is already in the catalog.", nameof(definition));
            byId.Add(definition.Id, definition);
            ordered.Add(definition);
        }

        /// <summary>Definitions in the order they were added. The index is how a command names a definition, since a command carries no strings.</summary>
        public IReadOnlyList<EntityDefinition> All => ordered;

        public bool TryGetByIndex(int index, out EntityDefinition definition)
        {
            definition = index >= 0 && index < ordered.Count ? ordered[index] : null;
            return definition != null;
        }

        public int IndexOf(string id)
        {
            for (int i = 0; i < ordered.Count; i++)
                if (ordered[i].Id == id) return i;
            return -1;
        }

        public bool TryGet(string id, out EntityDefinition definition) => byId.TryGetValue(id ?? string.Empty, out definition);
    }
}
