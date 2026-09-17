using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>Carry everything in the pack to one chosen store. Room is reserved before walking, so arriving is not a gamble.</summary>
    public sealed class DeliverTask : SimTask
    {
        private readonly EntityId targetId;
        private readonly Mover mover = new Mover();
        private Reservation room;
        private bool started;

        public DeliverTask(EntityId target) => targetId = target;

        public override string Kind => "deliver";

        protected internal override void Release(TaskContext context) => context.Logistics?.Release(room);

        protected internal override void Tick(TaskContext context, Entity actor)
        {
            Logistics goods = context.Logistics;
            if (!LogisticsQueries.CanCarry(context, actor))
            {
                Enter(context, TaskState.Failed, TaskReason.ActorCannotCarry);
                return;
            }
            context.World.TryGet(targetId, out Entity target);
            if (!LogisticsQueries.IsOpenStore(context, actor.Owner, target))
            {
                Enter(context, TaskState.Failed, target == null || !target.IsAlive ? TaskReason.TargetGone : TaskReason.NotAllowed);
                return;
            }
            goods.TryGetContainer(actor.Id, out Container pack);
            if (pack.Total < 1)
            {
                Enter(context, TaskState.Failed, TaskReason.NothingToCarry);
                return;
            }
            if (!started)
            {
                started = true;
                room = goods.Reserve(ReservationKind.Deposit, targetId, string.Empty, pack.Total, Id);
                if (room == null)
                {
                    Enter(context, TaskState.Failed, TaskReason.NoDepotAvailable);
                    return;
                }
                mover.GoBeside(LogisticsQueries.FootprintOf(context, target));
                Enter(context, TaskState.Running);
            }
            goods.Renew(room);

            switch (mover.Tick(context, actor))
            {
                case MoverStatus.Arrived:
                    foreach (KeyValuePair<string, int> carried in new List<KeyValuePair<string, int>>(pack.Contents))
                        goods.Transfer(actor.Id, targetId, carried.Key, carried.Value, null, room, Id);
                    // What did not fit stays in the pack: goods never vanish because a store was small.
                    Enter(context, pack.Total == 0 ? TaskState.Completed : TaskState.Failed, pack.Total == 0 ? TaskReason.Delivered : TaskReason.NoDepotAvailable);
                    break;
                case MoverStatus.Failed:
                    Enter(context, TaskState.Failed, mover.Reason);
                    break;
            }
        }
    }
}
