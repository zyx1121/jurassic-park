using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace JurassicPark.Infrastructure
{
    public sealed class InfraWorld
    {
        public InfraMap Map { get; }
        public InfraRules Rules { get; }
        public IReadOnlyDictionary<int, InfraEntity> Entities { get; }
        public IReadOnlyList<InfraEvent> Events { get; }
        public float Time { get; private set; }
        public int Revision => occupancyRevision + Map.Revision;
        public int ConsumedMaterials { get; private set; }
        public int InitialMaterials { get; private set; }
        public int PathPlans { get; private set; }

        public int TotalPhysicalMaterials
        {
            get
            {
                int total = 0;
                foreach (InfraEntity entity in entities.Values)
                    total += entity.Stored + entity.Carried + entity.Delivered;
                return total;
            }
        }

        readonly Dictionary<int, InfraEntity> entities = new Dictionary<int, InfraEntity>();
        readonly Dictionary<Vector2Int, int> occupancy = new Dictionary<Vector2Int, int>();
        readonly List<int> actors = new List<int>();
        readonly List<InfraEvent> events = new List<InfraEvent>();
        readonly Dictionary<(int owner, long id), CommandRecord> commands =
            new Dictionary<(int owner, long id), CommandRecord>();
        int nextId = 1;
        int occupancyRevision;

        readonly struct CommandRecord
        {
            public readonly InfraCommand Command;
            public readonly InfraCommandResult Result;
            public CommandRecord(InfraCommand command, InfraCommandResult result)
            {
                Command = command;
                Result = result;
            }
        }

        public InfraWorld(InfraMap map, InfraRules rules)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Rules.Validate(Map.CellSize);
            Entities = new ReadOnlyDictionary<int, InfraEntity>(entities);
            Events = events.AsReadOnly();
        }

        public InfraEntity AddEntity(InfraEntityKind kind, Vector2Int cell, int owner = 0, int stock = 0)
        {
            if (!Enum.IsDefined(typeof(InfraEntityKind), kind) || owner < 0 || stock < 0)
                throw new ArgumentOutOfRangeException(nameof(kind));
            if (!IsWalkable(cell))
                throw new ArgumentException("An entity must start on unoccupied ground.", nameof(cell));
            bool stores = kind == InfraEntityKind.Source || kind == InfraEntityKind.Depot ||
                kind == InfraEntityKind.GroundPile || kind == InfraEntityKind.Blueprint;
            if (!stores && stock != 0)
                throw new ArgumentException("Only material stores or blueprints accept initial stock.", nameof(stock));
            if ((kind == InfraEntityKind.Depot && stock > Rules.DepotCapacity) ||
                (kind == InfraEntityKind.Blueprint && stock > Rules.WallCost))
                throw new ArgumentOutOfRangeException(nameof(stock));
            if (Blocks(kind) && ActorIn(cell))
                throw new ArgumentException("An actor occupies this footprint.", nameof(cell));
            InfraEntity entity = CreateEntity(kind, cell, owner, stock);
            InitialMaterials += stock;
            return entity;
        }

        InfraEntity CreateEntity(InfraEntityKind kind, Vector2Int cell, int owner, int stock)
        {
            var entity = new InfraEntity
            {
                Id = nextId++,
                Kind = kind,
                Owner = owner,
                Cell = cell,
                Position = Map.CellCenter(cell),
                Health = kind == InfraEntityKind.Worker ? Rules.WorkerHealth :
                    kind == InfraEntityKind.Dinosaur ? Rules.DinosaurHealth :
                    kind == InfraEntityKind.Wall || kind == InfraEntityKind.Blueprint ?
                    Rules.WallHealth : Rules.StructureHealth,
                Capacity = kind == InfraEntityKind.Worker ? Rules.CarryCapacity :
                    kind == InfraEntityKind.Depot ? Rules.DepotCapacity :
                    kind == InfraEntityKind.Blueprint ? Rules.WallCost :
                    kind == InfraEntityKind.Source || kind == InfraEntityKind.GroundPile ? int.MaxValue : 0,
                RequiredMaterials = kind == InfraEntityKind.Blueprint ? Rules.WallCost : 0,
                Stored = kind == InfraEntityKind.Blueprint ? 0 : stock,
                Delivered = kind == InfraEntityKind.Blueprint ? stock : 0
            };
            entities.Add(entity.Id, entity);
            if (IsActor(kind))
                actors.Add(entity.Id);
            if (Blocks(kind))
            {
                occupancy.Add(cell, entity.Id);
                occupancyRevision++;
            }
            Log(entity.Id, "Created " + kind);
            return entity;
        }

        public InfraCommandResult Submit(InfraCommand command)
        {
            var key = (command.Owner, command.CommandId);
            if (commands.TryGetValue(key, out CommandRecord previous))
            {
                if (previous.Command.Equals(command))
                    return previous.Result;
                return Reject(command, "Command ID reused with different payload.");
            }

            string error = Validate(command, out InfraEntity actor);
            InfraCommandResult result;
            if (error != null)
                result = Reject(command, error);
            else if (command.Kind == InfraCommandKind.CancelBuild)
            {
                Destroy(entities[command.TargetId], "Construction cancelled", true);
                result = new InfraCommandResult(true, "Construction cancelled.", command.TargetId);
            }
            else if (command.Kind == InfraCommandKind.Stop)
            {
                EndTask(actor, InfraTaskStatus.Cancelled, "Stopped", true);
                result = new InfraCommandResult(true, "Stopped.");
            }
            else
            {
                EndTask(actor, InfraTaskStatus.Cancelled, "Replaced by new command", true);
                int targetId = command.TargetId;
                if (command.Kind == InfraCommandKind.Build)
                    targetId = AddEntity(InfraEntityKind.Blueprint, command.Cell, command.Owner).Id;
                actor.Task = new InfraTask
                {
                    Command = command,
                    TargetId = targetId,
                    Phase = command.Kind == InfraCommandKind.Move ? InfraPhase.Move :
                        command.Kind == InfraCommandKind.Attack ? InfraPhase.Attack : InfraPhase.ChooseHaul
                };
                actor.TargetId = targetId;
                State(actor, InfraTaskStatus.Queued, "Queued", "");
                result = new InfraCommandResult(true, "Queued.", targetId);
            }
            commands.Add(key, new CommandRecord(command, result));
            if (result.Accepted)
                Log(command.ActorId, command.Kind + ": " + result.Reason);
            return result;
        }

        string Validate(InfraCommand command, out InfraEntity actor)
        {
            actor = null;
            if (command.Kind == InfraCommandKind.Build && command.CommandId >= 0)
            {
                if (!CanBuild(command.Owner, command.ActorId, command.Cell, out string reason))
                    return reason;
                actor = entities[command.ActorId];
                return null;
            }
            if (command.Owner < 0 || command.CommandId < 0)
                return "Invalid owner or command ID.";
            if (!entities.TryGetValue(command.ActorId, out actor) || !actor.IsAlive)
                return "Unknown or dead actor.";
            if (actor.Owner != command.Owner)
                return "Owner does not control actor.";
            if (!IsActor(actor.Kind) || !Enum.IsDefined(typeof(InfraCommandKind), command.Kind))
                return "Actor lacks this capability.";
            if (!Map.Contains(command.Cell))
                return "Cell is outside the map.";
            if ((command.Kind == InfraCommandKind.Gather || command.Kind == InfraCommandKind.CancelBuild) &&
                actor.Kind != InfraEntityKind.Worker)
                return "Actor lacks this capability.";
            if (command.Kind == InfraCommandKind.Attack && actor.Kind != InfraEntityKind.Dinosaur)
                return "Only dinosaurs can attack in this slice.";
            if (command.Kind == InfraCommandKind.Move && !IsWalkable(command.Cell))
                return "Move destination is blocked.";
            if (command.Kind == InfraCommandKind.Gather || command.Kind == InfraCommandKind.Attack ||
                command.Kind == InfraCommandKind.CancelBuild)
            {
                if (!Alive(command.TargetId, out InfraEntity target))
                    return "Unknown or dead target.";
                if (command.Kind == InfraCommandKind.Gather && !Gatherable(target, actor.Owner))
                    return "Target is not a usable source or ground pile.";
                if (command.Kind == InfraCommandKind.CancelBuild &&
                    (target.Kind != InfraEntityKind.Blueprint || target.Owner != command.Owner))
                    return "Target is not an owned blueprint.";
                if (command.Kind == InfraCommandKind.Attack &&
                    (target.Owner == command.Owner || target.Kind == InfraEntityKind.GroundPile))
                    return "Target is not an attackable enemy.";
            }
            return null;
        }

        public bool CanBuild(int owner, int actorId, Vector2Int cell, out string reason)
        {
            reason = "";
            if (owner < 0)
                reason = "Invalid owner.";
            else if (!Alive(actorId, out InfraEntity actor))
                reason = "Unknown or dead actor.";
            else if (actor.Owner != owner)
                reason = "Owner does not control actor.";
            else if (actor.Kind != InfraEntityKind.Worker)
                reason = "Actor lacks this capability.";
            else if (!Map.Contains(cell))
                reason = "Cell is outside the map.";
            else if (!IsWalkable(cell))
                reason = "Build footprint is blocked or already reserved.";
            else if (ActorIn(cell))
                reason = "An actor occupies the build footprint.";
            else if (FindRoute(actor, InteractionCells(cell), false, cell) == null)
                reason = "No reachable construction side.";
            return reason.Length == 0;
        }

        InfraCommandResult Reject(InfraCommand command, string reason)
        {
            Log(command.ActorId, "Rejected " + command.Kind + ": " + reason);
            return new InfraCommandResult(false, reason);
        }

        public void Tick(float deltaSeconds)
        {
            if (!InfraMap.Finite(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            float dt = Mathf.Min(deltaSeconds, Rules.MaxTickSeconds);
            if (dt <= 0)
                return;
            Time += dt;
            for (int i = 0; i < actors.Count; i++)
            {
                InfraEntity actor = entities[actors[i]];
                InfraTask task = actor.Task;
                if (!actor.IsAlive || task == null)
                    continue;
                if (actor.TaskStatus == InfraTaskStatus.Blocked)
                {
                    if (Time < task.RetryAt && task.BlockedRevision == Revision)
                        continue;
                    State(actor, InfraTaskStatus.Planning, "Replanning", "");
                }
                switch (task.Phase)
                {
                    case InfraPhase.Move: StepMove(actor, task, dt); break;
                    case InfraPhase.ChooseHaul: ChooseHaul(actor, task); break;
                    case InfraPhase.Pickup: StepPickup(actor, task, dt); break;
                    case InfraPhase.Dropoff: StepDropoff(actor, task, dt); break;
                    case InfraPhase.Construct: StepConstruct(actor, task, dt); break;
                    case InfraPhase.Attack: StepAttack(actor, task, dt); break;
                }
            }
        }

        void StepMove(InfraEntity actor, InfraTask task, float dt)
        {
            if (!EnsurePlan(actor, task, new List<Vector2Int> { task.Command.Cell }, false))
            {
                Block(actor, task, "Move destination unreachable");
                return;
            }
            State(actor, InfraTaskStatus.Running, "Walking", "");
            if (Follow(actor, task.Plan, dt, out _))
                EndTask(actor, InfraTaskStatus.Completed, "Arrived", false);
        }

        void ChooseHaul(InfraEntity actor, InfraTask task)
        {
            bool build = task.Command.Kind == InfraCommandKind.Build;
            InfraEntity destination;
            if (build)
            {
                if (!Alive(task.TargetId, out destination) || destination.Kind != InfraEntityKind.Blueprint)
                {
                    EndTask(actor, InfraTaskStatus.Failed, "Construction site gone", false);
                    return;
                }
                if (destination.MaterialsConsumed || destination.Delivered >= destination.RequiredMaterials)
                {
                    task.Phase = InfraPhase.Construct;
                    task.Plan = null;
                    return;
                }
                if (FindRoute(actor, InteractionCells(destination.Cell), false) == null)
                {
                    Block(actor, task, "Construction site unreachable");
                    return;
                }
            }
            else
                destination = null;

            if (actor.Carried > 0)
            {
                if (!build && !FindDepot(actor, out destination, out string reason))
                {
                    Block(actor, task, reason);
                    return;
                }
                int room = Room(destination);
                int amount = Mathf.Min(actor.Carried, room);
                if (amount <= 0)
                {
                    Block(actor, task, "Destination full or reserved");
                    return;
                }
                ReserveDestination(task, destination, amount);
                task.Phase = InfraPhase.Dropoff;
                task.Plan = null;
                task.Elapsed = 0;
                State(actor, InfraTaskStatus.Running, "Planning delivery", "");
                return;
            }

            InfraEntity source;
            if (build)
            {
                if (!FindBuildSource(actor, out source, out string reason))
                {
                    Block(actor, task, reason);
                    return;
                }
            }
            else
            {
                if (!Alive(task.TargetId, out source))
                {
                    EndTask(actor, InfraTaskStatus.Failed, "Source gone", false);
                    return;
                }
                if (source.Stored == 0)
                {
                    EndTask(actor, InfraTaskStatus.Completed, "Source depleted", false);
                    return;
                }
                if (source.Stored - source.OutgoingReserved <= 0)
                {
                    Block(actor, task, "Source stock reserved by another worker");
                    return;
                }
                if (FindRoute(actor, InteractionCells(source.Cell), false) == null)
                {
                    Block(actor, task, "Source unreachable");
                    return;
                }
                if (!FindDepot(actor, out destination, out string reason))
                {
                    Block(actor, task, reason);
                    return;
                }
            }
            int pickup = Mathf.Min(actor.Capacity, source.Stored - source.OutgoingReserved);
            pickup = Mathf.Min(pickup, Room(destination));
            if (pickup <= 0)
            {
                Block(actor, task, "Destination full or reserved");
                return;
            }
            task.SourceId = source.Id;
            task.SourceAmount = pickup;
            source.OutgoingReserved += pickup;
            ReserveDestination(task, destination, pickup);
            task.Phase = InfraPhase.Pickup;
            task.Plan = null;
            task.Elapsed = 0;
            State(actor, InfraTaskStatus.Running, "Planning pickup", "");
        }

        bool FindDepot(InfraEntity actor, out InfraEntity found, out string reason)
        {
            found = null;
            bool any = false;
            bool room = false;
            int best = int.MaxValue;
            foreach (InfraEntity candidate in entities.Values)
            {
                if (!candidate.IsAlive || candidate.Kind != InfraEntityKind.Depot || candidate.Owner != actor.Owner)
                    continue;
                any = true;
                if (Room(candidate) <= 0)
                    continue;
                room = true;
                List<Vector2Int> route = FindRoute(actor, InteractionCells(candidate.Cell), false);
                if (route != null && route.Count < best)
                {
                    found = candidate;
                    best = route.Count;
                }
            }
            reason = !any ? "No owned depot" : !room ? "All depots full or reserved" : "Depot unreachable";
            return found != null;
        }

        bool FindBuildSource(InfraEntity actor, out InfraEntity found, out string reason)
        {
            found = null;
            bool stock = false;
            int best = int.MaxValue;
            foreach (InfraEntity candidate in entities.Values)
            {
                bool usable = Gatherable(candidate, actor.Owner) ||
                    (candidate.Kind == InfraEntityKind.Depot && candidate.Owner == actor.Owner);
                if (!candidate.IsAlive || !usable || candidate.Stored - candidate.OutgoingReserved <= 0)
                    continue;
                stock = true;
                List<Vector2Int> route = FindRoute(actor, InteractionCells(candidate.Cell), false);
                if (route != null && route.Count < best)
                {
                    found = candidate;
                    best = route.Count;
                }
            }
            reason = stock ? "Material source unreachable" : "No available materials (empty or reserved)";
            return found != null;
        }

        void StepPickup(InfraEntity actor, InfraTask task, float dt)
        {
            if (!Alive(task.SourceId, out InfraEntity source) || task.SourceAmount <= 0)
            {
                ResetHaul(task);
                return;
            }
            if (!EnsurePlan(actor, task, InteractionCells(source.Cell), false))
            {
                Block(actor, task, "Source unreachable");
                return;
            }
            State(actor, InfraTaskStatus.Running, "Walking to material", "");
            if (!Follow(actor, task.Plan, dt, out _))
                return;
            State(actor, InfraTaskStatus.Running, "Gathering", "");
            task.Elapsed += dt;
            if (task.Elapsed < Rules.GatherSeconds)
                return;
            int amount = task.SourceAmount;
            source.Stored -= amount;
            source.OutgoingReserved -= amount;
            actor.Carried += amount;
            task.SourceId = 0;
            task.SourceAmount = 0;
            task.Elapsed = 0;
            task.Plan = null;
            task.Phase = InfraPhase.Dropoff;
            Log(actor.Id, "Picked up " + amount + " from " + source.Id);
        }

        void StepDropoff(InfraEntity actor, InfraTask task, float dt)
        {
            if (!Alive(task.DestinationId, out InfraEntity destination) || task.DestinationAmount <= 0)
            {
                ResetHaul(task);
                return;
            }
            if (!EnsurePlan(actor, task, InteractionCells(destination.Cell), false))
            {
                Block(actor, task, "Destination unreachable");
                return;
            }
            State(actor, InfraTaskStatus.Running, "Hauling", "");
            if (!Follow(actor, task.Plan, dt, out _))
                return;
            State(actor, InfraTaskStatus.Running, "Transferring", "");
            task.Elapsed += dt;
            if (task.Elapsed < Rules.TransferSeconds)
                return;
            int amount = task.DestinationAmount;
            actor.Carried -= amount;
            destination.IncomingReserved -= amount;
            if (destination.Kind == InfraEntityKind.Blueprint)
                destination.Delivered += amount;
            else
                destination.Stored += amount;
            task.DestinationId = 0;
            task.DestinationAmount = 0;
            task.Elapsed = 0;
            task.Plan = null;
            task.Phase = InfraPhase.ChooseHaul;
            Log(actor.Id, "Delivered " + amount + " to " + destination.Id);
        }

        void StepConstruct(InfraEntity actor, InfraTask task, float dt)
        {
            if (!Alive(task.TargetId, out InfraEntity site) || site.Kind != InfraEntityKind.Blueprint)
            {
                EndTask(actor, InfraTaskStatus.Failed, "Construction site gone", false);
                return;
            }
            if (!EnsurePlan(actor, task, InteractionCells(site.Cell), false))
            {
                Block(actor, task, "Construction site unreachable");
                return;
            }
            State(actor, InfraTaskStatus.Running, "Walking to construction", "");
            if (!Follow(actor, task.Plan, dt, out _))
                return;
            if (!site.MaterialsConsumed)
            {
                if (site.Delivered < site.RequiredMaterials)
                {
                    task.Phase = InfraPhase.ChooseHaul;
                    task.Plan = null;
                    return;
                }
                site.Delivered -= site.RequiredMaterials;
                ConsumedMaterials += site.RequiredMaterials;
                site.MaterialsConsumed = true;
                Log(site.Id, "Consumed " + site.RequiredMaterials + " construction materials");
            }
            State(actor, InfraTaskStatus.Running, "Constructing", "");
            site.BuildProgress += dt;
            if (site.BuildProgress < Rules.BuildSeconds)
                return;
            site.Kind = InfraEntityKind.Wall;
            site.Capacity = 0;
            site.TaskStatus = InfraTaskStatus.Completed;
            site.Action = "Built";
            occupancyRevision++;
            Log(site.Id, "Wall completed");
            EndTask(actor, InfraTaskStatus.Completed, "Wall completed", false);
        }

        void StepAttack(InfraEntity actor, InfraTask task, float dt)
        {
            if (!Alive(task.TargetId, out InfraEntity target))
            {
                EndTask(actor, InfraTaskStatus.Completed, "Target destroyed", false);
                return;
            }
            if (CanStrike(actor, target))
            {
                Strike(actor, task, target, dt);
                if (!target.IsAlive)
                    EndTask(actor, InfraTaskStatus.Completed, "Target destroyed", false);
                return;
            }
            if (!task.HasAttackCell || task.AttackCell != target.Cell)
            {
                task.Plan = null;
                task.AttackCell = target.Cell;
                task.HasAttackCell = true;
            }
            List<Vector2Int> goals = AttackCells(target.Cell, actor.Owner);
            if (IsActor(target.Kind) && IsWalkable(target.Cell))
                goals.Add(target.Cell);
            // A moving target can leave strike range without crossing a cell boundary.
            if (task.Plan != null && task.Plan.Index >= task.Plan.Cells.Count && IsActor(target.Kind))
            {
                task.Plan = null;
                goals = new List<Vector2Int> { target.Cell };
            }
            if (!EnsurePlan(actor, task, goals, true))
            {
                task.AttackVictim = 0;
                task.Elapsed = 0;
                Block(actor, task, "Target unreachable; no useful breach route");
                return;
            }
            State(actor, InfraTaskStatus.Running, "Pursuing", "");
            Follow(actor, task.Plan, dt, out InfraEntity blocker);
            if (blocker != null && CanStrike(actor, blocker))
                Strike(actor, task, blocker, dt);
            else
            {
                task.AttackVictim = 0;
                task.Elapsed = 0;
            }
        }

        bool CanStrike(InfraEntity actor, InfraEntity target)
        {
            if (!Map.Contains(actor.Cell) || !Map.Contains(target.Cell) ||
                Map.TileAt(actor.Cell) != InfraTileKind.Ground || Map.TileAt(target.Cell) != InfraTileKind.Ground)
                return false;
            int gridDistance = Mathf.Abs(actor.Cell.x - target.Cell.x) + Mathf.Abs(actor.Cell.y - target.Cell.y);
            return gridDistance <= 1 &&
                Vector2.Distance(actor.Position, Map.CellCenter(actor.Cell)) <= Rules.ArrivalTolerance &&
                Vector2.Distance(actor.Position, target.Position) <= Map.CellSize + Rules.ArrivalTolerance;
        }

        void Strike(InfraEntity actor, InfraTask task, InfraEntity victim, float dt)
        {
            if (task.AttackVictim != victim.Id)
            {
                task.AttackVictim = victim.Id;
                task.Elapsed = 0;
            }
            State(actor, InfraTaskStatus.Running,
                victim.Id == task.TargetId ? "Attacking" : "Breaching " + victim.Id, "");
            task.Elapsed += dt;
            if (task.Elapsed < Rules.AttackInterval)
                return;
            task.Elapsed -= Rules.AttackInterval;
            Damage(victim.Id, Rules.AttackDamage);
        }

        bool EnsurePlan(InfraEntity actor, InfraTask task, List<Vector2Int> goals, bool breach)
        {
            if (task.Plan != null && task.Plan.Revision == Revision)
                return true;
            State(actor, InfraTaskStatus.Planning, "Planning route", "");
            List<Vector2Int> cells = FindRoute(actor, goals, false);
            bool usesBreach = false;
            if (cells == null && breach)
            {
                cells = FindRoute(actor, goals, true);
                usesBreach = true;
            }
            task.Plan = cells == null ? null : new InfraPlan
            {
                Cells = cells,
                Goals = goals,
                Revision = Revision,
                AllowBreach = usesBreach
            };
            return task.Plan != null;
        }

        List<Vector2Int> FindRoute(InfraEntity actor, List<Vector2Int> goals, bool breach,
            Vector2Int? extraBlocked = null)
        {
            PathPlans++;
            if (!Map.Contains(actor.Cell) || Map.TileAt(actor.Cell) != InfraTileKind.Ground)
                return null;
            var validGoals = new List<Vector2Int>();
            foreach (Vector2Int goal in goals)
                if ((!extraBlocked.HasValue || goal != extraBlocked.Value) &&
                    (IsWalkable(goal) || (breach && Map.Contains(goal) &&
                        Map.TileAt(goal) == InfraTileKind.Ground && occupancy.TryGetValue(goal, out int goalId) &&
                        Breachable(entities[goalId], actor.Owner))))
                    validGoals.Add(goal);
            return InfraPathfinder.Find(Map, actor.Cell, validGoals, cell =>
            {
                if (Map.TileAt(cell) != InfraTileKind.Ground ||
                    (extraBlocked.HasValue && cell == extraBlocked.Value))
                    return float.PositiveInfinity;
                float travel = Map.CellSize / Rules.MoveSpeed;
                if (!occupancy.TryGetValue(cell, out int id))
                    return travel;
                InfraEntity obstruction = entities[id];
                if (breach && Breachable(obstruction, actor.Owner))
                    return travel + obstruction.Health * Rules.AttackInterval / Rules.AttackDamage;
                return float.PositiveInfinity;
            });
        }

        bool Follow(InfraEntity actor, InfraPlan plan, float dt, out InfraEntity blocker)
        {
            blocker = null;
            float remaining = Rules.MoveSpeed * dt;
            while (plan.Index < plan.Cells.Count)
            {
                Vector2Int cell = plan.Cells[plan.Index];
                if (!Map.Contains(cell) || Map.TileAt(cell) != InfraTileKind.Ground)
                    return false;
                if (occupancy.TryGetValue(cell, out int occupantId))
                {
                    InfraEntity occupant = entities[occupantId];
                    if (plan.AllowBreach && Breachable(occupant, actor.Owner))
                        blocker = occupant;
                    return false;
                }
                Vector2 point = Map.CellCenter(cell);
                float distance = Vector2.Distance(actor.Position, point);
                if (distance <= Rules.ArrivalTolerance)
                {
                    actor.Position = point;
                    actor.Cell = cell;
                    plan.Index++;
                    continue;
                }
                if (remaining <= 0)
                    return false;
                float step = Mathf.Min(remaining, distance);
                actor.Position = Vector2.MoveTowards(actor.Position, point, step);
                actor.Cell = Map.WorldToCell(actor.Position);
                remaining -= step;
                if (step < distance)
                    return false;
            }
            return true;
        }

        List<Vector2Int> InteractionCells(Vector2Int cell)
        {
            var cells = new List<Vector2Int>();
            foreach (Vector2Int direction in InfraPathfinder.Directions)
                if (IsWalkable(cell + direction))
                    cells.Add(cell + direction);
            return cells;
        }

        List<Vector2Int> AttackCells(Vector2Int cell, int owner)
        {
            var cells = new List<Vector2Int>();
            foreach (Vector2Int direction in InfraPathfinder.Directions)
            {
                Vector2Int next = cell + direction;
                if (IsWalkable(next) || (Map.Contains(next) && Map.TileAt(next) == InfraTileKind.Ground &&
                    occupancy.TryGetValue(next, out int id) && Breachable(entities[id], owner)))
                    cells.Add(next);
            }
            return cells;
        }

        public void Damage(int entityId, float damage)
        {
            if (!InfraMap.Finite(damage) || damage < 0)
                throw new ArgumentOutOfRangeException(nameof(damage));
            if (!Alive(entityId, out InfraEntity entity) || damage == 0)
                return;
            entity.Health = Mathf.Max(0, entity.Health - damage);
            if (!entity.IsAlive)
                Destroy(entity, "Destroyed");
        }

        void Destroy(InfraEntity entity, string reason, bool cancelled = false)
        {
            entity.Health = 0;
            InfraTaskStatus status = cancelled ? InfraTaskStatus.Cancelled : InfraTaskStatus.Failed;
            EndTask(entity, status, reason, true);
            if (Blocks(entity.Kind) && occupancy.Remove(entity.Cell))
                occupancyRevision++;
            int stock = entity.Stored + entity.Delivered + entity.Carried;
            entity.Stored = 0;
            entity.Delivered = 0;
            entity.Carried = 0;
            for (int i = 0; i < actors.Count; i++)
            {
                InfraEntity actor = entities[actors[i]];
                InfraTask task = actor.Task;
                if (task == null)
                    continue;
                if (task.Command.Kind == InfraCommandKind.Build && task.TargetId == entity.Id)
                    EndTask(actor, status, "Construction site gone: " + reason, false);
                else if (task.SourceId == entity.Id || task.DestinationId == entity.Id)
                    ResetHaul(task);
            }
            entity.OutgoingReserved = 0;
            entity.IncomingReserved = 0;
            if (stock > 0)
                DropPile(entity.Cell, entity.Owner, stock);
            Log(entity.Id, reason);
        }

        void DropPile(Vector2Int cell, int owner, int amount)
        {
            foreach (InfraEntity entity in entities.Values)
            {
                if (entity.IsAlive && entity.Kind == InfraEntityKind.GroundPile &&
                    entity.Cell == cell && entity.Owner == owner)
                {
                    entity.Stored += amount;
                    Log(entity.Id, "Recovered " + amount + " dropped materials");
                    return;
                }
            }
            // Internal transfer, deliberately not AddEntity: dropping must not increase the baseline.
            CreateEntity(InfraEntityKind.GroundPile, cell, owner, amount);
        }

        void EndTask(InfraEntity actor, InfraTaskStatus status, string reason, bool cancelBuild)
        {
            InfraTask previous = actor.Task;
            actor.Task = null;
            if (previous != null)
            {
                ReleaseClaims(previous);
                if (cancelBuild && previous.Command.Kind == InfraCommandKind.Build &&
                    Alive(previous.TargetId, out InfraEntity site) && site.Kind == InfraEntityKind.Blueprint)
                    Destroy(site, "Builder work cancelled", true);
            }
            State(actor, status, status == InfraTaskStatus.Completed ? "Completed" :
                status == InfraTaskStatus.Failed ? "Failed" : "Stopped", reason);
        }

        void Block(InfraEntity actor, InfraTask task, string reason)
        {
            if (task.Command.Kind == InfraCommandKind.Gather || task.Command.Kind == InfraCommandKind.Build)
                ResetHaul(task);
            else
                task.Plan = null;
            task.RetryAt = Time + Rules.ReplanSeconds;
            task.BlockedRevision = Revision;
            State(actor, InfraTaskStatus.Blocked, "Waiting", reason);
        }

        void ResetHaul(InfraTask task)
        {
            ReleaseClaims(task);
            task.Phase = InfraPhase.ChooseHaul;
            task.Plan = null;
            task.Elapsed = 0;
        }

        void ReleaseClaims(InfraTask task)
        {
            if (task.SourceAmount > 0 && entities.TryGetValue(task.SourceId, out InfraEntity source))
                source.OutgoingReserved -= task.SourceAmount;
            if (task.DestinationAmount > 0 && entities.TryGetValue(task.DestinationId, out InfraEntity destination))
                destination.IncomingReserved -= task.DestinationAmount;
            task.SourceId = 0;
            task.SourceAmount = 0;
            task.DestinationId = 0;
            task.DestinationAmount = 0;
        }

        static void ReserveDestination(InfraTask task, InfraEntity destination, int amount)
        {
            task.DestinationId = destination.Id;
            task.DestinationAmount = amount;
            destination.IncomingReserved += amount;
        }

        static int Room(InfraEntity entity) => entity.Kind == InfraEntityKind.Blueprint ?
            entity.RequiredMaterials - entity.Delivered - entity.IncomingReserved :
            entity.Capacity - entity.Stored - entity.IncomingReserved;

        public bool IsWalkable(Vector2Int cell) => Map.Contains(cell) &&
            Map.TileAt(cell) == InfraTileKind.Ground && !occupancy.ContainsKey(cell);

        bool ActorIn(Vector2Int cell)
        {
            foreach (int id in actors)
                if (entities[id].IsAlive && entities[id].Cell == cell)
                    return true;
            return false;
        }

        bool Alive(int id, out InfraEntity entity) => entities.TryGetValue(id, out entity) && entity.IsAlive;
        static bool IsActor(InfraEntityKind kind) => kind == InfraEntityKind.Worker || kind == InfraEntityKind.Dinosaur;
        static bool Blocks(InfraEntityKind kind) => kind == InfraEntityKind.Source || kind == InfraEntityKind.Depot ||
            kind == InfraEntityKind.Blueprint || kind == InfraEntityKind.Wall;
        static bool Breachable(InfraEntity entity, int owner) => entity.IsAlive && entity.Owner != owner &&
            (entity.Kind == InfraEntityKind.Wall || entity.Kind == InfraEntityKind.Blueprint);
        static bool Gatherable(InfraEntity entity, int owner) => entity.Kind == InfraEntityKind.Source ||
            (entity.Kind == InfraEntityKind.GroundPile && (entity.Owner == owner || entity.Owner == 0));

        void State(InfraEntity entity, InfraTaskStatus status, string action, string reason)
        {
            if (entity.TaskStatus != status || entity.Reason != reason)
                Log(entity.Id, status + (reason.Length > 0 ? ": " + reason : ""));
            entity.TaskStatus = status;
            entity.Action = action;
            entity.Reason = reason;
        }

        void Log(int entityId, string message)
        {
            while (events.Count >= Rules.EventLogCapacity)
                events.RemoveAt(0);
            events.Add(new InfraEvent(Time, entityId, message));
        }
    }
}
