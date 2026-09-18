using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// The instinct of every computer-controlled unit that has perception: when idle and something hostile is within reach,
    /// attack it. Nothing more, and nothing that skips the door: it submits ordinary Attack commands through its seat's
    /// sender, so a dinosaur obeys exactly the rules a player's unit does.
    /// </summary>
    public sealed class Predators : ISimSystem
    {
        private readonly TaskContext context;
        private readonly CommandRouter router;
        private readonly Dictionary<SeatId, CommandSender> senders = new Dictionary<SeatId, CommandSender>();
        private readonly List<EntityId> single = new List<EntityId>(1);
        private readonly int scanIntervalTicks;

        /// <param name="scanIntervalTicks">Idle units look around this often. Perception is a full scan of the entities, so not every tick.</param>
        public Predators(TaskContext context, CommandRouter router, int scanIntervalTicks)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.router = router ?? throw new ArgumentNullException(nameof(router));
            if (scanIntervalTicks < 1) throw new ArgumentOutOfRangeException(nameof(scanIntervalTicks));
            this.scanIntervalTicks = scanIntervalTicks;
        }

        public void Tick(World world)
        {
            if (context.Seats == null || world.Tick % scanIntervalTicks != 0) return;
            IReadOnlyList<Entity> entities = world.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity hunter = entities[i];
                if (!hunter.IsAlive || hunter.Kind != EntityKind.Unit || hunter.Owner.IsNone) continue;
                if (!context.Catalog.TryGet(hunter.DefinitionId, out EntityDefinition definition) || !definition.CanAttack || definition.PerceptionRadius <= 0f) continue;
                if (!context.Seats.TryGet(hunter.Owner, out Seat seat) || seat.Controller != SeatController.Computer) continue;
                // Busy hunters keep hunting; one whose hunt is blocked (an enclosed target) may notice easier prey walking past.
                SimTask current = context.Tasks?.CurrentOf(hunter.Id);
                if (current != null && current.State != TaskState.Blocked) continue;
                Entity prey = Nearest(entities, hunter, definition.PerceptionRadius);
                if (prey == null || (current is AttackTask blockedHunt && blockedHunt.Target == prey.Id)) continue;
                single.Clear();
                single.Add(hunter.Id);
                SenderFor(seat).Send(CommandKind.Attack, single, prey.Position, prey.Id);
            }
        }

        private Entity Nearest(IReadOnlyList<Entity> entities, Entity hunter, float radius)
        {
            Entity best = null;
            float bestDistance = radius;
            for (int i = 0; i < entities.Count; i++)
            {
                Entity candidate = entities[i];
                if (!candidate.IsAlive || candidate.Owner == hunter.Owner || candidate.Id == hunter.Id) continue;
                if (candidate.Kind != EntityKind.Unit && candidate.Kind != EntityKind.Building) continue;
                if (candidate.Owner.IsNone || context.Seats.AreAllied(hunter.Owner, candidate.Owner)) continue;
                if (context.Vitals == null || !context.Vitals.TryGet(candidate.Id, out _, out _)) continue;
                float distance = SimVector2.Distance(hunter.Position, candidate.Position);
                if (distance <= bestDistance && (best == null || distance < bestDistance))
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
