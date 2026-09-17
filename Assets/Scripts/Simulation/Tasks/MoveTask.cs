using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Walk to a point. Plans a grid route, then follows it in continuous space at the unit's speed. The map can change under
    /// it: the route is re-checked whenever the map version moves, and replanned only if the remaining route is actually cut.
    /// </summary>
    public sealed class MoveTask : SimTask
    {
        private readonly SimVector2 destination;
        private readonly List<SimVector2> waypoints = new List<SimVector2>();
        private readonly List<Cell> routeCells = new List<Cell>();
        private int nextWaypoint;
        private long routeMapVersion;
        private long nextPlanTick;
        private int plansWithoutProgress;
        private long lastSpendTick = -1;

        public MoveTask(SimVector2 destination)
        {
            this.destination = destination;
        }

        public override string Kind => "move";

        public SimVector2 Destination => destination;

        protected internal override void Tick(TaskContext context, Entity actor)
        {
            if (State == TaskState.Planning || State == TaskState.Blocked)
            {
                if (context.World.Tick < nextPlanTick) return;
                Plan(context, actor);
                if (State != TaskState.Running) return;
            }
            Follow(context, actor);
        }

        private void Plan(TaskContext context, Entity actor)
        {
            if (!context.Catalog.TryGet(actor.DefinitionId, out EntityDefinition definition) || definition.MoveSpeed <= 0f)
            {
                Enter(context, TaskState.Failed, TaskReason.ActorCannotMove);
                return;
            }
            GridMap map = context.Map;
            Cell goal = map.CellAt(destination);
            if (!map.InBounds(goal))
            {
                Enter(context, TaskState.Failed, TaskReason.TargetOutOfBounds);
                return;
            }
            Cell start = map.CellAt(actor.Position);

            PathResult path;
            bool endsOnDestination = map.IsWalkable(goal);
            if (endsOnDestination)
            {
                path = GridPathfinder.FindPath(map, start, goal, context.Config.UnitPathOptions);
            }
            else
            {
                // Ordered onto a cliff or a building: go as close as the ground allows instead of refusing the click.
                GridPathfinder.TryFindApproachCell(map, start, new[] { goal }, context.Config.UnitPathOptions, out _, out path);
            }

            switch (path.Status)
            {
                case PathStatus.Found:
                    Adopt(map, path, endsOnDestination);
                    Enter(context, TaskState.Running);
                    return;
                case PathStatus.BudgetExceeded:
                    // Unknown, not unreachable: try again later at a limited rate, but not for the whole match.
                    if (!SpendPlanAttempt(context))
                    {
                        Enter(context, TaskState.Failed, TaskReason.PathSearchBudgetExceeded);
                        return;
                    }
                    nextPlanTick = context.World.Tick + context.Config.ReplanIntervalTicks;
                    Enter(context, TaskState.Planning, TaskReason.PathSearchBudgetExceeded);
                    return;
                default:
                    Enter(context, TaskState.Failed, TaskReason.NoRoute);
                    return;
            }
        }

        private void Adopt(GridMap map, PathResult path, bool endsOnDestination)
        {
            waypoints.Clear();
            routeCells.Clear();
            // Cell 0 is where the actor already stands; walking back to its centre first would make every order start with a twitch.
            for (int i = 1; i < path.Cells.Count; i++)
            {
                routeCells.Add(path.Cells[i]);
                waypoints.Add(map.CenterOf(path.Cells[i]));
            }
            // The exact point comes after the goal cell's centre, never instead of it: a hop inside one cell cannot leave it,
            // while a straight line from the previous cell to an off-centre point can cross a cell that is not on the route.
            if (endsOnDestination) waypoints.Add(destination);
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
                Enter(context, TaskState.Failed, TaskReason.ActorCannotMove);
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
            if (nextWaypoint >= waypoints.Count) Enter(context, TaskState.Completed, TaskReason.Arrived);
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

        /// <summary>Plans again from where the actor stands. Returns true when the task is running on a new route.</summary>
        private bool Replan(TaskContext context, Entity actor)
        {
            if (!SpendPlanAttempt(context))
            {
                Enter(context, TaskState.Failed, TaskReason.RouteBlocked);
                return false;
            }
            nextPlanTick = context.World.Tick;
            Enter(context, TaskState.Planning, TaskReason.RouteBlocked);
            Plan(context, actor);
            return State == TaskState.Running;
        }

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
