using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Buildings and the cells they take: placing a site, bringing it to completion, opening and closing gates, demolition, and
    /// giving cells back when a blocker or a felled tree leaves the world. Register it after Logistics: it reacts to removals
    /// Logistics already accounted for.
    /// </summary>
    public sealed class Structures : ISimSystem
    {
        private readonly World world;
        private readonly GridMap map;
        private readonly Logistics goods;
        private readonly Vitals vitals;
        private readonly SeatRegistry seats;
        private readonly Dictionary<EntityId, BuildSite> sites = new Dictionary<EntityId, BuildSite>();
        private readonly HashSet<EntityId> openGates = new HashSet<EntityId>();
        private readonly Dictionary<EntityId, IReadOnlyList<Cell>> gateFootprints = new Dictionary<EntityId, IReadOnlyList<Cell>>();
        private readonly List<EntityId> blockers = new List<EntityId>();

        public Structures(World world, GridMap map, Logistics goods, Vitals vitals, SeatRegistry seats)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.goods = goods ?? throw new ArgumentNullException(nameof(goods));
            this.vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
            this.seats = seats ?? throw new ArgumentNullException(nameof(seats));
        }

        public bool TryGetSite(EntityId id, out BuildSite site) => sites.TryGetValue(id, out site);
        public bool IsGateOpen(EntityId id) => openGates.Contains(id);
        public int SiteCount => sites.Count;

        /// <summary>The cells a definition would take when anchored at a cell.</summary>
        public static List<Cell> FootprintAt(EntityDefinition definition, Cell anchor)
        {
            var cells = new List<Cell>(definition.FootprintWidth * definition.FootprintHeight);
            for (int y = 0; y < definition.FootprintHeight; y++)
                for (int x = 0; x < definition.FootprintWidth; x++)
                    cells.Add(new Cell(anchor.X + x, anchor.Y + y));
            return cells;
        }

        /// <summary>Can a site of this definition be placed here right now? The reason is specific because the GUI shows it while the player aims.</summary>
        public CommandRejection CheckPlacement(EntityDefinition definition, Cell anchor, out List<Cell> footprint)
        {
            footprint = FootprintAt(definition, anchor);
            for (int i = 0; i < footprint.Count; i++)
                if (!map.InBounds(footprint[i]) || !map.IsBuildable(footprint[i])) return CommandRejection.SiteBlocked;
            // Nothing may be built over a unit: a unit inside a wall is a unit that can never leave.
            IReadOnlyList<Entity> entities = world.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                if (!entities[i].IsAlive || entities[i].Kind != EntityKind.Unit) continue;
                Cell at = map.CellAt(entities[i].Position);
                for (int c = 0; c < footprint.Count; c++)
                    if (footprint[c] == at) return CommandRejection.SiteBlocked;
            }
            // A site nobody can stand beside is a site nobody can ever build.
            bool anyApproach = false;
            for (int c = 0; c < footprint.Count && !anyApproach; c++)
                for (int dx = -1; dx <= 1 && !anyApproach; dx++)
                    for (int dy = -1; dy <= 1 && !anyApproach; dy++)
                    {
                        var beside = new Cell(footprint[c].X + dx, footprint[c].Y + dy);
                        if (!footprint.Contains(beside) && map.IsWalkable(beside)) anyApproach = true;
                    }
            return anyApproach ? CommandRejection.None : CommandRejection.SiteUnreachable;
        }

        /// <summary>Places a site: the entity exists, blocks its cells at once (destructible, so a dinosaur may still come through it), and waits for materials and work.</summary>
        public Entity PlaceSite(EntityDefinition definition, SeatId owner, Cell anchor)
        {
            if (CheckPlacement(definition, anchor, out List<Cell> footprint) != CommandRejection.None) return null;
            SimVector2 first = map.CenterOf(footprint[0]), last = map.CenterOf(footprint[footprint.Count - 1]);
            Entity site = world.Spawn(EntityKind.Building, definition.Id, owner, (first + last) * 0.5f);
            if (definition.Blocks)
            {
                map.TryOccupy(footprint, site.Id, destructible: true);
                blockers.Add(site.Id);
                world.Raise(new PassabilityChanged(site.Id, true));
            }
            int materials = 0;
            foreach (KeyValuePair<string, int> need in definition.BuildCost) materials += need.Value;
            if (materials > 0) goods.AddContainer(site, materials);
            sites.Add(site.Id, new BuildSite(site.Id, definition, footprint));
            world.Raise(new SitePlaced(site.Id, owner));
            return site;
        }

        /// <summary>A builder worked on the site for some seconds. Only counts while every material is on site: there is no free building.</summary>
        public bool Work(EntityId siteId, float seconds)
        {
            if (!sites.TryGetValue(siteId, out BuildSite site) || !world.IsAlive(siteId) || !site.HasAllMaterials(goods)) return false;
            site.Work += seconds;
            if (site.Progress < 1f) return true;
            Complete(site);
            return true;
        }

        private void Complete(BuildSite site)
        {
            // Materials become the building. Consumed, so the books still balance and nothing can be recovered from a wall.
            foreach (KeyValuePair<string, int> need in site.Definition.BuildCost) goods.Consume(site.Entity, need.Key, need.Value);
            goods.RemoveContainer(site.Entity);
            sites.Remove(site.Entity);
            world.TryGet(site.Entity, out Entity building);
            vitals.Attach(building, site.Definition);
            if (site.Definition.Blocks)
            {
                // From destructible-while-building to whatever the finished thing is.
                map.Release(site.Entity);
                map.TryOccupy(site.Footprint, site.Entity, site.Definition.Destructible);
            }
            if (site.Definition.IsGate) gateFootprints[site.Entity] = site.Footprint;
            world.Raise(new BuildingCompleted(site.Entity));
        }

        /// <summary>Registers a finished building placed by the scenario rather than built in play.</summary>
        public void AttachBuilt(Entity building, EntityDefinition definition, IReadOnlyList<Cell> footprint)
        {
            vitals.Attach(building, definition);
            if (definition.Blocks) blockers.Add(building.Id);
            if (definition.IsGate) gateFootprints[building.Id] = footprint;
        }

        /// <summary>Removes a site or building the seat owns. Returns false when there is no such thing.</summary>
        public bool Demolish(EntityId id, SeatId bySeat)
        {
            if (!world.TryGet(id, out Entity building) || !building.IsAlive || building.Kind != EntityKind.Building || building.Owner != bySeat) return false;
            world.Despawn(id, "demolished");
            return true;
        }

        /// <summary>Would ToggleGate succeed right now, and if not, why. Changes nothing.</summary>
        public CommandRejection CheckToggle(EntityId id, SeatId bySeat)
        {
            if (!gateFootprints.TryGetValue(id, out IReadOnlyList<Cell> footprint) || !world.TryGet(id, out Entity gate) || !gate.IsAlive) return CommandRejection.InvalidTarget;
            if (!seats.MayUsePropertyOf(bySeat, gate.Owner)) return CommandRejection.NotAllowedOnTarget;
            if (!openGates.Contains(id)) return CommandRejection.None;
            // Closing a gate on a unit would wall it in.
            IReadOnlyList<Entity> entities = world.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                if (!entities[i].IsAlive || entities[i].Kind != EntityKind.Unit) continue;
                Cell at = map.CellAt(entities[i].Position);
                for (int c = 0; c < footprint.Count; c++)
                    if (footprint[c] == at) return CommandRejection.SiteBlocked;
            }
            return CommandRejection.None;
        }

        /// <summary>Opens a closed gate or closes an open one. Closing refuses while a unit stands in the gateway.</summary>
        public bool ToggleGate(EntityId id, SeatId bySeat, out CommandRejection why)
        {
            why = CheckToggle(id, bySeat);
            if (why != CommandRejection.None) return false;
            IReadOnlyList<Cell> footprint = gateFootprints[id];
            if (openGates.Contains(id))
            {
                openGates.Remove(id);
                map.TryOccupy(footprint, id, destructible: true);
                world.Raise(new PassabilityChanged(id, true));
                world.Raise(new GateToggled(id, false));
                return true;
            }
            openGates.Add(id);
            map.Release(id);
            world.Raise(new PassabilityChanged(id, false));
            world.Raise(new GateToggled(id, true));
            return true;
        }

        public void Tick(World tickedWorld)
        {
            // Whatever stopped existing gives its cells back: a destroyed wall, a demolished site, a felled tree.
            for (int i = blockers.Count - 1; i >= 0; i--)
            {
                EntityId id = blockers[i];
                if (tickedWorld.IsAlive(id)) continue;
                blockers.RemoveAt(i);
                if (map.Release(id)) tickedWorld.Raise(new PassabilityChanged(id, false));
                if (sites.Remove(id, out BuildSite site)) goods.RemoveContainer(id);
                openGates.Remove(id);
                gateFootprints.Remove(id);
                vitals.Forget(id);
            }
            // A site can also vanish without ever blocking (a non-blocking building); still stop tracking it.
            if (sites.Count > 0)
            {
                pendingSiteRemovals.Clear();
                foreach (KeyValuePair<EntityId, BuildSite> pair in sites)
                    if (!tickedWorld.IsAlive(pair.Key)) pendingSiteRemovals.Add(pair.Key);
                for (int i = 0; i < pendingSiteRemovals.Count; i++)
                {
                    sites.Remove(pendingSiteRemovals[i]);
                    goods.RemoveContainer(pendingSiteRemovals[i]);
                }
            }
            // A gathered-out tree falls: its cells open and the way through the grove changes.
            IReadOnlyList<Entity> entities = tickedWorld.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity entity = entities[i];
                if (entity.Kind == EntityKind.ResourceNode && entity.IsAlive && goods.TryGetNode(entity.Id, out ResourceNode node) && node.Remaining == 0 && node.Unreserved == 0)
                    tickedWorld.Despawn(entity.Id, "felled");
            }
        }

        private readonly List<EntityId> pendingSiteRemovals = new List<EntityId>();

        /// <summary>Tracks a scenario-placed blocker so its cells are released when it goes.</summary>
        public void TrackBlocker(EntityId id) => blockers.Add(id);
    }
}
