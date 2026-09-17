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

        /// <summary>
        /// Builds the immutable definition the simulation uses. Authoring slips inside the asset, such as a rectangle
        /// whose corners are the wrong way round, are straightened out rather than thrown, because an exception in the
        /// inspector tells the author nothing. Use <see cref="TryToDefinition"/> to see what was straightened, and call
        /// Validate on the result before loading a world from it.
        /// </summary>
        public MapDefinition ToDefinition()
        {
            if (!TryToDefinition(out MapDefinition definition, out IReadOnlyList<string> errors) && definition == null)
            {
                throw new InvalidOperationException($"'{name}' cannot describe a map at all: {string.Join(" | ", errors)}");
            }

            return definition;
        }

        /// <summary>
        /// Converts the asset and reports every authoring problem found on the way, in the order the entries were
        /// authored. It returns false when something was wrong; the definition is still produced unless the grid
        /// itself is impossible, so the editor can show the map and the list of problems side by side.
        /// </summary>
        public bool TryToDefinition(out MapDefinition definition, out IReadOnlyList<string> errors)
        {
            var problems = new List<string>();
            definition = null;
            errors = problems;

            if (width < 1) problems.Add($"Width {width} is not a map width; it needs at least one cell.");
            if (height < 1) problems.Add($"Height {height} is not a map height; it needs at least one cell.");
            if (!(cellSize > 0f)) problems.Add($"Cell size {cellSize} is not a length in metres.");
            if (problems.Count > 0) return false;

            var campList = new List<CampDefinition>(camps.Length);
            for (int i = 0; i < camps.Length; i++) campList.Add(camps[i].ToDefinition(i, problems));

            var regionList = new List<RegionDefinition>(regions.Length);
            for (int i = 0; i < regions.Length; i++) regionList.Add(regions[i].ToDefinition(i, problems));

            definition = new MapDefinition(width, height, cellSize, cells, campList, regionList);
            return problems.Count == 0;
        }

        /// <summary>Puts a rectangle's corners the right way round and says so, so a swapped pair is a reported mistake instead of a thrown one.</summary>
        private static CellBounds Straighten(int minX, int minY, int maxX, int maxY, string owner, List<string> problems)
        {
            if (maxX >= minX && maxY >= minY) return new CellBounds(minX, minY, maxX, maxY);
            problems.Add($"{owner} has its bounds corners swapped ([{minX},{minY}..{maxX},{maxY}]); they were put the right way round.");
            return new CellBounds(Math.Min(minX, maxX), Math.Min(minY, maxY), Math.Max(minX, maxX), Math.Max(minY, maxY));
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

            public CampDefinition ToDefinition(int index, List<string> problems)
            {
                string owner = $"Camp '{(string.IsNullOrEmpty(id) ? $"#{index}" : id)}'";
                var entrances = new List<Cell>(entranceCoordinates.Length / 2);
                for (int i = 0; i + 1 < entranceCoordinates.Length; i += 2)
                {
                    entrances.Add(new Cell(entranceCoordinates[i], entranceCoordinates[i + 1]));
                }

                if (entranceCoordinates.Length % 2 != 0)
                {
                    problems.Add($"{owner} has an odd number of entrance coordinates; the last one has no y and was dropped.");
                }

                return new CampDefinition(id, displayName, Straighten(minX, minY, maxX, maxY, owner, problems), entrances);
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

            public RegionDefinition ToDefinition(int index, List<string> problems)
            {
                string owner = $"Region '{(string.IsNullOrEmpty(id) ? $"#{index}" : id)}'";
                return new RegionDefinition(id, kind, Straighten(minX, minY, maxX, maxY, owner, problems));
            }
        }
    }
}
