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
        private int replans;

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
                    // Unknown, not unreachable: stay planning and try again later at a limited rate.
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
            if (endsOnDestination)
            {
                if (waypoints.Count > 0) waypoints[waypoints.Count - 1] = destination;
                else waypoints.Add(destination);
            }
            nextWaypoint = 0;
            routeMapVersion = path.MapVersion;
        }

        private void Follow(TaskContext context, Entity actor)
        {
            GridMap map = context.Map;
            if (map.Version != routeMapVersion)
            {
                routeMapVersion = map.Version;
                if (RemainingRouteIsCut(map))
                {
                    if (replans >= context.Config.MaxReplans)
                    {
                        Enter(context, TaskState.Failed, TaskReason.RouteBlocked);
                        return;
                    }
                    replans++;
                    nextPlanTick = context.World.Tick;
                    Enter(context, TaskState.Planning, TaskReason.RouteBlocked);
                    Plan(context, actor);
                    if (State != TaskState.Running) return;
                }
            }

            context.Catalog.TryGet(actor.DefinitionId, out EntityDefinition definition);
            float budget = definition.MoveSpeed * context.TickSeconds;
            SimVector2 position = actor.Position;
            while (nextWaypoint < waypoints.Count && budget > 0f)
            {
                SimVector2 target = waypoints[nextWaypoint];
                float distance = SimVector2.Distance(position, target);
                if (distance <= budget)
                {
                    position = target;
                    budget -= distance;
                    nextWaypoint++;
                }
                else
                {
                    position = position + (target - position) * (budget / distance);
                    budget = 0f;
                }
            }
            actor.Position = position;
            if (nextWaypoint >= waypoints.Count) Enter(context, TaskState.Completed, TaskReason.Arrived);
        }

        private bool RemainingRouteIsCut(GridMap map)
        {
            for (int i = nextWaypoint; i < routeCells.Count; i++)
                if (!map.IsWalkable(routeCells[i])) return true;
            return false;
        }
    }
}
