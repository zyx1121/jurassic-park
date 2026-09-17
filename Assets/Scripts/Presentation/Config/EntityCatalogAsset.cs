using System;
using JurassicPark.Simulation;
using UnityEngine;

namespace JurassicPark.Presentation
{
    public enum PlaceholderShape { Capsule, Box, Cylinder, Sphere }

    /// <summary>Content data for every kind of entity: the simulation numbers and how to draw a stand-in for it until real art exists.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Entity Catalog")]
    public sealed class EntityCatalogAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string id = string.Empty;
            public EntityKind kind = EntityKind.Unit;

            [Header("Simulation")]
            [Min(0f)] public float moveSpeed;
            [Min(0)] public int storageCapacity;
            public bool isDepot;
            [Min(0f)] public float gatherSecondsPerUnit;
            public string nodeResource = string.Empty;
            [Min(0)] public int nodeAmount;
            [Tooltip("Blocks the cells it stands on. Size in cells, anchored at the placement cell.")]
            public bool blocks;
            [Min(1)] public int footprintWidth = 1;
            [Min(1)] public int footprintHeight = 1;
            public bool destructible = true;

            [Header("Stand-in view")]
            public PlaceholderShape shape = PlaceholderShape.Capsule;
            public Color color = Color.white;
            public Vector3 size = Vector3.one;

            public EntityDefinition ToDefinition() => new EntityDefinition(id, moveSpeed, storageCapacity, isDepot, gatherSecondsPerUnit,
                string.IsNullOrEmpty(nodeResource) ? null : nodeResource, nodeAmount);
        }

        public Entry[] entries = Array.Empty<Entry>();

        public bool TryGet(string id, out Entry entry)
        {
            for (int i = 0; i < entries.Length; i++)
            {
                if (entries[i].id != id) continue;
                entry = entries[i];
                return true;
            }
            entry = null;
            return false;
        }

        public DefinitionCatalog ToCatalog()
        {
            var catalog = new DefinitionCatalog();
            for (int i = 0; i < entries.Length; i++) catalog.Add(entries[i].ToDefinition());
            return catalog;
        }
    }
}
