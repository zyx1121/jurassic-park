using System.Collections.Generic;
using System.Linq;
using JurassicPark.Net;
using JurassicPark.Simulation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Tests.EditMode
{
    /// <summary>The host's trust boundary, driven with real message bytes and connection ids and no socket.</summary>
    public sealed class HostProtocolTests
    {
        private sealed class Recorder : ICommandHandler
        {
            public readonly List<(SeatId seat, long id)> Executed = new List<(SeatId, long)>();
            public CommandRejection Validate(World world, Command command, IReadOnlyList<Entity> livingActors) => CommandRejection.None;
            public void Execute(World world, Command command, IReadOnlyList<Entity> livingActors) => Executed.Add((command.Seat, command.CommandId));
        }

        private const ulong Alice = 11, Bob = 22, Stranger = 99;
        private static readonly SeatId Red = new SeatId(1), Blue = new SeatId(2);

        private World world;
        private SeatRegistry seats;
        private CommandRouter router;
        private Recorder recorder;
        private HostProtocol host;
        private Entity blueUnit, redUnit;
        private readonly List<(ulong client, CommandResolved answer)> sent = new List<(ulong, CommandResolved)>();
        private readonly List<string> warnings = new List<string>();
        private double now;

        [SetUp]
        public void SetUp()
        {
            sent.Clear();
            warnings.Clear();
            now = 0;
            world = new World(new SimConfig(10, 8, 1));
            seats = new SeatRegistry(world);
            seats.Add(Red, "Red", 1, SeatController.Human);
            seats.Add(Blue, "Blue", 1, SeatController.Computer);
            router = new CommandRouter(seats, new CommandRouterConfig(rememberedResultsPerSeat: 64, maxPendingPerSeat: 32, maxCommandIdGap: 64));
            recorder = new Recorder();
            router.Register(CommandKind.Move, recorder);
            world.AddSystem(router);
            redUnit = world.Spawn(EntityKind.Unit, "survivor", Red, SimVector2.Zero);
            blueUnit = world.Spawn(EntityKind.Unit, "survivor", Blue, SimVector2.Zero);
            world.Commit();
            world.DrainEvents();
            var binder = new SeatBinder(seats, new[] { Red, Blue }, hostSeat: Red);
            host = new HostProtocol(router, binder, (client, answer) => sent.Add((client, answer)), warnings.Add, commandsPerSecond: 1000, burst: 1000);
        }

        /// <summary>Sends what a client's CommandSender would put on the wire, through the real writer and reader.</summary>
        private bool Receive(ulong client, long id, int epoch, Entity actor, SeatId claimedSeat = default)
        {
            var command = new Command(id, epoch, claimedSeat, CommandKind.Move, new[] { actor.Id }, new SimVector2(3f, 4f));
            using FastBufferWriter writer = NetMessages.WriteCommand(command);
            var reader = new FastBufferReader(writer, Allocator.Temp);
            bool submitted = host.OnCommand(client, ref reader, now);
            reader.Dispose();
            return submitted;
        }

        private void Tick()
        {
            world.Step();
            host.RouteAnswers(world.DrainEvents());
        }

        private int EpochOf(SeatId seat) { seats.TryGet(seat, out Seat s); return s.ControllerEpoch; }

        [Test]
        public void AConnectionWithoutASeatIsIgnoredEntirely()
        {
            Assert.That(Receive(Stranger, 1, 1, blueUnit), Is.False);
            Tick();

            Assert.That(recorder.Executed, Is.Empty);
            Assert.That(sent, Is.Empty, "not even an answer: an unbound connection costs the host one dictionary lookup");
        }

        [Test]
        public void ACommandRunsUnderTheBoundSeatAndCannotTouchAnotherSeatsUnits()
        {
            host.Binder.Bind(Alice);
            int epoch = EpochOf(Blue);

            Receive(Alice, 1, epoch, blueUnit, claimedSeat: Red);
            Receive(Alice, 2, epoch, redUnit, claimedSeat: Red);
            Tick();

            Assert.That(recorder.Executed, Is.EqualTo(new[] { (Blue, 1L) }));
            Assert.That(sent.Select(s => (s.client, s.answer.Seat, s.answer.Rejection)), Is.EqualTo(new[]
            {
                (Alice, Blue, CommandRejection.None), (Alice, Blue, CommandRejection.NotOwner),
            }));
        }

        [Test]
        public void AFloodIsToldWhereToContinueAndTheSendersNextOrdersStillRun()
        {
            host.Binder.Bind(Alice);
            int epoch = EpochOf(Blue);
            var sender = new CommandSender(c => SubmitOutcome.Queued, Blue, epoch);
            long wireId = 0;

            // Forty orders inside one tick: 32 fit the queue, 8 are dropped at the door.
            for (int i = 0; i < 40; i++) Receive(Alice, ++wireId, epoch, blueUnit);
            CommandResolved[] drops = sent.Select(s => s.answer).ToArray();
            Assert.That(drops, Has.Length.EqualTo(8));
            Assert.That(drops.Select(d => d.Rejection), Is.All.EqualTo(CommandRejection.DroppedFlood));
            Assert.That(drops.Select(d => d.NextCommandId), Is.All.EqualTo(33), "one past the ids still waiting in the queue, not one past the last resolved id");

            // The client applies the answers in order, as a sequenced channel delivers them.
            sender = new CommandSender(c => { Receive(Alice, c.CommandId, c.Epoch, blueUnit); return SubmitOutcome.Queued; }, Blue, epoch, nextCommandId: 41);
            foreach (CommandResolved drop in drops) sender.Observe(drop);
            sent.Clear();
            Tick();
            Assert.That(recorder.Executed, Has.Count.EqualTo(32));

            sender.Send(CommandKind.Move, new[] { blueUnit.Id });
            sent.Clear();
            Tick();

            Assert.That(recorder.Executed, Has.Count.EqualTo(33), "the order after the flood actually ran");
            Assert.That(sent.Single().answer.IsRepeat, Is.False);
            Assert.That(sent.Single().answer.CommandId, Is.EqualTo(33));
        }

        [Test]
        public void ANewcomerToASeatNeverReceivesThePreviousPlayersAnswers()
        {
            host.Binder.Bind(Alice);
            int alicesEpoch = EpochOf(Blue);
            Receive(Alice, 1, alicesEpoch, blueUnit);

            // Alice drops and Bob takes the same seat before the tick that resolves her command.
            host.OnClientLeft(Alice);
            Assert.That(host.Binder.Bind(Bob), Is.EqualTo(Blue));
            Tick();

            Assert.That(sent, Is.Empty, "her command is refused as WrongEpoch, and that answer is nobody's business but hers");
            Assert.That(recorder.Executed, Is.Empty);

            Receive(Bob, 1, EpochOf(Blue), blueUnit);
            Tick();
            Assert.That(sent.Single().client, Is.EqualTo(Bob));
            Assert.That(sent.Single().answer.Accepted, Is.True);
        }

        [Test]
        public void TheHostsOwnAnswersAreNotSentToAnyone()
        {
            host.Binder.Bind(Alice);
            router.Submit(new Command(1, EpochOf(Red), Red, CommandKind.Move, new[] { redUnit.Id }));
            Tick();

            Assert.That(recorder.Executed, Is.EqualTo(new[] { (Red, 1L) }));
            Assert.That(sent, Is.Empty);
        }

        [Test]
        public void MessagesBeyondTheRateAreDroppedUnreadAndUnansweredAndTheBudgetRefills()
        {
            var binder = new SeatBinder(seats, new[] { Red, Blue }, hostSeat: Red);
            host = new HostProtocol(router, binder, (client, answer) => sent.Add((client, answer)), warnings.Add, commandsPerSecond: 10, burst: 5);
            binder.Bind(Alice);
            int epoch = EpochOf(Blue);

            int submitted = 0;
            for (int i = 1; i <= 50; i++) if (Receive(Alice, i, epoch, blueUnit)) submitted++;
            Assert.That(submitted, Is.EqualTo(5), "the burst allowance");
            Assert.That(sent, Is.Empty);
            Assert.That(warnings, Has.Count.EqualTo(1), "warned once, not once per dropped message");

            now += 1.0;
            submitted = 0;
            for (int i = 6; i <= 50; i++) if (Receive(Alice, i, epoch, blueUnit)) submitted++;
            Assert.That(submitted, Is.EqualTo(5), "a second at ten a second, capped at the burst of five");
        }

        [Test]
        public void GarbageFromABoundConnectionIsRefusedWithoutTouchingTheRouter()
        {
            host.Binder.Bind(Alice);
            using var writer = new FastBufferWriter(4, Allocator.Temp);
            writer.WriteValueSafe(12345);
            var reader = new FastBufferReader(writer, Allocator.Temp);

            Assert.That(host.OnCommand(Alice, ref reader, now), Is.False);
            reader.Dispose();
            Assert.That(router.PendingCount, Is.EqualTo(0));
            Assert.That(warnings.Single(), Does.Contain("unreadable"));
        }
    }
}
