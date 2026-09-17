using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// The fixed map: size, cell scale, static terrain flags, recommended camps and event candidate regions. It is
    /// immutable once built, because everything that changes during a match (buildings, doors, wrecks) belongs to
    /// <see cref="GridMap"/> occupancy instead. Loading never repairs a bad map: <see cref="Validate"/> names the
    /// offending entity and condition so an illegal layout fails loudly rather than pretending the world is ready.
    /// </summary>
    public sealed class MapDefinition
    {
        private readonly CellFlags[] flags;
        private readonly List<CampDefinition> camps;
        private readonly List<RegionDefinition> regions;

        public int Width { get; }
        public int Height { get; }

        /// <summary>Edge length of one cell in metres. Cell coordinates are indices; only this turns them into world space.</summary>
        public float CellSize { get; }

        /// <summary>Static terrain flags, row major: index is y * Width + x. Its length is only checked by <see cref="Validate"/>, so a broken authoring pass still reports instead of throwing.</summary>
        public IReadOnlyList<CellFlags> Flags { get; }

        public IReadOnlyList<CampDefinition> Camps { get; }
        public IReadOnlyList<RegionDefinition> Regions { get; }

        public MapDefinition(
            int width,
            int height,
            float cellSize,
            IReadOnlyList<CellFlags> flags,
            IReadOnlyList<CampDefinition> camps,
            IReadOnlyList<RegionDefinition> regions)
        {
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width), "A map needs at least one cell across.");
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height), "A map needs at least one cell up.");
            if (!(cellSize > 0f) || float.IsInfinity(cellSize)) throw new ArgumentOutOfRangeException(nameof(cellSize), "Cell size is a positive length in metres.");
            if (flags == null) throw new ArgumentNullException(nameof(flags));
            if (camps == null) throw new ArgumentNullException(nameof(camps));
            if (regions == null) throw new ArgumentNullException(nameof(regions));

            Width = width;
            Height = height;
            CellSize = cellSize;
            this.flags = new CellFlags[flags.Count];
            for (int i = 0; i < flags.Count; i++) this.flags[i] = flags[i];
            this.camps = new List<CampDefinition>(camps);
            this.regions = new List<RegionDefinition>(regions);
            Flags = Array.AsReadOnly(this.flags);
            Camps = this.camps.AsReadOnly();
            Regions = this.regions.AsReadOnly();
        }

        public bool InBounds(Cell cell) => cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;

        /// <summary>Row major index of a cell. Only meaningful for cells inside the map.</summary>
        public int IndexOf(Cell cell) => cell.Y * Width + cell.X;

        /// <summary>Terrain flags of a cell, or None outside the map or past the end of a short flags array.</summary>
        public CellFlags FlagsAt(Cell cell)
        {
            if (!InBounds(cell)) return CellFlags.None;
            int index = IndexOf(cell);
            return index < flags.Length ? flags[index] : CellFlags.None;
        }

        /// <summary>Terrain alone says a unit may stand here. It says nothing about what is built on it; ask <see cref="GridMap"/> for that.</summary>
        public bool IsStaticWalkable(Cell cell) => (FlagsAt(cell) & CellFlags.Walkable) != 0;

        public bool IsStaticBuildable(Cell cell) => (FlagsAt(cell) & CellFlags.Buildable) != 0;

        /// <summary>
        /// Every reason this map cannot be loaded, in the order the offending entities were authored, as text a human
        /// can act on. An empty list means the layout is consistent; it does not promise the match is balanced.
        /// </summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            int expected = Width * Height;
            if (flags.Length != expected)
            {
                errors.Add($"Terrain flags hold {flags.Length} cells but the map is {Width}x{Height} = {expected} cells.");
            }

            AddBuildableTerrainErrors(errors);

            int[] components = BuildConnectivity(out int openGround);
            var campIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < camps.Count; i++)
            {
                CampDefinition camp = camps[i];
                string id = string.IsNullOrEmpty(camp.Id) ? $"#{i}" : camp.Id;
                if (string.IsNullOrEmpty(camp.Id)) errors.Add($"Camp {i} has an empty id.");
                else if (!campIds.Add(camp.Id)) errors.Add($"Camp id '{camp.Id}' is used more than once.");

                if (!InBounds(camp.Bounds.Min) || !InBounds(camp.Bounds.Max))
                {
                    errors.Add($"Camp '{id}' bounds {camp.Bounds} lie outside the {Width}x{Height} map.");
                }

                if (!HasWalkableCell(camp.Bounds))
                {
                    errors.Add($"Camp '{id}' bounds {camp.Bounds} hold no walkable cell, so nothing could ever camp there.");
                }

                if (camp.Entrances.Count == 0) errors.Add($"Camp '{id}' has no entrance cell, so nothing can enter it.");
                for (int e = 0; e < camp.Entrances.Count; e++)
                {
                    Cell entrance = camp.Entrances[e];
                    if (!InBounds(entrance))
                    {
                        errors.Add($"Camp '{id}' entrance {entrance} lies outside the {Width}x{Height} map.");
                        continue;
                    }

                    if (!IsStaticWalkable(entrance))
                    {
                        errors.Add($"Camp '{id}' entrance {entrance} is not walkable terrain.");
                        continue;
                    }

                    // An entrance somewhere else on the map is not this camp's entrance, however open it is.
                    if (!IsOnOrNextTo(camp.Bounds, entrance))
                    {
                        errors.Add($"Camp '{id}' entrance {entrance} is neither inside camp bounds {camp.Bounds} nor beside them.");
                        continue;
                    }

                    if (openGround >= 0 && components[IndexOf(entrance)] != openGround)
                    {
                        errors.Add($"Camp '{id}' entrance {entrance} is cut off from the map's open ground.");
                        continue;
                    }

                    // Standing in the gap is not entering: the walk from the gap into the camp has to exist.
                    if (openGround >= 0 && !ReachesInside(camp.Bounds, components, components[IndexOf(entrance)]))
                    {
                        errors.Add($"Camp '{id}' entrance {entrance} cannot reach any walkable cell inside camp bounds {camp.Bounds}.");
                    }
                }
            }

            var regionIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < regions.Count; i++)
            {
                RegionDefinition region = regions[i];
                string id = string.IsNullOrEmpty(region.Id) ? $"#{i}" : region.Id;
                if (string.IsNullOrEmpty(region.Id)) errors.Add($"Region {i} has an empty id.");
                else if (!regionIds.Add(region.Id)) errors.Add($"Region id '{region.Id}' is used more than once.");

                if (!InBounds(region.Bounds.Min) || !InBounds(region.Bounds.Max))
                {
                    errors.Add($"Region '{id}' ({region.Kind}) bounds {region.Bounds} lie outside the {Width}x{Height} map.");
                    continue;
                }

                if (!HasWalkableCell(region.Bounds))
                {
                    errors.Add($"Region '{id}' ({region.Kind}) has no walkable cell, so nothing can be placed in it.");
                }
            }

            return errors.AsReadOnly();
        }

        private bool HasWalkableCell(CellBounds bounds)
        {
            int minX = Math.Max(bounds.MinX, 0);
            int minY = Math.Max(bounds.MinY, 0);
            int maxX = Math.Min(bounds.MaxX, Width - 1);
            int maxY = Math.Min(bounds.MaxY, Height - 1);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (IsStaticWalkable(new Cell(x, y))) return true;
                }
            }

            return false;
        }

        /// <summary>A cell inside the rectangle or touching it, diagonals included: where a gap in the cliff line around a camp sits.</summary>
        private static bool IsOnOrNextTo(CellBounds bounds, Cell cell) =>
            cell.X >= bounds.MinX - 1 && cell.X <= bounds.MaxX + 1 && cell.Y >= bounds.MinY - 1 && cell.Y <= bounds.MaxY + 1;

        /// <summary>True when some walkable cell inside the rectangle belongs to the same connected area as the entrance.</summary>
        private bool ReachesInside(CellBounds bounds, int[] components, int fromComponent)
        {
            int minX = Math.Max(bounds.MinX, 0);
            int minY = Math.Max(bounds.MinY, 0);
            int maxX = Math.Min(bounds.MaxX, Width - 1);
            int maxY = Math.Min(bounds.MaxY, Height - 1);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    var cell = new Cell(x, y);
                    if (IsStaticWalkable(cell) && components[IndexOf(cell)] == fromComponent) return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Buildable ground that is not walkable would pass a blueprint's terrain check and then fail the occupancy
        /// claim, which asks for walkable cells, so the map would promise a site nothing can ever be built on.
        /// </summary>
        private void AddBuildableTerrainErrors(List<string> errors)
        {
            int offenders = 0;
            var first = Cell.Zero;
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var cell = new Cell(x, y);
                    if (!IsStaticBuildable(cell) || IsStaticWalkable(cell)) continue;
                    if (offenders == 0) first = cell;
                    offenders++;
                }
            }

            if (offenders > 0)
            {
                errors.Add($"{first} is buildable but not walkable ({offenders} cells like it); nothing could stand where a building was placed.");
            }
        }

        /// <summary>
        /// Labels every walkable cell with its connected component and returns the label of the open ground, which is
        /// the largest component, or -1 when nothing is walkable. Connectivity uses the same movement rule as
        /// <see cref="GridPathfinder"/>, diagonals included but no cutting past a corner, so "connected" here means
        /// "a unit can actually walk there" and not merely "the cells touch".
        /// </summary>
        private int[] BuildConnectivity(out int openGround)
        {
            var labels = new int[Width * Height];
            for (int i = 0; i < labels.Length; i++) labels[i] = -1;

            openGround = -1;
            int bestSize = 0;
            int next = 0;
            var stack = new Stack<Cell>();
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    var origin = new Cell(x, y);
                    int originIndex = IndexOf(origin);
                    if (labels[originIndex] >= 0 || !IsStaticWalkable(origin)) continue;

                    int label = next++;
                    int size = 0;
                    labels[originIndex] = label;
                    stack.Push(origin);
                    while (stack.Count > 0)
                    {
                        Cell current = stack.Pop();
                        size++;
                        for (int d = 0; d < GridDirections.Count; d++)
                        {
                            int dx = GridDirections.X[d];
                            int dy = GridDirections.Y[d];
                            var neighbour = new Cell(current.X + dx, current.Y + dy);
                            if (!IsStaticWalkable(neighbour)) continue;
                            if (dx != 0 && dy != 0 &&
                                (!IsStaticWalkable(new Cell(current.X + dx, current.Y)) ||
                                 !IsStaticWalkable(new Cell(current.X, current.Y + dy)))) continue;
                            int neighbourIndex = IndexOf(neighbour);
                            if (labels[neighbourIndex] >= 0) continue;
                            labels[neighbourIndex] = label;
                            stack.Push(neighbour);
                        }
                    }

                    if (size > bestSize)
                    {
                        bestSize = size;
                        openGround = label;
                    }
                }
            }

            return labels;
        }
    }
}
