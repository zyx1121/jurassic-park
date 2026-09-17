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
            public readonly List<long> ExecutedAtTick = new List<long>();
            public CommandRejection Verdict = CommandRejection.None;
            public System.Action<Command> OnExecute;

            public CommandRejection Validate(World world, Command command) => Verdict;

            public void Execute(World world, Command command)
            {
                Executed.Add(command);
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
            router = new CommandRouter(seats, new CommandRouterConfig(rememberedResultsPerSeat: 2, maxPendingPerSeat: 3));
            move = new RecordingHandler();
            router.Register(CommandKind.Move, move);
            world.AddSystem(router);
            red = world.Spawn(EntityKind.Unit, "survivor", Red, SimVector2.Zero);
            redTwo = world.Spawn(EntityKind.Unit, "survivor", Red, SimVector2.Zero);
            blue = world.Spawn(EntityKind.Unit, "survivor", Blue, SimVector2.Zero);
            world.Commit();
            world.DrainEvents();
        }

        private Command Move(long id, SeatId seat, params Entity[] actors) =>
            new Command(id, seat, CommandKind.Move, actors.Select(a => a.Id).ToArray(), new SimVector2(5f, 5f));

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
            router.Submit(new Command(1, new SeatId(5), CommandKind.Move, new[] { red.Id }));
            router.Submit(new Command(1, Red, CommandKind.Move, new EntityId[0]));
            router.Submit(new Command(2, Red, CommandKind.Move, new[] { new EntityId(999) }));
            router.Submit(new Command(3, Red, CommandKind.Stop, new[] { red.Id }));

            Assert.That(StepAndResults().Select(r => r.Rejection), Is.EqualTo(new[]
            {
                CommandRejection.UnknownSeat, CommandRejection.NoActors, CommandRejection.UnknownActor, CommandRejection.UnsupportedKind,
            }));
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
            Assert.That(move.Executed, Has.Count.EqualTo(1));
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
            for (long id = 1; id <= 3; id++) Assert.That(router.Submit(Move(id, Red, red)), Is.True);
            Assert.That(router.Submit(Move(4, Red, red)), Is.False);
            Assert.That(router.Submit(Move(1, Blue, blue)), Is.True);

            Assert.That(StepAndResults(), Has.Length.EqualTo(4));
            Assert.That(router.Submit(Move(4, Red, red)), Is.True, "the cap is on what is waiting, not a lifetime quota");
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

            router.Submit(Move(1, Red, red));
            Assert.That(StepAndResults().Single().Accepted, Is.True, "a computer-driven seat commands through the same door");
        }

        [Test]
        public void AlliesMayUseEachOthersThingsEnemiesMayNot()
        {
            Assert.That(seats.MayUse(Red, Red), Is.True);
            Assert.That(seats.MayUse(Red, Blue), Is.True);
            Assert.That(seats.MayUse(Red, Dinosaurs), Is.False);
            Assert.That(seats.MayUse(SeatId.None, SeatId.None), Is.False);
            Assert.That(seats.AreAllied(Red, Red), Is.False);
        }
    }
}
