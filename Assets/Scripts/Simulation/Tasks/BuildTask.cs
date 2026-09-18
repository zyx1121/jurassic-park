using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Bring a site what it lacks, then work on it until it is built. Materials come from the nearest open store that has them,
    /// reserved before walking; the site's own container is reserved for the delivery. Several builders may share a site: each
    /// fetches what is still missing and the reservations keep them from fetching the same log twice.
    /// </summary>
    public sealed class BuildTask : SimTask
    {
        private enum Phase { Decide, ToSource, ToSite, Working }

        private readonly EntityId siteId;
        private readonly Mover mover = new Mover();
        private readonly HashSet<EntityId> unreachable = new HashSet<EntityId>();
        private Phase phase = Phase.Decide;
        private Reservation claim, room;
        private EntityId sourceId;
        private string resource;
        private long retryAtTick;

        public BuildTask(EntityId site) => siteId = site;

        public override string Kind => "build";
        public EntityId Site => siteId;

        protected internal override void Release(TaskContext context)
        {
            context.Logistics?.Release(claim);
            context.Logistics?.Release(room);
        }

        protected internal override void Tick(TaskContext context, Entity actor)
        {
            if (context.Structures == null || context.Logistics == null || !LogisticsQueries.CanCarry(context, actor))
            {
                Enter(context, TaskState.Failed, TaskReason.ActorCannotCarry);
                return;
            }
            if (!context.World.IsAlive(siteId) || !context.Structures.TryGetSite(siteId, out BuildSite site))
            {
                // Built by someone else, or demolished: either way this task is done with it.
                Enter(context, context.World.IsAlive(siteId) ? TaskState.Completed : TaskState.Failed, context.World.IsAlive(siteId) ? TaskReason.Built : TaskReason.SiteGone);
                return;
            }
            context.Logistics.Renew(claim);
            context.Logistics.Renew(room);
            context.Logistics.TryGetContainer(actor.Id, out Container pack);

            if (phase == Phase.Decide && !Decide(context, actor, site, pack)) return;
            switch (phase)
            {
                case Phase.ToSource: WalkToSource(context, actor, pack); break;
                case Phase.ToSite: WalkToSite(context, actor, site, pack); break;
                case Phase.Working: WorkOn(context, actor, site); break;
            }
        }

        private bool Decide(TaskContext context, Entity actor, BuildSite site, Container pack)
        {
            if (context.World.Tick < retryAtTick) return false;
            Logistics goods = context.Logistics;
            context.World.TryGet(siteId, out Entity siteEntity);

            // Carrying something the site still needs: bring it. Otherwise fetch the next missing material. Nothing missing: work.
            foreach (KeyValuePair<string, int> carried in pack.Contents)
                if (site.Missing(goods, carried.Key) > 0) return HeadForSite(context, siteEntity, carried.Key, carried.Value);

            // Room on the site is reserved per resource before fetching, so a second builder sees what the first is already
            // bringing of each material and does not fetch it too. Any material somebody can be found for is fetched first;
            // waiting is only for when nothing is fetchable at all.
            bool anythingMissing = false;
            foreach (KeyValuePair<string, int> need in site.Definition.BuildCost)
            {
                int missing = site.Missing(goods, need.Key) - goods.ReservedRoomIn(siteId, need.Key);
                if (missing < 1) continue;
                anythingMissing = true;
                if (pack.FreeCapacity < 1) return Wait(context, TaskReason.PackFull);
                Entity source = NearestSourceOf(context, actor, need.Key);
                if (source == null) continue;
                int fetch = System.Math.Min(missing, pack.FreeCapacity);
                room = goods.Reserve(ReservationKind.Deposit, siteId, need.Key, fetch, Id);
                if (room == null) continue;
                claim = goods.Reserve(ReservationKind.Withdrawal, source.Id, need.Key, room.Amount, Id);
                if (claim == null)
                {
                    goods.Release(room);
                    continue;
                }
                sourceId = source.Id;
                resource = need.Key;
                mover.GoBeside(LogisticsQueries.FootprintOf(context, source));
                phase = Phase.ToSource;
                Enter(context, TaskState.Running);
                return true;
            }
            if (anythingMissing) return Wait(context, TaskReason.NoMaterialsAvailable);

            mover.GoBeside(site.Footprint);
            phase = Phase.Working;
            Enter(context, TaskState.Running);
            return true;
        }

        private bool HeadForSite(TaskContext context, Entity siteEntity, string carriedResource, int amount)
        {
            resource = carriedResource;
            // Goods fetched under this task already hold their room; goods carried in from before do not.
            if (!context.Logistics.IsLive(room)) room = context.Logistics.Reserve(ReservationKind.Deposit, siteId, carriedResource, amount, Id);
            mover.GoBeside(LogisticsQueries.FootprintOf(context, siteEntity));
            phase = Phase.ToSite;
            Enter(context, TaskState.Running);
            return true;
        }

        private bool Wait(TaskContext context, TaskReason reason)
        {
            // Whatever could not be reached may be reachable by the time we look again: a gate opens, a wall falls.
            unreachable.Clear();
            retryAtTick = context.World.Tick + context.Config.ReplanIntervalTicks;
            Enter(context, TaskState.Blocked, reason);
            return false;
        }

        /// <summary>The nearest store the builder may use that offers the resource, by straight line, ties to the older entity.</summary>
        private Entity NearestSourceOf(TaskContext context, Entity actor, string wanted)
        {
            Entity best = null;
            float bestDistance = float.MaxValue;
            IReadOnlyList<Entity> entities = context.World.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity candidate = entities[i];
                if (candidate.Id == siteId || unreachable.Contains(candidate.Id) || !LogisticsQueries.IsOpenStore(context, actor.Owner, candidate)) continue;
                if (context.Logistics.AvailableIn(candidate.Id, wanted, Id) < 1) continue;
                float distance = SimVector2.Distance(actor.Position, candidate.Position);
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private void WalkToSource(TaskContext context, Entity actor, Container pack)
        {
            Logistics goods = context.Logistics;
            if (!goods.IsLive(claim) || !context.World.IsAlive(sourceId))
            {
                goods.Release(claim);
                goods.Release(room);
                phase = Phase.Decide;
                return;
            }
            switch (mover.Tick(context, actor))
            {
                case MoverStatus.Arrived:
                    goods.Transfer(sourceId, actor.Id, resource, claim.Amount, claim, null, Id);
                    goods.Release(claim);
                    phase = Phase.Decide;
                    break;
                case MoverStatus.Failed:
                    goods.Release(claim);
                    goods.Release(room);
                    unreachable.Add(sourceId);
                    phase = Phase.Decide;
                    break;
            }
        }

        private void WalkToSite(TaskContext context, Entity actor, BuildSite site, Container pack)
        {
            switch (mover.Tick(context, actor))
            {
                case MoverStatus.Arrived:
                    context.Logistics.Transfer(actor.Id, siteId, resource, pack.AmountOf(resource), null, room, Id);
                    context.Logistics.Release(room);
                    phase = Phase.Decide;
                    break;
                case MoverStatus.Failed:
                    context.Logistics.Release(room);
                    Enter(context, TaskState.Failed, mover.Reason);
                    break;
            }
        }

        private void WorkOn(TaskContext context, Entity actor, BuildSite site)
        {
            switch (mover.Tick(context, actor))
            {
                case MoverStatus.Arrived:
                    if (!context.Structures.Work(siteId, context.TickSeconds))
                    {
                        // Materials went missing (a hauler died on the way): fetch again.
                        phase = Phase.Decide;
                    }
                    break;
                case MoverStatus.Failed:
                    Enter(context, TaskState.Failed, mover.Reason);
                    break;
            }
        }
    }
}
