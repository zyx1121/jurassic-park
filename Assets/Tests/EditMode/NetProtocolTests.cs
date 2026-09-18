using System.Collections.Generic;
using System.Linq;
using JurassicPark.Net;
using JurassicPark.Presentation;
using JurassicPark.Simulation;
using NUnit.Framework;
using Unity.Collections;
using Unity.Netcode;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Tests.EditMode
{
    /// <summary>The wire format, the seat binding rule and the client's half of the command protocol, all without a socket.</summary>
    public sealed class NetProtocolTests
    {
        private static FastBufferReader ReaderOf(FastBufferWriter writer) => new FastBufferReader(writer, Allocator.Temp);

        [Test]
        public void ACommandSurvivesTheWireAndTakesItsSeatFromTheBindingNotThePayload()
        {
            var sent = new Command(7, 3, new SeatId(99), CommandKind.Gather, new[] { new EntityId(5), new EntityId(6) },
                new SimVector2(12.5f, -3f), new EntityId(40), CommandMode.Queue, argument: 7);
            using FastBufferWriter writer = NetMessages.WriteCommand(sent);
            FastBufferReader reader = ReaderOf(writer);

            Assert.That(NetMessages.TryReadCommand(ref reader, new SeatId(2), out Command got), Is.True);
            reader.Dispose();

            Assert.That(got.Seat, Is.EqualTo(new SeatId(2)), "whatever seat the sender claimed is not even on the wire");
            Assert.That((got.CommandId, got.Epoch, got.Kind, got.Mode), Is.EqualTo((7L, 3, CommandKind.Gather, CommandMode.Queue)));
            Assert.That(got.Actors, Is.EqualTo(sent.Actors));
            Assert.That(got.TargetPosition, Is.EqualTo(sent.TargetPosition));
            Assert.That(got.TargetEntity, Is.EqualTo(sent.TargetEntity));
            Assert.That(got.Argument, Is.EqualTo(7));
        }

        [Test]
        public void ATruncatedOrOversizedOrNonFiniteCommandIsRefusedNotThrown()
        {
            using (var tiny = new FastBufferWriter(8, Allocator.Temp))
            {
                tiny.WriteValueSafe(1L);
                FastBufferReader reader = ReaderOf(tiny);
                Assert.That(NetMessages.TryReadCommand(ref reader, new SeatId(1), out _), Is.False);
                reader.Dispose();
            }
            var actors = Enumerable.Range(1, NetMessages.MaxActorsPerCommand + 1).Select(i => new EntityId(i)).ToArray();
            using (FastBufferWriter big = NetMessages.WriteCommand(new Command(1, 1, SeatId.None, CommandKind.Move, actors)))
            {
                FastBufferReader reader = ReaderOf(big);
                Assert.That(NetMessages.TryReadCommand(ref reader, new SeatId(1), out _), Is.False, "a selection larger than the cap");
                reader.Dispose();
            }
            using (FastBufferWriter nan = NetMessages.WriteCommand(new Command(1, 1, SeatId.None, CommandKind.Move, new[] { new EntityId(1) }, new SimVector2(float.NaN, 0f))))
            {
                FastBufferReader reader = ReaderOf(nan);
                Assert.That(NetMessages.TryReadCommand(ref reader, new SeatId(1), out _), Is.False, "a NaN target would poison pathfinding");
                reader.Dispose();
            }
        }

        [Test]
        public void ASnapshotSurvivesTheWire()
        {
            var sent = new List<EntitySnapshot>
            {
                new EntitySnapshot { Id = new EntityId(16), DefinitionIndex = 0, Kind = EntityKind.Unit, Owner = new SeatId(1), Position = new SimVector2(31f, 29.5f),
                    PackTotal = 7, PackCapacity = 10, Task = TaskKindCode.Gather, TaskState = TaskState.Blocked, TaskReason = TaskReason.SourceEmpty, BuildProgress = 255, HealthFraction = 200 },
                new EntitySnapshot { Id = new EntityId(30), DefinitionIndex = 4, Kind = EntityKind.Building, Owner = new SeatId(1), Position = new SimVector2(24f, 30f), IsSite = true, BuildProgress = 90, HealthFraction = 255, GateOpen = true },
                new EntitySnapshot { Id = new EntityId(2), DefinitionIndex = 2, Kind = EntityKind.ResourceNode, Owner = SeatId.None, Position = new SimVector2(61f, 25f), NodeRemaining = 33 },
            };
            using FastBufferWriter writer = NetMessages.WriteSnapshot(512, sent);
            FastBufferReader reader = ReaderOf(writer);
            var got = new List<EntitySnapshot>();

            Assert.That(NetMessages.TryReadSnapshot(ref reader, out long tick, got), Is.True);
            reader.Dispose();

            Assert.That(tick, Is.EqualTo(512));
            Assert.That(got, Is.EqualTo(sent));
        }

        [Test]
        public void ASnapshotThatClaimsMoreEntitiesThanItCarriesIsRefused()
        {
            using var writer = new FastBufferWriter(16, Allocator.Temp);
            writer.WriteValueSafe(1L);
            writer.WriteValueSafe(1000);
            FastBufferReader reader = ReaderOf(writer);
            Assert.That(NetMessages.TryReadSnapshot(ref reader, out _, new List<EntitySnapshot>()), Is.False);
            reader.Dispose();
        }

        [Test]
        public void AnswersWelcomesAndSeatTablesSurviveTheWire()
        {
            var answer = new CommandResolved(new SeatId(2), 4, 19, CommandRejection.QueueFull, true, 5, 20);
            using (FastBufferWriter writer = NetMessages.WriteAnswer(answer))
            {
                FastBufferReader reader = ReaderOf(writer);
                Assert.That(NetMessages.TryReadAnswer(ref reader, out CommandResolved got), Is.True);
                reader.Dispose();
                Assert.That((got.Seat, got.Epoch, got.CommandId, got.Rejection, got.IsRepeat, got.CurrentEpoch, got.NextCommandId),
                    Is.EqualTo((answer.Seat, 4, 19L, CommandRejection.QueueFull, true, 5, 20L)));
            }
            using (FastBufferWriter writer = NetMessages.WriteWelcome(new SeatId(2), 3, 1))
            {
                FastBufferReader reader = ReaderOf(writer);
                Assert.That(NetMessages.TryReadWelcome(ref reader, out ushort version, out SeatId seat, out int epoch, out long next), Is.True);
                reader.Dispose();
                Assert.That((version, seat, epoch, next), Is.EqualTo((NetMessages.ProtocolVersion, new SeatId(2), 3, 1L)));
            }
            var seats = new List<SeatSnapshot> { new SeatSnapshot { Id = new SeatId(1), Team = 1, Controller = SeatController.Human }, new SeatSnapshot { Id = new SeatId(8), Team = 2, Controller = SeatController.Computer } };
            using (FastBufferWriter writer = NetMessages.WriteSeats(seats))
            {
                FastBufferReader reader = ReaderOf(writer);
                var got = new List<SeatSnapshot>();
                Assert.That(NetMessages.TryReadSeats(ref reader, got), Is.True);
                reader.Dispose();
                Assert.That(got, Is.EqualTo(seats));
            }
        }

        // ---------------- seat binding ----------------

        private static (World world, SeatRegistry seats, SeatBinder binder) Binding()
        {
            var world = new World(new SimConfig(10, 8, 1));
            var seats = new SeatRegistry(world);
            seats.Add(new SeatId(1), "Red", 1, SeatController.Human);
            seats.Add(new SeatId(2), "Blue", 1, SeatController.Computer);
            seats.Add(new SeatId(3), "Green", 1, SeatController.Human);
            seats.Add(new SeatId(8), "Dinosaurs", 2, SeatController.Computer);
            return (world, seats, new SeatBinder(seats, new[] { new SeatId(1), new SeatId(2), new SeatId(3) }, hostSeat: new SeatId(1)));
        }

        [Test]
        public void ConnectionsAreSeatedInOrderNeverInTheHostsSeatOrAnUnplayableOne()
        {
            (_, SeatRegistry seats, SeatBinder binder) = Binding();

            Assert.That(binder.Bind(100), Is.EqualTo(new SeatId(2)));
            Assert.That(binder.Bind(101), Is.EqualTo(new SeatId(3)));
            Assert.That(binder.Bind(102), Is.EqualTo(SeatId.None), "full: the dinosaurs' seat is never handed out");
            Assert.That(binder.Bind(100), Is.EqualTo(new SeatId(2)), "binding twice is the same seat");

            seats.TryGet(new SeatId(2), out Seat blue);
            Assert.That(blue.Controller, Is.EqualTo(SeatController.Human));
            Assert.That(binder.TryGetClient(new SeatId(3), out ulong client) && client == 101, Is.True);
        }

        [Test]
        public void EveryBindingAndEveryDepartureStartsANewEpoch()
        {
            (_, SeatRegistry seats, SeatBinder binder) = Binding();
            seats.TryGet(new SeatId(2), out Seat blue);
            seats.TryGet(new SeatId(3), out Seat green);

            binder.Bind(100);
            binder.Bind(101);
            Assert.That(blue.ControllerEpoch, Is.EqualTo(2), "computer to human");
            Assert.That(green.ControllerEpoch, Is.EqualTo(2), "already marked human, but a new connection still gets a fresh epoch");

            Assert.That(binder.Unbind(100), Is.EqualTo(new SeatId(2)));
            Assert.That(blue.Controller, Is.EqualTo(SeatController.Computer));
            Assert.That(blue.ControllerEpoch, Is.EqualTo(3));
            Assert.That(binder.Unbind(100), Is.EqualTo(SeatId.None));

            Assert.That(binder.Bind(200), Is.EqualTo(new SeatId(2)), "the freed seat goes to the next connection");
            Assert.That(blue.ControllerEpoch, Is.EqualTo(4));
        }

        // ---------------- the client's half of the command protocol ----------------

        [Test]
        public void ASenderToldWhereToContinueAfterADropContinuesThere()
        {
            var sentIds = new List<long>();
            var sender = new CommandSender(c => { sentIds.Add(c.CommandId); return SubmitOutcome.Queued; }, new SeatId(2), epoch: 3, nextCommandId: 5);
            sender.Send(CommandKind.Move, new[] { new EntityId(1) });
            sender.Send(CommandKind.Move, new[] { new EntityId(1) });
            Assert.That(sentIds, Is.EqualTo(new long[] { 5, 6 }), "continues where the host said, optimistically");

            // The host dropped 6 at the door (flood) and says the next id it will take is still 6.
            Assert.That(sender.Observe(new CommandResolved(new SeatId(2), 3, 6, CommandRejection.DroppedFlood, false, 3, 6)), Is.True);
            sender.Send(CommandKind.Move, new[] { new EntityId(1) });
            Assert.That(sentIds.Last(), Is.EqualTo(6));
        }

        // ---------------- the read model ----------------

        [Test]
        public void TheReadModelReportsWhoAppearedAndVanishedKeepsPreviousPositionsAndIgnoresOldSnapshots()
        {
            var catalog = UnityEngine.ScriptableObject.CreateInstance<EntityCatalogAsset>();
            catalog.entries = new[] { new EntityCatalogAsset.Entry { id = "survivor" } };
            var map = new MapDefinition(2, 2, 2f, Enumerable.Repeat(CellFlags.Walkable, 4).ToArray(), new CampDefinition[0], new RegionDefinition[0]);
            var model = new MatchReadModel(catalog, map);
            var appeared = new List<long>(); var vanished = new List<long>();
            model.EntityAppeared += e => appeared.Add(e.Id.Value);
            model.EntityVanished += id => vanished.Add(id.Value);
            EntitySnapshot At(long id, float x) => new EntitySnapshot { Id = new EntityId(id), Position = new SimVector2(x, 0f) };

            Assert.That(model.Apply(1, new[] { At(1, 0f), At(2, 0f) }), Is.True);
            Assert.That(model.Apply(2, new[] { At(2, 1.5f), At(3, 0f) }), Is.True);

            Assert.That(appeared, Is.EqualTo(new long[] { 1, 2, 3 }));
            Assert.That(vanished, Is.EqualTo(new long[] { 1 }));
            Assert.That(model.PreviousPositionOf(new EntityId(2), default), Is.EqualTo(new SimVector2(0f, 0f)));
            Assert.That(model.PreviousPositionOf(new EntityId(3), new SimVector2(9f, 9f)), Is.EqualTo(new SimVector2(9f, 9f)), "a newcomer has no past");

            Assert.That(model.Apply(2, new[] { At(9, 0f) }), Is.False, "a late or repeated snapshot never moves the screen backwards");
            Assert.That(model.TryGet(new EntityId(9), out _), Is.False);
            Assert.That(model.Revision, Is.EqualTo(2));
            UnityEngine.Object.DestroyImmediate(catalog);
        }
    }
}
