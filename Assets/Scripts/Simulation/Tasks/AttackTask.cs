using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Get beside the target and hit it until it is gone. An attacker that can breach plans through destructible blockers when
    /// that is the only or the cheaper way, and then deals with the first blocker on the route before going on: it never picks a
    /// wall because the wall is weak, only because the wall is in the way. When the route or the target changes, it plans again.
    /// </summary>
    public sealed class AttackTask : SimTask
    {
        private enum Phase { Plan, Approach, Fighting }

        private readonly EntityId targetId;
        private readonly Mover mover = new Mover();
        private Phase phase = Phase.Plan;
        private EntityId victimId;
        private float cooldown;
        private long retryAtTick;
        private SimVector2 targetWasAt;

        public AttackTask(EntityId target) => targetId = target;

        public override string Kind => "attack";
        public EntityId Target => targetId;

        /// <summary>What is being hit right now: the target, or the blocker in the way of it.</summary>
        public EntityId Victim => victimId;

        protected internal override void Tick(TaskContext context, Entity actor)
        {
            if (context.Vitals == null || !context.Catalog.TryGet(actor.DefinitionId, out EntityDefinition definition) || !definition.CanAttack)
            {
                Enter(context, TaskState.Failed, TaskReason.ActorCannotAttack);
                return;
            }
            if (!context.World.IsAlive(targetId))
            {
                Enter(context, TaskState.Completed, TaskReason.TargetDestroyed);
                return;
            }
            cooldown = System.Math.Max(0f, cooldown - context.TickSeconds);
            switch (phase)
            {
                case Phase.Plan: Plan(context, actor, definition); break;
                case Phase.Approach: Approach(context, actor); break;
                case Phase.Fighting: Fight(context, actor, definition); break;
            }
        }

        private void Plan(TaskContext context, Entity actor, EntityDefinition definition)
        {
            if (context.World.Tick < retryAtTick) return;
            context.World.TryGet(targetId, out Entity target);
            mover.GoBeside(LogisticsQueries.FootprintOf(context, target), breach: definition.CanBreach);
            victimId = targetId;
            targetWasAt = target.Position;
            phase = Phase.Approach;
            // Running only once there is a route: a hunter that keeps finding none stays Blocked, which is what it is.
            if (State == TaskState.Planning) Enter(context, TaskState.Running);
        }

        private void Approach(TaskContext context, Entity actor)
        {
            switch (mover.Tick(context, actor))
            {
                case MoverStatus.Moving:
                    Enter(context, TaskState.Running);
                    // The route was just planned or replanned: the first blocker on it is the victim, or the target when it is clear.
                    FollowRoute(context);
                    if (BesideVictim(context, actor)) phase = Phase.Fighting;
                    break;
                case MoverStatus.Arrived:
                    FollowRoute(context);
                    phase = Phase.Fighting;
                    break;
                case MoverStatus.Failed:
                    if (mover.Reason == TaskReason.NoRoute)
                    {
                        // Nothing leads there, not even through a wall. Look again later; the world changes.
                        retryAtTick = context.World.Tick + context.Config.ReplanIntervalTicks;
                        phase = Phase.Plan;
                        Enter(context, TaskState.Blocked, TaskReason.NoRoute);
                    }
                    else Enter(context, TaskState.Failed, mover.Reason);
                    break;
            }
        }

        private void FollowRoute(TaskContext context)
        {
            EntityId next = mover.Breached.Count > 0 && context.World.IsAlive(mover.Breached[0]) ? mover.Breached[0] : targetId;
            if (next == victimId) return;
            victimId = next;
            Enter(context, TaskState.Running, next == targetId ? TaskReason.None : TaskReason.Breaching);
        }

        private bool BesideVictim(TaskContext context, Entity actor)
        {
            if (!context.World.TryGet(victimId, out Entity victim) || !victim.IsAlive) return false;
            return LogisticsQueries.IsBeside(context, actor, victim);
        }

        private void Fight(TaskContext context, Entity actor, EntityDefinition definition)
        {
            if (!context.World.IsAlive(victimId) || !BesideVictim(context, actor))
            {
                // The wall fell, or the target moved: plan the way to the target again.
                phase = Phase.Plan;
                return;
            }
            // Breaking a wall to reach something that has since walked away is wasted work: check the way again.
            if (victimId != targetId && context.World.TryGet(targetId, out Entity target) && SimVector2.Distance(target.Position, targetWasAt) > context.Map.CellSize)
            {
                phase = Phase.Plan;
                return;
            }
            if (cooldown > 0f) return;
            int dealt = context.Vitals.Damage(victimId, definition.AttackDamage, "attacked");
            cooldown = definition.AttackSeconds;
            if (dealt > 0)
            {
                context.World.Raise(new Attacked(actor.Id, victimId, dealt));
                return;
            }
            // Something that cannot be hurt is in the way. Not a route, then: wait and look again rather than gnaw forever.
            retryAtTick = context.World.Tick + context.Config.ReplanIntervalTicks;
            phase = Phase.Plan;
            Enter(context, TaskState.Blocked, TaskReason.NoRoute);
        }
    }
}
