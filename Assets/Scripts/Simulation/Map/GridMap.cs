using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// The authoritative space query surface: static terrain from a <see cref="MapDefinition"/> plus the occupancy that
    /// buildings, sites, doors and wrecks change during a match. It is the single truth the derived data must follow,
    /// so a wall that closes a road closes it for pathing in the same commit, and a path computed before that change
    /// can be recognised as stale through <see cref="Version"/> instead of quietly walking through the new wall.
    /// </summary>
    public sealed class GridMap
    {
        private readonly EntityId[] blockers;
        private readonly bool[] destructibleCells;
        private readonly Dictionary<EntityId, List<Cell>> footprints = new Dictionary<EntityId, List<Cell>>();
        private readonly PathScratch scratch = new PathScratch();

        public MapDefinition Definition { get; }
        public int Width { get; }
        public int Height { get; }

        /// <summary>Edge length of one cell in metres, taken from the definition.</summary>
        public float CellSize { get; }

        /// <summary>
        /// Rises by one on every accepted occupancy change. A path result carries the version it was computed on, so a
        /// consumer can tell "still valid" from "recheck" without diffing the map. Rejected changes leave it alone.
        /// </summary>
        public long Version { get; private set; } = 1;

        public GridMap(MapDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Width = definition.Width;
            Height = definition.Height;
            CellSize = definition.CellSize;
            int count = Width * Height;
            if (definition.Flags.Count != count)
            {
                throw new ArgumentException($"The definition holds {definition.Flags.Count} terrain flags for a {Width}x{Height} map; call Validate before loading.", nameof(definition));
            }

            blockers = new EntityId[count];
            destructibleCells = new bool[count];
        }

        /// <summary>
        /// The working memory path queries borrow, so a search costs no map sized allocation. One search runs at a
        /// time: the simulation is single threaded on the host, and a second caller would overwrite the first's state.
        /// </summary>
        internal PathScratch Scratch => scratch;

        public bool InBounds(Cell cell) => cell.X >= 0 && cell.X < Width && cell.Y >= 0 && cell.Y < Height;

        /// <summary>Row major index of a cell inside the map.</summary>
        public int IndexOf(Cell cell) => cell.Y * Width + cell.X;

        public CellFlags FlagsAt(Cell cell) => Definition.FlagsAt(cell);

        /// <summary>Terrain permits standing here. Cliffs and water are never walkable and can never be opened up by destroying something.</summary>
        public bool IsStaticWalkable(Cell cell) => Definition.IsStaticWalkable(cell);

        /// <summary>A unit can stand here right now: walkable terrain with nothing occupying it.</summary>
        public bool IsWalkable(Cell cell) => IsStaticWalkable(cell) && BlockerAt(cell).IsNone;

        /// <summary>A blueprint may claim this cell: buildable terrain with nothing occupying it yet.</summary>
        public bool IsBuildable(Cell cell) => Definition.IsStaticBuildable(cell) && BlockerAt(cell).IsNone;

        /// <summary>The entity occupying this cell, or <see cref="EntityId.None"/> when the cell is free or outside the map.</summary>
        public EntityId BlockerAt(Cell cell) => InBounds(cell) ? blockers[IndexOf(cell)] : EntityId.None;

        /// <summary>True when this cell is blocked by something that can be destroyed, which is the only kind of blocker a breach can remove.</summary>
        public bool IsDestructibleBlocker(Cell cell) => !BlockerAt(cell).IsNone && destructibleCells[IndexOf(cell)];

        /// <summary>The cells this blocker holds, in the order they were claimed, or an empty list when it holds none.</summary>
        public IReadOnlyList<Cell> FootprintOf(EntityId blocker) =>
            footprints.TryGetValue(blocker, out List<Cell> cells) ? cells.AsReadOnly() : (IReadOnlyList<Cell>)Array.Empty<Cell>();

        /// <summary>
        /// Claims every cell of a footprint for one entity, or claims none. Half placed buildings would leave the map
        /// describing a shape the world never agreed to, so any cell outside the map, on unwalkable terrain, or already
        /// taken rejects the whole placement and leaves <see cref="Version"/> untouched.
        /// </summary>
        public bool TryOccupy(IReadOnlyList<Cell> footprint, EntityId blocker, bool destructible)
        {
            if (footprint == null) throw new ArgumentNullException(nameof(footprint));
            if (blocker.IsNone) throw new ArgumentException("A blocker needs an entity id.", nameof(blocker));
            if (footprint.Count == 0) return false;
            if (footprints.ContainsKey(blocker)) return false;

            for (int i = 0; i < footprint.Count; i++)
            {
                Cell cell = footprint[i];
                if (!InBounds(cell) || !IsStaticWalkable(cell)) return false;
                if (!blockers[IndexOf(cell)].IsNone) return false;
            }

            var claimed = new List<Cell>(footprint.Count);
            for (int i = 0; i < footprint.Count; i++)
            {
                Cell cell = footprint[i];
                int index = IndexOf(cell);
                if (!blockers[index].IsNone) continue; // the same cell listed twice is claimed once
                blockers[index] = blocker;
                destructibleCells[index] = destructible;
                claimed.Add(cell);
            }

            footprints.Add(blocker, claimed);
            Version++;
            return true;
        }

        /// <summary>Frees everything this blocker held, which is how a collapsed building reopens a road. False when it held nothing, and then the version does not move.</summary>
        public bool Release(EntityId blocker)
        {
            if (!footprints.TryGetValue(blocker, out List<Cell> cells)) return false;
            for (int i = 0; i < cells.Count; i++)
            {
                int index = IndexOf(cells[i]);
                blockers[index] = EntityId.None;
                destructibleCells[index] = false;
            }

            footprints.Remove(blocker);
            Version++;
            return true;
        }

        /// <summary>The cell a world position falls in. Cell (0,0) covers [0, CellSize) on both axes, so positions left of or below the origin land on negative cells rather than clamping onto the map.</summary>
        public Cell CellAt(SimVector2 position) =>
            new Cell((int)Math.Floor(position.X / CellSize), (int)Math.Floor(position.Y / CellSize));

        /// <summary>The world position at the centre of a cell. Movement targets a centre, never a corner, so a unit cannot stand on the seam between two cells.</summary>
        public SimVector2 CenterOf(Cell cell) =>
            new SimVector2((cell.X + 0.5f) * CellSize, (cell.Y + 0.5f) * CellSize);
    }
}
