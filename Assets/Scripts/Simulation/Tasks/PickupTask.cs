using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>Take goods from a pile or an open depot, as much as the pack holds. The goods are reserved before walking, so a second hauler is told there is nothing left instead of finding out on arrival.</summary>
    public sealed class PickupTask : SimTask
    {
        private readonly EntityId sourceId;
        private readonly Mover mover = new Mover();
        private readonly List<Reservation> claims = new List<Reservation>();
        private bool started;

        public PickupTask(EntityId source) => sourceId = source;

        public override string Kind => "pickup";

        protected internal override void Release(TaskContext context)
        {
            for (int i = 0; i < claims.Count; i++) context.Logistics?.Release(claims[i]);
        }

        protected internal override void Tick(TaskContext context, Entity actor)
        {
            Logistics goods = context.Logistics;
            if (!LogisticsQueries.CanCarry(context, actor))
            {
                Enter(context, TaskState.Failed, TaskReason.ActorCannotCarry);
                return;
            }
            context.World.TryGet(sourceId, out Entity source);
            if (!LogisticsQueries.IsOpenStore(context, actor.Owner, source))
            {
                Enter(context, TaskState.Failed, source == null || !source.IsAlive ? TaskReason.TargetGone : TaskReason.NotAllowed);
                return;
            }
            goods.TryGetContainer(actor.Id, out Container pack);
            goods.TryGetContainer(sourceId, out Container store);
            if (!started)
            {
                started = true;
                int wanted = pack.FreeCapacity;
                if (wanted < 1)
                {
                    Enter(context, TaskState.Failed, TaskReason.PackFull);
                    return;
                }
                foreach (KeyValuePair<string, int> stored in new List<KeyValuePair<string, int>>(store.Contents))
                {
                    if (wanted < 1) break;
                    Reservation claim = goods.Reserve(ReservationKind.Withdrawal, sourceId, stored.Key, wanted, Id);
                    if (claim == null) continue;
                    claims.Add(claim);
                    wanted -= claim.Amount;
                }
                if (claims.Count == 0)
                {
                    Enter(context, TaskState.Failed, TaskReason.SourceEmpty);
                    return;
                }
                mover.GoBeside(LogisticsQueries.FootprintOf(context, source));
                Enter(context, TaskState.Running);
            }
            for (int i = 0; i < claims.Count; i++) goods.Renew(claims[i]);

            switch (mover.Tick(context, actor))
            {
                case MoverStatus.Arrived:
                    int taken = 0;
                    for (int i = 0; i < claims.Count; i++)
                        if (goods.IsLive(claims[i])) taken += goods.Transfer(sourceId, actor.Id, claims[i].Resource, claims[i].Amount, claims[i], null, Id);
                    Enter(context, taken > 0 ? TaskState.Completed : TaskState.Failed, taken > 0 ? TaskReason.PickedUp : TaskReason.SourceEmpty);
                    break;
                case MoverStatus.Failed:
                    Enter(context, TaskState.Failed, mover.Reason);
                    break;
            }
        }
    }
}
