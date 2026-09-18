using System;
using System.Collections.Generic;
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
            [Min(0)] public int maxHealth;

            [Header("Construction (empty cost = not buildable)")]
            public CostEntry[] buildCost = Array.Empty<CostEntry>();
            [Min(0f)] public float buildWorkSeconds;
            public bool isGate;

            [Header("Stand-in view")]
            public PlaceholderShape shape = PlaceholderShape.Capsule;
            public Color color = Color.white;
            public Vector3 size = Vector3.one;

            /// <summary>Height of the stand-in above the ground in metres. Unity's capsule and cylinder are two units tall, its box and sphere one.</summary>
            public float DrawnHeight => shape == PlaceholderShape.Capsule || shape == PlaceholderShape.Cylinder ? size.y * 2f : size.y;

            /// <summary>Half the stand-in's width on the ground in metres.</summary>
            public float DrawnHalfWidth => Mathf.Max(size.x, size.z) * 0.5f;

            public EntityDefinition ToDefinition()
            {
                Dictionary<string, int> cost = null;
                if (buildCost.Length > 0)
                {
                    cost = new Dictionary<string, int>();
                    for (int i = 0; i < buildCost.Length; i++) cost[buildCost[i].resource] = buildCost[i].amount;
                }
                return new EntityDefinition(id, moveSpeed, storageCapacity, isDepot, gatherSecondsPerUnit,
                    string.IsNullOrEmpty(nodeResource) ? null : nodeResource, nodeAmount,
                    footprintWidth, footprintHeight, blocks, destructible, maxHealth, cost, buildWorkSeconds, isGate);
            }
        }

        [Serializable]
        public sealed class CostEntry
        {
            public string resource = string.Empty;
            [Min(1)] public int amount = 1;
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
