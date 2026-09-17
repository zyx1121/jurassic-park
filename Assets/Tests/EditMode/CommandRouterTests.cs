using System.Collections.Generic;
using System.Linq;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class CommandRouterTests
    {
        private sealed class RecordingHandler : ICommandHandler
        {
            public readonly List<Command> Executed = new List<Command>();
            public readonly List<EntityId[]> ExecutedActors = new List<EntityId[]>();
            public readonly List<long> ExecutedAtTick = new List<long>();
            public CommandRejection Verdict = CommandRejection.None;
            public System.Action<Command> OnExecute;

            public CommandRejection Validate(World world, Command command, IReadOnlyList<Entity> livingActors) => Verdict;

            public void Execute(World world, Command command, IReadOnlyList<Entity> livingActors)
            {
                Executed.Add(command);
                ExecutedActors.Add(livingActors.Select(a => a.Id).ToArray());
                ExecutedAtTick.Add(world.Tick);
                OnExecute?.Invoke(command);
            }
        }

        private World world;
        private SeatRegistry seats;
        private CommandRouter router;
        private RecordingHandler move;
        private Entity red, redTwo, blue;
        private static readonly SeatId Red = new SeatId(1), Blue = new SeatId(2), Dinosaurs = new SeatId(8);

        [SetUp]
        public void SetUp()
        {
            world = new World(new SimConfig(10, 8, 1));
            seats = new SeatRegistry(world);
            seats.Add(Red, "Red", team: 1, SeatController.Human);
            seats.Add(Blue, "Blue", team: 1, SeatController.Computer);
            seats.Add(Dinosaurs, "Dinosaurs", team: 2, SeatController.Computer);
            router = new CommandRouter(seats, new CommandRouterConfig(rememberedResultsPerSeat: 2, maxPendingPerSeat: 3, maxCommandIdGap: 8));
            move = new RecordingHandler();
            router.Register(CommandKind.Move, move);
            world.AddSystem(router);
            red = world.Spawn(EntityKind.Unit, "survivor", Red, SimVector2.Zero);
            redTwo = world.Spawn(EntityKind.Unit, "survivor", Red, SimVector2.Zero);
            blue = world.Spawn(EntityKind.Unit, "survivor", Blue, SimVector2.Zero);
            world.Commit();
            world.DrainEvents();
        }

        private Command Move(long id, SeatId seat, params Entity[] actors) => MoveInEpoch(id, 1, seat, actors);

        private Command MoveInEpoch(long id, int epoch, SeatId seat, params Entity[] actors) =>
            new Command(id, epoch, seat, CommandKind.Move, actors.Select(a => a.Id).ToArray(), new SimVector2(5f, 5f));

        private CommandResolved[] StepAndResults()
        {
            world.Step();
            return world.DrainEvents().OfType<CommandResolved>().ToArray();
        }

        [Test]
        public void SubmitChangesNothingUntilTheNextTick()
        {
            router.Submit(Move(1, Red, red));

            Assert.That(move.Executed, Is.Empty);
            Assert.That(router.PendingCount, Is.EqualTo(1));

            CommandResolved[] results = StepAndResults();
            Assert.That(move.ExecutedAtTick, Is.EqualTo(new long[] { 1 }));
            Assert.That(results.Single().Accepted, Is.True);
            Assert.That(results.Single().Tick, Is.EqualTo(1));
        }

        [Test]
        public void CommandsRunInArrivalOrderAcrossSeats()
        {
            router.Submit(Move(1, Blue, blue));
            router.Submit(Move(1, Red, red));
            router.Submit(Move(2, Blue, blue));
            world.Step();

            Assert.That(move.Executed.Select(c => $"{c.Seat.Value}:{c.CommandId}"), Is.EqualTo(new[] { "2:1", "1:1", "2:2" }));
        }

        [Test]
        public void ASeatCannotCommandAnotherSeatsUnitEvenAnAllys()
        {
            Assert.That(seats.AreAllied(Red, Blue), Is.True);
            router.Submit(Move(1, Red, red, blue));

            Assert.That(StepAndResults().Single().Rejection, Is.EqualTo(CommandRejection.NotOwner));
            Assert.That(move.Executed, Is.Empty, "one foreign actor voids the whole command");
        }

        [Test]
        public void RejectionsAreSpecific()
        {
            router.Submit(new Command(1, 1, Red, CommandKind.Move, new EntityId[0]));
            router.Submit(new Command(2, 1, Red, CommandKind.Move, new[] { new EntityId(999) }));
            router.Submit(new Command(3, 1, Red, CommandKind.Stop, new[] { red.Id }));
            // Red is at its pending cap of 3, so the malformed ones come from Blue.
            router.Submit(new Command(1, 1, Blue, (CommandKind)77, new[] { blue.Id }));
            router.Submit(new Command(2, 1, Blue, CommandKind.Move, new[] { blue.Id }, mode: (CommandMode)999));

            Assert.That(StepAndResults().Select(r => r.Rejection), Is.EqualTo(new[]
            {
                CommandRejection.NoActors, CommandRejection.UnknownActor, CommandRejection.UnsupportedKind,
                CommandRejection.Malformed, CommandRejection.Malformed,
            }));
            Assert.That(move.Executed, Is.Empty);
        }

        [Test]
        public void TheHandlersVerdictIsReportedAndBlocksExecution()
        {
            move.Verdict = CommandRejection.InvalidTarget;
            router.Submit(Move(1, Red, red));

            Assert.That(StepAndResults().Single().Rejection, Is.EqualTo(CommandRejection.InvalidTarget));
            Assert.That(move.Executed, Is.Empty);
        }

        [Test]
        public void AnActorThatDiedBeforeTheTickDoesNotVoidTheRestOfTheSelection()
        {
            router.Submit(Move(1, Red, red, redTwo));
            world.Despawn(red.Id, "eaten");

            Assert.That(StepAndResults().Single().Accepted, Is.True);
            Assert.That(move.ExecutedActors.Single(), Is.EqualTo(new[] { redTwo.Id }), "the handler is handed the living actors only");
            Assert.That(move.Executed.Single().Actors, Does.Contain(red.Id), "the command itself still names the dead one, which is why handlers must not read it");
        }

        [Test]
        public void ACommandWhoseActorsAreAllDeadIsRejected()
        {
            router.Submit(Move(1, Red, red));
            world.Despawn(red.Id, "eaten");

            Assert.That(StepAndResults().Single().Rejection, Is.EqualTo(CommandRejection.ActorNotAlive));
        }

        [Test]
        public void AResentCommandGetsItsOriginalAnswerAndIsNotExecutedAgain()
        {
            router.Submit(Move(1, Red, red));
            world.Step();
            world.DrainEvents();

            router.Submit(Move(1, Red, red));
            CommandResolved repeat = StepAndResults().Single();

            Assert.That(repeat.Accepted, Is.True);
            Assert.That(repeat.IsRepeat, Is.True);
            Assert.That(move.Executed, Has.Count.EqualTo(1));
        }

        [Test]
        public void AResendInTheSameTickIsAlsoExecutedOnce()
        {
            router.Submit(Move(1, Red, red));
            router.Submit(Move(1, Red, red));

            CommandResolved[] results = StepAndResults();
            Assert.That(results.Select(r => r.IsRepeat), Is.EqualTo(new[] { false, true }));
            Assert.That(move.Executed, Has.Count.EqualTo(1));
        }

        [Test]
        public void ARejectedCommandIsRememberedAsRejected()
        {
            move.Verdict = CommandRejection.InvalidTarget;
            router.Submit(Move(1, Red, red));
            world.Step();
            move.Verdict = CommandRejection.None;
            world.DrainEvents();

            router.Submit(Move(1, Red, red));
            CommandResolved repeat = StepAndResults().Single();

            Assert.That(repeat.Rejection, Is.EqualTo(CommandRejection.InvalidTarget), "a resend must not get a second chance");
            Assert.That(move.Executed, Is.Empty);
        }

        [Test]
        public void AnOldIdThatIsNoLongerRememberedIsStaleNotReExecuted()
        {
            for (long id = 1; id <= 3; id++)
            {
                router.Submit(Move(id, Red, red));
                world.Step();
            }
            world.DrainEvents();

            router.Submit(Move(1, Red, red));
            CommandResolved result = StepAndResults().Single();

            Assert.That(result.Rejection, Is.EqualTo(CommandRejection.StaleCommandId));
            Assert.That(move.Executed, Has.Count.EqualTo(3));
        }

        [Test]
        public void CommandIdsAreTrackedPerSeat()
        {
            router.Submit(Move(1, Red, red));
            router.Submit(Move(1, Blue, blue));

            Assert.That(StepAndResults().Select(r => r.Accepted), Is.All.True);
            Assert.That(move.Executed, Has.Count.EqualTo(2));
        }

        [Test]
        public void ACommandSubmittedByAHandlerRunsOnTheNextTick()
        {
            move.OnExecute = c => { if (c.CommandId == 1) router.Submit(Move(2, Red, redTwo)); };
            router.Submit(Move(1, Red, red));

            world.Step();
            Assert.That(move.ExecutedAtTick, Is.EqualTo(new long[] { 1 }));
            world.Step();
            Assert.That(move.ExecutedAtTick, Is.EqualTo(new long[] { 1, 2 }));
        }

        [Test]
        public void AFloodingSeatIsCappedWithoutStarvingOthers()
        {
            for (long id = 1; id <= 3; id++) Assert.That(router.Submit(Move(id, Red, red)), Is.EqualTo(SubmitOutcome.Queued));
            Assert.That(router.Submit(Move(4, Red, red)), Is.EqualTo(SubmitOutcome.DroppedFlood));
            Assert.That(router.Submit(Move(1, Blue, blue)), Is.EqualTo(SubmitOutcome.Queued));

            Assert.That(StepAndResults(), Has.Length.EqualTo(4));
            Assert.That(router.Submit(Move(4, Red, red)), Is.EqualTo(SubmitOutcome.Queued), "the cap is on what is waiting, not a lifetime quota");
            Assert.That(StepAndResults().Single().Accepted, Is.True, "the id skipped by the drop is inside the allowed gap");
        }

        [Test]
        public void HandingASeatToTheComputerIsAnEventAndKeepsOwnership()
        {
            Assert.That(seats.SetController(Red, SeatController.Computer), Is.True);
            Assert.That(seats.SetController(Red, SeatController.Computer), Is.False);
            world.Step();

            SeatControllerChanged changed = world.DrainEvents().OfType<SeatControllerChanged>().Single();
            Assert.That(changed.Controller, Is.EqualTo(SeatController.Computer));
            Assert.That(red.Owner, Is.EqualTo(Red));

            Assert.That(changed.Epoch, Is.EqualTo(2));
            router.Submit(MoveInEpoch(1, 2, Red, red));
            Assert.That(StepAndResults().Single().Accepted, Is.True, "a computer-driven seat commands through the same door");
        }

        [Test]
        public void AlliesMayUseEachOthersThingsEnemiesMayNot()
        {
            Assert.That(seats.MayUsePropertyOf(Red, Red), Is.True);
            Assert.That(seats.MayUsePropertyOf(Red, Blue), Is.True);
            Assert.That(seats.MayUsePropertyOf(Red, Dinosaurs), Is.False);
            Assert.That(seats.MayUsePropertyOf(Red, SeatId.None), Is.True, "resource nodes and ground piles belong to no seat and are open to all");
            Assert.That(seats.MayUsePropertyOf(SeatId.None, SeatId.None), Is.False);
            Assert.That(seats.MayUsePropertyOf(new SeatId(5), SeatId.None), Is.False, "an unregistered seat may use nothing");
            Assert.That(seats.AreAllied(Red, Red), Is.False);
        }

        [Test]
        public void AReturningHumanIsNotMistakenForTheComputerThatCoveredForThem()
        {
            for (long id = 1; id <= 3; id++) router.Submit(Move(id, Red, red));
            world.Step();
            seats.SetController(Red, SeatController.Computer);
            for (long id = 1; id <= 3; id++) router.Submit(MoveInEpoch(id, 2, Red, red));
            world.Step();
            seats.SetController(Red, SeatController.Human);
            world.Step();
            world.DrainEvents();
            int executedBefore = move.Executed.Count;

            router.Submit(MoveInEpoch(1, 3, Red, red));
            CommandResolved result = StepAndResults().Single();

            Assert.That(result.Accepted, Is.True);
            Assert.That(result.IsRepeat, Is.False, "id 1 of epoch 3 is a new command, not a resend of the computer's id 1");
            Assert.That(move.Executed.Count, Is.EqualTo(executedBefore + 1));
        }

        [Test]
        public void ACommandFromThePreviousControllerIsRefused()
        {
            router.Submit(Move(1, Red, red));
            seats.SetController(Red, SeatController.Computer);

            CommandResolved result = StepAndResults().Single();

            Assert.That(result.Rejection, Is.EqualTo(CommandRejection.WrongEpoch));
            Assert.That(move.Executed, Is.Empty);
        }

        [Test]
        public void AHugeOrNonPositiveIdCannotLockTheSeatOut()
        {
            router.Submit(Move(long.MaxValue, Red, red));
            router.Submit(Move(0, Red, red));
            router.Submit(Move(-5, Red, red));
            Assert.That(StepAndResults().Select(r => r.Rejection), Is.All.EqualTo(CommandRejection.InvalidCommandId));

            router.Submit(Move(1, Red, red));
            Assert.That(StepAndResults().Single().Accepted, Is.True, "the watermark was not moved by the refused ids");
        }

        [Test]
        public void TheCommandKeepsItsOwnCopyOfTheSelection()
        {
            var selection = new List<EntityId> { red.Id };
            var command = new Command(1, 1, Red, CommandKind.Move, selection);
            router.Submit(command);
            selection[0] = blue.Id;
            selection.Add(redTwo.Id);

            Assert.That(StepAndResults().Single().Accepted, Is.True);
            Assert.That(move.ExecutedActors.Single(), Is.EqualTo(new[] { red.Id }));
        }

        [Test]
        public void AnUnknownSeatIsTurnedAwayAtTheDoorAndCannotClaimQueueSpace()
        {
            for (int i = 0; i < 50; i++)
                Assert.That(router.Submit(new Command(1, 1, new SeatId(100 + i), CommandKind.Move, new[] { red.Id })), Is.EqualTo(SubmitOutcome.DroppedUnknownSeat));

            Assert.That(router.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void TheSameActorNamedTwiceIsHandedOverOnce()
        {
            router.Submit(Move(1, Red, red, red));
            world.Step();

            Assert.That(move.ExecutedActors.Single(), Is.EqualTo(new[] { red.Id }));
        }

        [Test]
        public void AClickBurstThatOverflowsTheQueueDoesNotLockTheSeatOut()
        {
            var sender = new CommandSender(router, Red, epoch: 1);
            var outcomes = new List<SubmitOutcome>();
            for (int i = 0; i < 12; i++) outcomes.Add(sender.Send(CommandKind.Move, new[] { red.Id }));

            Assert.That(outcomes.Count(o => o == SubmitOutcome.Queued), Is.EqualTo(3));
            Assert.That(outcomes.Count(o => o == SubmitOutcome.DroppedFlood), Is.EqualTo(9));
            world.Step();
            world.DrainEvents();

            Assert.That(sender.Send(CommandKind.Move, new[] { red.Id }), Is.EqualTo(SubmitOutcome.Queued));
            CommandResolved next = StepAndResults().Single();
            Assert.That(next.CommandId, Is.EqualTo(4), "dropped commands did not consume ids");
            Assert.That(next.Accepted, Is.True);
        }

        [Test]
        public void ASenderThatRanAheadIsToldWhichIdToUseAndRecovers()
        {
            for (long id = 1; id <= 3; id++) router.Submit(Move(id, Red, red));
            world.Step();
            world.DrainEvents();

            router.Submit(Move(3 + 8 + 1, Red, red));
            CommandResolved refused = StepAndResults().Single();
            Assert.That(refused.Rejection, Is.EqualTo(CommandRejection.InvalidCommandId), "one past the allowed gap");
            Assert.That(refused.NextCommandId, Is.EqualTo(4));
            Assert.That(refused.CurrentEpoch, Is.EqualTo(1));

            router.Submit(Move(refused.NextCommandId, Red, red));
            Assert.That(StepAndResults().Single().Accepted, Is.True);
        }

        [Test]
        public void ASenderWithAStaleEpochAdoptsTheCurrentOneFromTheAnswer()
        {
            var sender = new CommandSender(router, Red, epoch: 1);
            sender.Send(CommandKind.Move, new[] { red.Id });
            world.Step();
            Assert.That(seats.BeginControllerEpoch(Red), Is.EqualTo(2));
            world.DrainEvents();

            sender.Send(CommandKind.Move, new[] { red.Id });
            CommandResolved refused = StepAndResults().Single();
            Assert.That(refused.Rejection, Is.EqualTo(CommandRejection.WrongEpoch));
            Assert.That(sender.Observe(refused), Is.True);
            Assert.That(sender.Epoch, Is.EqualTo(2));

            sender.Send(CommandKind.Move, new[] { red.Id });
            CommandResolved accepted = StepAndResults().Single();
            Assert.That(accepted.Accepted, Is.True);
            Assert.That(accepted.CommandId, Is.EqualTo(1), "ids start again in the new epoch");
            Assert.That(move.Executed, Has.Count.EqualTo(2));
        }

        [Test]
        public void TheSyncPointCountsIdsStillWaitingInTheQueueButNotAbsurdOnes()
        {
            for (long id = 1; id <= 3; id++) router.Submit(Move(id, Red, red));
            Assert.That(router.TryGetSync(Red, out int epoch, out long next), Is.True);
            Assert.That((epoch, next), Is.EqualTo((1, 4L)), "three are queued and none resolved yet");

            world.Step();
            router.Submit(Move(long.MaxValue, Red, red));
            router.TryGetSync(Red, out _, out next);
            Assert.That(next, Is.EqualTo(4), "a queued id that is going to be refused does not drag the sync point with it");
            Assert.That(router.TryGetSync(new SeatId(77), out _, out _), Is.False);
        }

        [Test]
        public void ASenderIgnoresAnAnswerAddressedToAnEarlierHolderOfItsSeat()
        {
            var sender = new CommandSender(router, Red, epoch: 3);
            Assert.That(sender.Observe(new CommandResolved(Red, 2, 9, CommandRejection.WrongEpoch, false, 3, 1)), Is.False);
            Assert.That(sender.Observe(new CommandResolved(Red, 3, 9, CommandRejection.InvalidCommandId, false, 3, 5)), Is.True);
        }
    }
}
