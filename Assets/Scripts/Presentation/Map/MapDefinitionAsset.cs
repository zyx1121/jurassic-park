using System;
using System.Collections.Generic;
using JurassicPark.Simulation;
using UnityEngine;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// Authoring shell for one fixed map. The editor owns the asset, the simulation owns the rules: this holds only
    /// serialized fields and converts them once into an immutable <see cref="MapDefinition"/>, so no gameplay number
    /// lives in code and nothing in the simulation assembly needs Unity to load a map. Conversion does not validate;
    /// the loader calls <see cref="MapDefinition.Validate"/> and reports what is wrong instead of hiding it.
    /// </summary>
    [CreateAssetMenu(fileName = "MapDefinitionAsset", menuName = "Jurassic Park/Map Definition")]
    public sealed class MapDefinitionAsset : ScriptableObject
    {
        [Header("Grid")]
        [SerializeField] private int width = 1;
        [SerializeField] private int height = 1;

        [Tooltip("Edge length of one cell in metres.")]
        [SerializeField] private float cellSize = 1f;

        [Tooltip("Static terrain flags, row major: index is y * width + x.")]
        [SerializeField] private CellFlags[] cells = Array.Empty<CellFlags>();

        [Header("Layout")]
        [SerializeField] private CampEntry[] camps = Array.Empty<CampEntry>();
        [SerializeField] private RegionEntry[] regions = Array.Empty<RegionEntry>();

        public int Width => width;
        public int Height => height;
        public float CellSize => cellSize;

        /// <summary>Builds the immutable definition the simulation uses. Call Validate on the result before loading a world from it.</summary>
        public MapDefinition ToDefinition()
        {
            var campList = new List<CampDefinition>(camps.Length);
            for (int i = 0; i < camps.Length; i++) campList.Add(camps[i].ToDefinition());

            var regionList = new List<RegionDefinition>(regions.Length);
            for (int i = 0; i < regions.Length; i++) regionList.Add(regions[i].ToDefinition());

            return new MapDefinition(width, height, cellSize, cells, campList, regionList);
        }

        /// <summary>Serialized form of one camp. Unity cannot serialize the simulation types, so the asset carries plain fields and rebuilds them.</summary>
        [Serializable]
        public sealed class CampEntry
        {
            [SerializeField] private string id = string.Empty;
            [SerializeField] private string displayName = string.Empty;
            [SerializeField] private int minX;
            [SerializeField] private int minY;
            [SerializeField] private int maxX;
            [SerializeField] private int maxY;

            [Tooltip("Walkable gaps that lead into the camp, as x,y pairs in order.")]
            [SerializeField] private int[] entranceCoordinates = Array.Empty<int>();

            public CampDefinition ToDefinition()
            {
                var entrances = new List<Cell>(entranceCoordinates.Length / 2);
                for (int i = 0; i + 1 < entranceCoordinates.Length; i += 2)
                {
                    entrances.Add(new Cell(entranceCoordinates[i], entranceCoordinates[i + 1]));
                }

                return new CampDefinition(id, displayName, new CellBounds(minX, minY, maxX, maxY), entrances);
            }
        }

        /// <summary>Serialized form of one event candidate region.</summary>
        [Serializable]
        public sealed class RegionEntry
        {
            [SerializeField] private string id = string.Empty;
            [SerializeField] private RegionKind kind = RegionKind.DinosaurSpawn;
            [SerializeField] private int minX;
            [SerializeField] private int minY;
            [SerializeField] private int maxX;
            [SerializeField] private int maxY;

            public RegionDefinition ToDefinition() => new RegionDefinition(id, kind, new CellBounds(minX, minY, maxX, maxY));
        }
    }
}
