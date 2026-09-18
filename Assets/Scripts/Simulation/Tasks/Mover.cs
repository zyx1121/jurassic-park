using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    public enum MoverStatus
    {
        /// <summary>No usable route yet; waiting for the next planning attempt.</summary>
        Planning = 1,
        Moving = 2,
        Arrived = 3,
        Failed = 4,
    }

    /// <summary>
    /// The walking part of any task: plan a grid route to a point or to the edge of a footprint, follow it in continuous space
    /// at the unit's speed, and cope with the map changing underneath. Move, gather, deliver and attack all walk through this,
    /// so the rules "never inside a wall" and "replan only when actually cut" exist once.
    /// </summary>
    public sealed class Mover
    {
        private readonly List<SimVector2> waypoints = new List<SimVector2>();
        private readonly List<Cell> routeCells = new List<Cell>();
        private SimVector2 goalPoint;
        private IReadOnlyList<Cell> goalFootprint;
        private bool allowBreach;
        private readonly List<EntityId> breached = new List<EntityId>();
        private int nextWaypoint;
        private long routeMapVersion;
        private long nextPlanTick;
        private int plansWithoutProgress;
        private long lastSpendTick = -1;

        public MoverStatus Status { get; private set; } = MoverStatus.Planning;

        /// <summary>Why planning is waiting or why the walk failed. None while moving normally.</summary>
        public TaskReason Reason { get; private set; }

        /// <summary>Walk to this exact point, or as close as the ground allows when the point itself is not walkable.</summary>
        public void GoToPoint(SimVector2 point)
        {
            goalPoint = point;
            goalFootprint = null;
            allowBreach = false;
            Restart();
        }

        /// <summary>Walk to the cheapest reachable cell beside the footprint: the operating position for gathering, delivering, building or attacking it.</summary>
        /// <param name="breach">Plan through destructible blockers when that is the only or the cheaper way. The blockers on the route are listed in <see cref="Breached"/>.</param>
        public void GoBeside(IReadOnlyList<Cell> footprint, bool breach = false)
        {
            goalFootprint = footprint;
            allowBreach = breach;
            Restart();
        }

        /// <summary>Destructible blockers the current route goes through, in walking order. Empty for a clear route.</summary>
        public IReadOnlyList<EntityId> Breached => breached;

        private void Restart()
        {
            Status = MoverStatus.Planning;
            Reason = TaskReason.None;
            waypoints.Clear();
            routeCells.Clear();
            breached.Clear();
            nextWaypoint = 0;
            nextPlanTick = 0;
            plansWithoutProgress = 0;
            lastSpendTick = -1;
        }

        /// <summary>One tick of walking. Returns the status after it.</summary>
        public MoverStatus Tick(TaskContext context, Entity actor)
        {
            if (Status == MoverStatus.Arrived || Status == MoverStatus.Failed) return Status;
            if (Status == MoverStatus.Planning)
            {
                if (context.World.Tick < nextPlanTick) return Status;
                Plan(context, actor);
                if (Status != MoverStatus.Moving) return Status;
            }
            Follow(context, actor);
            return Status;
        }

        private void Fail(TaskReason reason)
        {
            Status = MoverStatus.Failed;
            Reason = reason;
        }

        private void Plan(TaskContext context, Entity actor)
        {
            if (!context.Catalog.TryGet(actor.DefinitionId, out EntityDefinition definition) || definition.MoveSpeed <= 0f)
            {
                Fail(TaskReason.ActorCannotMove);
                return;
            }
            GridMap map = context.Map;
            Cell start = map.CellAt(actor.Position);
            PathResult path;
            bool endsOnPoint = false;
            if (goalFootprint != null)
            {
                if (IsBeside(map, start, goalFootprint))
                {
                    Status = MoverStatus.Arrived;
                    Reason = TaskReason.None;
                    return;
                }
                PathOptions options = allowBreach ? context.Config.BreachPathOptions : context.Config.UnitPathOptions;
                GridPathfinder.TryFindApproachCell(map, start, goalFootprint, options, out _, out path);
            }
            else
            {
                Cell goal = map.CellAt(goalPoint);
                if (!map.InBounds(goal))
                {
                    Fail(TaskReason.TargetOutOfBounds);
                    return;
                }
                endsOnPoint = map.IsWalkable(goal);
                if (endsOnPoint) path = GridPathfinder.FindPath(map, start, goal, context.Config.UnitPathOptions);
                // Ordered onto a cliff or a building: go as close as the ground allows instead of refusing the click.
                else GridPathfinder.TryFindApproachCell(map, start, new[] { goal }, context.Config.UnitPathOptions, out _, out path);
            }

            switch (path.Status)
            {
                case PathStatus.Found:
                    breached.Clear();
                    for (int i = 0; i < path.Breached.Count; i++) breached.Add(path.Breached[i]);
                    Adopt(map, path, endsOnPoint);
                    Status = MoverStatus.Moving;
                    Reason = TaskReason.None;
                    return;
                case PathStatus.BudgetExceeded:
                    // Unknown, not unreachable: try again later at a limited rate, but not for the whole match.
                    if (!SpendPlanAttempt(context))
                    {
                        Fail(TaskReason.PathSearchBudgetExceeded);
                        return;
                    }
                    nextPlanTick = context.World.Tick + context.Config.ReplanIntervalTicks;
                    Status = MoverStatus.Planning;
                    Reason = TaskReason.PathSearchBudgetExceeded;
                    return;
                default:
                    Fail(TaskReason.NoRoute);
                    return;
            }
        }

        /// <summary>True when the cell is in contact with the footprint, or (for a footprint that does not block) on it. Contact is corner-aware: see <see cref="GridPathfinder.Touches"/>.</summary>
        public static bool IsBeside(GridMap map, Cell cell, IReadOnlyList<Cell> footprint)
        {
            for (int i = 0; i < footprint.Count; i++)
                if (GridPathfinder.Touches(map, cell, footprint[i])) return true;
            return false;
        }

        private void Adopt(GridMap map, PathResult path, bool endsOnPoint)
        {
            waypoints.Clear();
            routeCells.Clear();
            // Cell 0 is where the actor already stands; walking back to its centre first would make every order start with a twitch.
            // A route that goes through a blocker is followed up to the cell before it: the blocker is dealt with there.
            for (int i = 1; i < path.Cells.Count; i++)
            {
                if (!map.BlockerAt(path.Cells[i]).IsNone) break;
                routeCells.Add(path.Cells[i]);
                waypoints.Add(map.CenterOf(path.Cells[i]));
            }
            // The exact point comes after the goal cell's centre, never instead of it: a hop inside one cell cannot leave it,
            // while a straight line from the previous cell to an off-centre point can cross a cell that is not on the route.
            if (endsOnPoint) waypoints.Add(goalPoint);
            nextWaypoint = 0;
            routeMapVersion = path.MapVersion;
        }

        private void Follow(TaskContext context, Entity actor)
        {
            GridMap map = context.Map;
            if (map.Version != routeMapVersion)
            {
                routeMapVersion = map.Version;
                if (RemainingRouteIsCut(map) && !Replan(context, actor)) return;
            }

            if (!context.Catalog.TryGet(actor.DefinitionId, out EntityDefinition definition))
            {
                Fail(TaskReason.ActorCannotMove);
                return;
            }
            float budget = definition.MoveSpeed * context.TickSeconds;
            SimVector2 position = actor.Position;
            while (nextWaypoint < waypoints.Count && budget > 0f)
            {
                SimVector2 target = waypoints[nextWaypoint];
                float distance = SimVector2.Distance(position, target);
                SimVector2 next = distance <= budget ? target : position + (target - position) * (budget / distance);
                // The route check above works on cells of the plan. This one works on the ground actually covered this tick,
                // so no bookkeeping gap can ever put a unit inside a wall: an off-centre first leg after a replan is the known case.
                if (!SegmentIsClear(map, position, next))
                {
                    actor.Position = position;
                    Replan(context, actor);
                    return;
                }
                position = next;
                if (distance <= budget)
                {
                    budget -= distance;
                    nextWaypoint++;
                    plansWithoutProgress = 0;
                }
                else
                {
                    budget = 0f;
                }
            }
            actor.Position = position;
            if (nextWaypoint >= waypoints.Count)
            {
                Status = MoverStatus.Arrived;
                Reason = TaskReason.None;
            }
        }

        /// <summary>
        /// Takes one attempt from the allowance, at most once per tick: a tick that both finds the route cut and runs out of
        /// search budget is one bad tick, not two. Returns false when the allowance is used up.
        /// </summary>
        private bool SpendPlanAttempt(TaskContext context)
        {
            if (lastSpendTick == context.World.Tick) return true;
            if (plansWithoutProgress >= context.Config.MaxReplans) return false;
            lastSpendTick = context.World.Tick;
            plansWithoutProgress++;
            return true;
        }

        /// <summary>Plans again from where the actor stands. Returns true when walking continues on a new route.</summary>
        private bool Replan(TaskContext context, Entity actor)
        {
            if (!SpendPlanAttempt(context))
            {
                Fail(TaskReason.RouteBlocked);
                return false;
            }
            nextPlanTick = context.World.Tick;
            Status = MoverStatus.Planning;
            Reason = TaskReason.RouteBlocked;
            ReplanCount++;
            Plan(context, actor);
            return Status == MoverStatus.Moving;
        }

        /// <summary>How many times a cut route forced a new plan. For events and tests.</summary>
        public int ReplanCount { get; private set; }

        /// <summary>True when every cell the segment passes through, other than the one it starts in, is walkable. Exact grid traversal, no sampling.</summary>
        private static bool SegmentIsClear(GridMap map, SimVector2 from, SimVector2 to)
        {
            Cell cell = map.CellAt(from);
            Cell last = map.CellAt(to);
            if (cell == last) return true;

            float size = map.CellSize;
            float dx = to.X - from.X, dy = to.Y - from.Y;
            int stepX = dx > 0f ? 1 : dx < 0f ? -1 : 0;
            int stepY = dy > 0f ? 1 : dy < 0f ? -1 : 0;
            // Parametric distance along the segment to the next vertical and horizontal grid line, and between consecutive lines.
            float tMaxX = stepX == 0 ? float.PositiveInfinity : (((stepX > 0 ? cell.X + 1 : cell.X) * size) - from.X) / dx;
            float tMaxY = stepY == 0 ? float.PositiveInfinity : (((stepY > 0 ? cell.Y + 1 : cell.Y) * size) - from.Y) / dy;
            float tDeltaX = stepX == 0 ? float.PositiveInfinity : size / System.Math.Abs(dx);
            float tDeltaY = stepY == 0 ? float.PositiveInfinity : size / System.Math.Abs(dy);

            int x = cell.X, y = cell.Y;
            // Bounded by the cell distance, so a rounding slip can never spin here.
            int guard = System.Math.Abs(last.X - x) + System.Math.Abs(last.Y - y) + 2;
            while ((x != last.X || y != last.Y) && guard-- > 0)
            {
                if (tMaxX < tMaxY) { x += stepX; tMaxX += tDeltaX; }
                else if (tMaxY < tMaxX) { y += stepY; tMaxY += tDeltaY; }
                else
                {
                    // Exactly through a corner: the pathfinder only allows that when both side cells are free, so require the same.
                    if (!map.IsWalkable(new Cell(x + stepX, y)) || !map.IsWalkable(new Cell(x, y + stepY))) return false;
                    x += stepX; y += stepY; tMaxX += tDeltaX; tMaxY += tDeltaY;
                }
                if (!map.IsWalkable(new Cell(x, y))) return false;
            }
            return true;
        }

        private bool RemainingRouteIsCut(GridMap map)
        {
            for (int i = nextWaypoint; i < routeCells.Count; i++)
                if (!map.IsWalkable(routeCells[i])) return true;
            return false;
        }
    }
}
