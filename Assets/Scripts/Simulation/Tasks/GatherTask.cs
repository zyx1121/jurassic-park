using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Gather from one node and keep carrying to a depot until the node runs out: walk beside the node, work until the pack is
    /// full, walk beside the nearest usable depot, hand over, repeat. Node stock and depot room are reserved before walking, so
    /// two gatherers never count on the same last log and a depot never receives more than it promised room for.
    /// </summary>
    public sealed class GatherTask : SimTask
    {
        private enum Phase { Decide, ToNode, Working, ToDepot }

        private readonly EntityId nodeId;
        private readonly Mover mover = new Mover();
        private readonly HashSet<EntityId> unreachableDepots = new HashSet<EntityId>();
        private Phase phase = Phase.Decide;
        private Reservation stock, room;
        private EntityId depotId;
        private float work;
        private long retryAtTick;
        private bool lastTrip;

        public GatherTask(EntityId node) => nodeId = node;

        public override string Kind => "gather";
        public EntityId Node => nodeId;

        protected internal override void Release(TaskContext context)
        {
            context.Logistics?.Release(stock);
            context.Logistics?.Release(room);
        }

        protected internal override void Tick(TaskContext context, Entity actor)
        {
            Logistics goods = context.Logistics;
            if (!LogisticsQueries.CanGather(context, actor))
            {
                Enter(context, TaskState.Failed, TaskReason.ActorCannotCarry);
                return;
            }
            goods.TryGetContainer(actor.Id, out Container pack);
            goods.Renew(stock);
            goods.Renew(room);

            if (phase == Phase.Decide && !Decide(context, actor, pack)) return;
            switch (phase)
            {
                case Phase.ToNode: WalkToNode(context, actor); break;
                case Phase.Working: Work(context, actor, pack); break;
                case Phase.ToDepot: WalkToDepot(context, actor, pack); break;
            }
        }

        /// <summary>Chooses the next leg. Returns false when the task ended or is waiting.</summary>
        private bool Decide(TaskContext context, Entity actor, Container pack)
        {
            if (context.World.Tick < retryAtTick) return false;
            Logistics goods = context.Logistics;
            bool nodeUsable = context.World.IsAlive(nodeId) && goods.TryGetNode(nodeId, out ResourceNode node) && node.Remaining > 0;
            if (!nodeUsable) lastTrip = true;

            if (pack.Total > 0 && (lastTrip || pack.FreeCapacity < 1)) return HeadForDepot(context, actor, pack);
            if (lastTrip)
            {
                Enter(context, TaskState.Completed, TaskReason.NodeDepleted);
                return false;
            }

            goods.TryGetNode(nodeId, out node);
            stock = goods.Reserve(ReservationKind.NodeStock, nodeId, node.Resource, pack.FreeCapacity, Id);
            if (stock == null)
            {
                // Everything left is promised to other gatherers. Carry home what we have; otherwise wait and look again.
                if (pack.Total > 0) return HeadForDepot(context, actor, pack);
                return Wait(context, TaskReason.SourceEmpty);
            }
            context.World.TryGet(nodeId, out Entity nodeEntity);
            mover.GoBeside(LogisticsQueries.FootprintOf(context, nodeEntity));
            phase = Phase.ToNode;
            Enter(context, TaskState.Running);
            return true;
        }

        private bool HeadForDepot(TaskContext context, Entity actor, Container pack)
        {
            Entity depot = LogisticsQueries.NearestDepotWithRoom(context, actor, unreachableDepots);
            if (depot == null)
            {
                unreachableDepots.Clear();
                return Wait(context, TaskReason.NoDepotAvailable);
            }
            depotId = depot.Id;
            room = context.Logistics.Reserve(ReservationKind.Deposit, depotId, string.Empty, pack.Total, Id);
            mover.GoBeside(LogisticsQueries.FootprintOf(context, depot));
            phase = Phase.ToDepot;
            Enter(context, TaskState.Running);
            return true;
        }

        private bool Wait(TaskContext context, TaskReason reason)
        {
            retryAtTick = context.World.Tick + context.Config.ReplanIntervalTicks;
            Enter(context, TaskState.Blocked, reason);
            return false;
        }

        private void WalkToNode(TaskContext context, Entity actor)
        {
            if (!context.World.IsAlive(nodeId))
            {
                context.Logistics.Release(stock);
                phase = Phase.Decide;
                return;
            }
            switch (mover.Tick(context, actor))
            {
                case MoverStatus.Arrived:
                    work = 0f;
                    phase = Phase.Working;
                    break;
                case MoverStatus.Failed:
                    Enter(context, TaskState.Failed, mover.Reason);
                    break;
            }
        }

        private void Work(TaskContext context, Entity actor, Container pack)
        {
            Logistics goods = context.Logistics;
            context.Catalog.TryGet(actor.DefinitionId, out EntityDefinition definition);
            work += context.TickSeconds;
            while (work >= definition.GatherSecondsPerUnit && goods.IsLive(stock))
            {
                if (goods.Gather(nodeId, actor.Id, 1, stock, null) < 1) break;
                work -= definition.GatherSecondsPerUnit;
            }
            // Done here when the promised stock is all in the pack, the pack is full, or the node is gone. Decide picks what is next:
            // top up if the node still has unpromised stock, otherwise carry home.
            if (!goods.IsLive(stock) || pack.FreeCapacity < 1 || !context.World.IsAlive(nodeId))
            {
                goods.Release(stock);
                phase = Phase.Decide;
            }
        }

        private void WalkToDepot(TaskContext context, Entity actor, Container pack)
        {
            Logistics goods = context.Logistics;
            context.World.TryGet(depotId, out Entity depot);
            if (!LogisticsQueries.IsOpenStore(context, actor.Owner, depot))
            {
                goods.Release(room);
                phase = Phase.Decide;
                return;
            }
            switch (mover.Tick(context, actor))
            {
                case MoverStatus.Arrived:
                    foreach (KeyValuePair<string, int> carried in new List<KeyValuePair<string, int>>(pack.Contents))
                        goods.Transfer(actor.Id, depotId, carried.Key, carried.Value, null, room, Id);
                    goods.Release(room);
                    unreachableDepots.Clear();
                    phase = Phase.Decide;
                    // Whatever did not fit stays in the pack and Decide finds another depot or waits.
                    break;
                case MoverStatus.Failed:
                    goods.Release(room);
                    unreachableDepots.Add(depotId);
                    phase = Phase.Decide;
                    break;
            }
        }
    }
}
