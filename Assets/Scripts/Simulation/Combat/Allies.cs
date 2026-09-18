using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    public sealed class AllyConfig
    {
        /// <summary>Ticks between an ally seat's looks at its situation.</summary>
        public int ThinkIntervalTicks { get; }

        /// <summary>Wood the ally wants in its depot before it spends on walls.</summary>
        public int WoodReserve { get; }

        /// <summary>Definition to build across camp entrances, the gate that goes in the first entrance so the camp can still be left, and the resource they cost.</summary>
        public string WallDefinitionId { get; }
        public string GateDefinitionId { get; }
        public string Resource { get; }

        public AllyConfig(int thinkIntervalTicks, int woodReserve, string wallDefinitionId, string gateDefinitionId, string resource)
        {
            if (thinkIntervalTicks < 1 || woodReserve < 0) throw new ArgumentOutOfRangeException(nameof(thinkIntervalTicks));
            if (string.IsNullOrEmpty(wallDefinitionId) || string.IsNullOrEmpty(gateDefinitionId) || string.IsNullOrEmpty(resource)) throw new ArgumentException("A wall, a gate and a resource are needed.");
            ThinkIntervalTicks = thinkIntervalTicks;
            WoodReserve = woodReserve;
            WallDefinitionId = wallDefinitionId;
            GateDefinitionId = gateDefinitionId;
            Resource = resource;
        }
    }

    /// <summary>
    /// The computer ally, first version: a fixed loop in its own camp. Idle units gather from the nearest tree until the
    /// depot holds the reserve, then close the camp: a gate in the first entrance, walls in the others; then gather again,
    /// opening the gate while anyone is out gathering and closing it when everyone is home. It never fights and never
    /// rescues. Like every seat it acts only through commands, so it can do nothing a player could not.
    /// </summary>
    public sealed class Allies : ISimSystem
    {
        private readonly TaskContext context;
        private readonly CommandRouter router;
        private readonly AllyConfig config;
        private readonly Dictionary<SeatId, CommandSender> senders = new Dictionary<SeatId, CommandSender>();
        private readonly List<EntityId> idle = new List<EntityId>();
        private readonly List<EntityId> gathering = new List<EntityId>();
        private readonly List<EntityId> one = new List<EntityId>(1);
        private readonly HashSet<Cell> gapsTakenThisThink = new HashSet<Cell>();
        private readonly List<KeyValuePair<EntityId, Entity>> gathersToSend = new List<KeyValuePair<EntityId, Entity>>();

        public Allies(TaskContext context, CommandRouter router, AllyConfig config)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.router = router ?? throw new ArgumentNullException(nameof(router));
            this.config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public void Tick(World world)
        {
            if (context.Seats == null || context.Logistics == null || context.Structures == null || world.Tick % config.ThinkIntervalTicks != 0) return;
            for (int i = 0; i < context.Seats.Seats.Count; i++)
            {
                Seat seat = context.Seats.Seats[i];
                if (seat.Controller != SeatController.Computer) continue;
                Think(world, seat);
            }
        }

        private void Think(World world, Seat seat)
        {
            idle.Clear();
            gathering.Clear();
            Entity depot = null;
            IReadOnlyList<Entity> entities = world.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity entity = entities[i];
                if (!entity.IsAlive || entity.Owner != seat.Id) continue;
                if (!context.Catalog.TryGet(entity.DefinitionId, out EntityDefinition definition)) continue;
                if (entity.Kind == EntityKind.Building && definition.IsDepot && depot == null) depot = entity;
                else if (entity.Kind == EntityKind.Unit && definition.GatherSecondsPerUnit > 0f)
                {
                    SimTask current = context.Tasks?.CurrentOf(entity.Id);
                    if (current == null) idle.Add(entity.Id);
                    // A gatherer keeps gathering until its tree is gone; a wall that can be afforded is worth interrupting it for.
                    else if (current is GatherTask) gathering.Add(entity.Id);
                }
            }
            // A seat without workers or a depot is a seat that lets the dinosaurs eat: nothing to do.
            if ((idle.Count == 0 && gathering.Count == 0) || depot == null) return;
            CampDefinition camp = CampOf(depot);
            int wood = context.Logistics.TryGetContainer(depot.Id, out Container store) ? store.AmountOf(config.Resource) : 0;
            context.Catalog.TryGet(config.WallDefinitionId, out EntityDefinition wall);
            context.Catalog.TryGet(config.GateDefinitionId, out EntityDefinition gate);
            int wallCost = wall != null && wall.BuildCost != null && wall.BuildCost.TryGetValue(config.Resource, out int cost) ? cost : 0;
            int gateCost = gate != null && gate.BuildCost != null && gate.BuildCost.TryGetValue(config.Resource, out int gcost) ? gcost : wallCost;

            CommandSender sender = SenderFor(seat);
            gapsTakenThisThink.Clear();
            gathersToSend.Clear();
            bool someoneComingHome = false;
            // Idle workers first, then gatherers: a wall goes to whoever is free before anyone is pulled off a tree.
            idle.AddRange(gathering);
            for (int i = 0; i < idle.Count; i++)
            {
                bool wasGathering = i >= idle.Count - gathering.Count;
                one.Clear();
                one.Add(idle[i]);
                Cell? gap = camp != null && wall != null && gate != null && wood >= config.WoodReserve + Math.Max(wallCost, gateCost) ? OpenEntrance(camp, wall) : null;
                if (gap.HasValue)
                {
                    // The first entrance gets the gate; the rest get walls.
                    bool isFirst = camp.Entrances.Count > 0 && camp.Entrances[0] == gap.Value;
                    string build = isFirst ? config.GateDefinitionId : config.WallDefinitionId;
                    int buildCost = isFirst ? gateCost : wallCost;
                    // A wall is built from whichever side the builder stands on. Walling the last gap from outside would seal the
                    // builder out of its own camp, so a worker who is outside goes home first and walls on the next look.
                    if (!camp.Bounds.Contains(context.Map.CellAt(WorkerPosition(world, idle[i]))))
                    {
                        sender.Send(CommandKind.Move, one, depot.Position);
                        continue;
                    }
                    sender.Send(CommandKind.Build, one, context.Map.CenterOf(gap.Value), EntityId.None, CommandMode.Replace, context.Catalog.IndexOf(build));
                    // Book the wood so the next worker does not wall the same gap; the site itself takes the cell at the next tick.
                    wood -= buildCost;
                    gapsTakenThisThink.Add(gap.Value);
                    continue;
                }
                if (wasGathering) continue;
                Entity tree = NearestNode(world, depot);
                if (tree != null) gathersToSend.Add(new KeyValuePair<EntityId, Entity>(idle[i], tree));
                // Nothing left to gather: an idle worker outside comes home before the gate is shut behind it.
                else if (camp != null && !camp.Bounds.Contains(context.Map.CellAt(WorkerPosition(world, idle[i]))))
                {
                    sender.Send(CommandKind.Move, one, depot.Position);
                    someoneComingHome = true;
                }
            }
            // The gate first, then the gathers: commands run in arrival order, so a gatherer sent through a closed gate would find no route.
            TendGate(world, seat, sender, camp, gathering.Count > 0 || gathersToSend.Count > 0 || someoneComingHome);
            for (int i = 0; i < gathersToSend.Count; i++)
            {
                one.Clear();
                one.Add(gathersToSend[i].Key);
                sender.Send(CommandKind.Gather, one, gathersToSend[i].Value.Position, gathersToSend[i].Value.Id);
            }
        }

        private static SimVector2 WorkerPosition(World world, EntityId id) => world.TryGet(id, out Entity entity) ? entity.Position : default;

        private CampDefinition CampOf(Entity depot)
        {
            Cell at = context.Map.CellAt(depot.Position);
            IReadOnlyList<CampDefinition> camps = context.Map.Definition.Camps;
            for (int i = 0; i < camps.Count; i++)
                if (camps[i].Bounds.Contains(at)) return camps[i];
            return null;
        }

        /// <summary>The first entrance cell of the camp that has nothing built in it yet, in the camp's order. An open gate is built, not a gap.</summary>
        private Cell? OpenEntrance(CampDefinition camp, EntityDefinition wall)
        {
            for (int i = 0; i < camp.Entrances.Count; i++)
            {
                Cell entrance = camp.Entrances[i];
                if (gapsTakenThisThink.Contains(entrance) || !context.Map.BlockerAt(entrance).IsNone || GateAt(entrance) != null) continue;
                if (context.Map.IsWalkable(entrance) && context.Structures.CheckPlacement(wall, entrance, out _) == CommandRejection.None) return entrance;
            }
            return null;
        }

        /// <summary>The finished gate standing in the cell, open or closed, or null.</summary>
        private Entity GateAt(Cell cell)
        {
            IReadOnlyList<Entity> entities = context.World.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity entity = entities[i];
                if (!entity.IsAlive || entity.Kind != EntityKind.Building || entity.DefinitionId != config.GateDefinitionId) continue;
                if (context.Structures.TryGetSite(entity.Id, out _)) continue;
                if (context.Map.CellAt(entity.Position) == cell) return entity;
            }
            return null;
        }

        /// <summary>Opens the camp's gate while workers are out gathering and closes it when everyone is home.</summary>
        private void TendGate(World world, Seat seat, CommandSender sender, CampDefinition camp, bool anyoneOut)
        {
            if (camp == null || camp.Entrances.Count == 0) return;
            Entity gate = GateAt(camp.Entrances[0]);
            if (gate == null || gate.Owner != seat.Id) return;
            bool open = context.Structures.IsGateOpen(gate.Id);
            if (open == anyoneOut) return;
            if (context.Structures.CheckToggle(gate.Id, seat.Id) != CommandRejection.None) return;
            one.Clear();
            one.Add(gate.Id);
            sender.Send(CommandKind.ToggleGate, one, gate.Position, gate.Id);
        }

        private Entity NearestNode(World world, Entity depot)
        {
            Entity best = null;
            float bestDistance = float.MaxValue;
            IReadOnlyList<Entity> entities = world.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity candidate = entities[i];
                if (!candidate.IsAlive || candidate.Kind != EntityKind.ResourceNode) continue;
                if (!context.Logistics.TryGetNode(candidate.Id, out ResourceNode node) || node.Resource != config.Resource || node.Unreserved < 1) continue;
                float distance = SimVector2.Distance(depot.Position, candidate.Position);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private CommandSender SenderFor(Seat seat)
        {
            if (senders.TryGetValue(seat.Id, out CommandSender sender) && sender.Epoch == seat.ControllerEpoch) return sender;
            router.TryGetSync(seat.Id, out int epoch, out long next);
            sender = new CommandSender(router.Submit, seat.Id, epoch, next);
            senders[seat.Id] = sender;
            return sender;
        }
    }
}
