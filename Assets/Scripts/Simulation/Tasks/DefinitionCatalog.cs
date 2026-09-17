using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>Lookup from definition id to content data. Built once at load and read-only during a match.</summary>
    public sealed class DefinitionCatalog
    {
        private readonly Dictionary<string, EntityDefinition> byId = new Dictionary<string, EntityDefinition>(StringComparer.Ordinal);

        public void Add(EntityDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (byId.ContainsKey(definition.Id)) throw new ArgumentException($"Definition '{definition.Id}' is already in the catalog.", nameof(definition));
            byId.Add(definition.Id, definition);
        }

        public bool TryGet(string id, out EntityDefinition definition) => byId.TryGetValue(id ?? string.Empty, out definition);
    }
}
