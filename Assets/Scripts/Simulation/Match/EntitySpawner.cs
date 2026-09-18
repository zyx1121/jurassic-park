using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// The one way a definition becomes an entity with everything attached: goods, hit points, footprint, gate and blocker
    /// tracking. The scenario uses it at setup and the match flow uses it during play, so both get the same thing.
    /// </summary>
    public sealed class EntitySpawner
    {
        private readonly World world;
        private readonly GridMap map;
        private readonly DefinitionCatalog catalog;
        private readonly Logistics goods;
        private readonly Vitals vitals;
        private readonly Structures structures;

        public EntitySpawner(World world, GridMap map, DefinitionCatalog catalog, Logistics goods, Vitals vitals, Structures structures)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.goods = goods ?? throw new ArgumentNullException(nameof(goods));
            this.vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
            this.structures = structures ?? throw new ArgumentNullException(nameof(structures));
        }

        public static EntityKind KindOf(EntityDefinition definition) =>
            definition.NodeResource != null ? EntityKind.ResourceNode : definition.MoveSpeed > 0f ? EntityKind.Unit : EntityKind.Building;

        /// <summary>Is there room for this definition anchored at the cell: in bounds, walkable, and free if it blocks.</summary>
        public bool CanSpawnAt(EntityDefinition definition, Cell anchor)
        {
            List<Cell> footprint = Structures.FootprintAt(definition, anchor);
            for (int i = 0; i < footprint.Count; i++)
                if (!map.InBounds(footprint[i]) || !map.IsWalkable(footprint[i])) return false;
            return true;
        }

        /// <summary>Spawns anchored at the cell, or returns null (with a reason) when it does not fit.</summary>
        public Entity Spawn(string definitionId, SeatId owner, Cell anchor, out string problem)
        {
            problem = null;
            if (!catalog.TryGet(definitionId, out EntityDefinition definition))
            {
                problem = $"unknown definition '{definitionId}'";
                return null;
            }
            List<Cell> footprint = Structures.FootprintAt(definition, anchor);
            for (int i = 0; i < footprint.Count; i++)
            {
                if (map.InBounds(footprint[i]) && map.IsWalkable(footprint[i])) continue;
                problem = $"'{definitionId}' at {anchor} stands on {footprint[i]}, which is not free walkable ground";
                return null;
            }
            // The entity sits at the middle of its footprint, so a 2x2 depot is drawn over the four cells it blocks.
            SimVector2 first = map.CenterOf(footprint[0]), last = map.CenterOf(footprint[footprint.Count - 1]);
            Entity entity = world.Spawn(KindOf(definition), definitionId, owner, (first + last) * 0.5f);
            goods.Attach(entity, definition);
            if (definition.Blocks && !map.TryOccupy(footprint, entity.Id, definition.Destructible))
            {
                problem = $"'{definitionId}' at {anchor} could not claim its footprint";
                world.Despawn(entity.Id, "could not be placed");
                return null;
            }
            if (entity.Kind == EntityKind.Building) structures.AttachBuilt(entity, definition, footprint);
            else if (definition.Blocks) structures.TrackBlocker(entity.Id);
            vitals.Attach(entity, definition);
            return entity;
        }

        /// <summary>A random cell inside the bounds where the definition fits, or null after a bounded number of tries. Uses the world's seeded random.</summary>
        public Cell? RandomCellFor(EntityDefinition definition, CellBounds bounds, int tries)
        {
            for (int i = 0; i < tries; i++)
            {
                var cell = new Cell(world.Random.Range(bounds.MinX, bounds.MaxX + 1), world.Random.Range(bounds.MinY, bounds.MaxY + 1));
                if (CanSpawnAt(definition, cell)) return cell;
            }
            return null;
        }
    }
}
