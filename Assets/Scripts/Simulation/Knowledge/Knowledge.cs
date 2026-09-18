using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>Per-cell state of the map for one team: never seen, seen before, or in sight right now.</summary>
    public enum Visibility : byte { Unexplored = 0, Explored = 1, Visible = 2 }

    /// <summary>
    /// What each team knows of the map: the explored mask that only grows, and the cells in sight right now from its units
    /// and buildings, with the day or night radius the clock says. Teams share sight, so allies see for each other.
    /// Everything a seat is shown, on the minimap and in the snapshot, is filtered through this.
    /// </summary>
    public sealed class Knowledge : ISimSystem
    {
        private sealed class TeamView
        {
            public byte[] Cells;
            public byte[] Scratch;
            public int VisibleCount;
            public long Revision;
        }

        private readonly World world;
        private readonly GridMap map;
        private readonly DefinitionCatalog catalog;
        private readonly SeatRegistry seats;
        private readonly Func<bool> isNight;
        private readonly int updateIntervalTicks;
        private readonly Dictionary<int, TeamView> teams = new Dictionary<int, TeamView>();
        private readonly List<int> teamOrder = new List<int>();

        /// <param name="isNight">Asks the clock. Null means it is always day.</param>
        /// <param name="updateIntervalTicks">Sight is recomputed this often; a unit's move within a cell does not change what it sees.</param>
        public Knowledge(World world, GridMap map, DefinitionCatalog catalog, SeatRegistry seats, Func<bool> isNight, int updateIntervalTicks)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.seats = seats ?? throw new ArgumentNullException(nameof(seats));
            this.isNight = isNight ?? (() => false);
            if (updateIntervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(updateIntervalTicks));
            this.updateIntervalTicks = updateIntervalTicks;
        }

        public int TeamOf(SeatId seat) => seats.TryGet(seat, out Seat s) ? s.Team : 0;

        public Visibility At(int team, Cell cell)
        {
            if (!map.InBounds(cell) || !teams.TryGetValue(team, out TeamView view)) return Visibility.Unexplored;
            return (Visibility)view.Cells[map.IndexOf(cell)];
        }

        public Visibility At(SeatId seat, Cell cell) => At(TeamOf(seat), cell);

        /// <summary>Whether a seat can see the entity right now: its own team's things always, anything else only in a visible cell.</summary>
        public bool CanSee(SeatId seat, Entity entity)
        {
            if (entity.Owner == seat || (!entity.Owner.IsNone && TeamOf(entity.Owner) == TeamOf(seat))) return true;
            return At(seat, map.CellAt(entity.Position)) == Visibility.Visible;
        }

        /// <summary>The team's per-cell visibility, one byte per cell in map index order. Read-only view for snapshots and the fog mesh.</summary>
        public IReadOnlyList<byte> CellsOf(int team) => teams.TryGetValue(team, out TeamView view) ? view.Cells : Array.Empty<byte>();

        /// <summary>Changes when the team's mask changes, so a consumer can skip unchanged ticks.</summary>
        public long RevisionOf(int team) => teams.TryGetValue(team, out TeamView view) ? view.Revision : 0;

        public int VisibleCountOf(int team) => teams.TryGetValue(team, out TeamView view) ? view.VisibleCount : 0;

        /// <summary>Recomputes sight now, regardless of the interval. The setup uses it so the first snapshot is not blind.</summary>
        public void Refresh() => Recompute();

        public void Tick(World tickedWorld)
        {
            if (tickedWorld.Tick % updateIntervalTicks != 0) return;
            Recompute();
        }

        private void Recompute()
        {
            for (int i = 0; i < seats.Seats.Count; i++)
            {
                int team = seats.Seats[i].Team;
                if (teams.ContainsKey(team)) continue;
                teams.Add(team, new TeamView { Cells = new byte[map.Width * map.Height], Scratch = new byte[map.Width * map.Height] });
                teamOrder.Add(team);
            }
            // The next mask is built beside the current one: what was visible becomes explored, sight is added, and only a
            // mask that actually differs is adopted, so the revision moves exactly when the screen must redraw.
            for (int t = 0; t < teamOrder.Count; t++)
            {
                TeamView view = teams[teamOrder[t]];
                for (int i = 0; i < view.Cells.Length; i++)
                    view.Scratch[i] = view.Cells[i] == (byte)Visibility.Visible ? (byte)Visibility.Explored : view.Cells[i];
                view.VisibleCount = 0;
            }
            bool night = isNight();
            IReadOnlyList<Entity> entities = world.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity entity = entities[i];
                if (!entity.IsAlive || entity.Owner.IsNone || !catalog.TryGet(entity.DefinitionId, out EntityDefinition definition)) continue;
                float radius = night ? definition.SightNight : definition.SightDay;
                if (radius <= 0f || !teams.TryGetValue(TeamOf(entity.Owner), out TeamView view)) continue;
                Reveal(view, entity.Position, radius);
            }
            for (int t = 0; t < teamOrder.Count; t++)
            {
                TeamView view = teams[teamOrder[t]];
                bool changed = false;
                for (int i = 0; i < view.Cells.Length; i++)
                    if (view.Cells[i] != view.Scratch[i])
                    {
                        changed = true;
                        break;
                    }
                if (!changed) continue;
                Array.Copy(view.Scratch, view.Cells, view.Cells.Length);
                view.Revision++;
            }
        }

        private void Reveal(TeamView view, SimVector2 centre, float radius)
        {
            float size = map.CellSize;
            int cells = (int)Math.Ceiling(radius / size);
            Cell at = map.CellAt(centre);
            float radiusSquared = radius * radius;
            for (int dy = -cells; dy <= cells; dy++)
                for (int dx = -cells; dx <= cells; dx++)
                {
                    var cell = new Cell(at.X + dx, at.Y + dy);
                    if (!map.InBounds(cell)) continue;
                    SimVector2 c = map.CenterOf(cell);
                    float ex = c.X - centre.X, ey = c.Y - centre.Y;
                    if (ex * ex + ey * ey > radiusSquared) continue;
                    int index = map.IndexOf(cell);
                    if (view.Scratch[index] == (byte)Visibility.Visible) continue;
                    view.Scratch[index] = (byte)Visibility.Visible;
                    view.VisibleCount++;
                }
        }
    }
}
