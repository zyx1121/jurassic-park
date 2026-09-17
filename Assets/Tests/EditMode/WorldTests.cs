using System.Collections.Generic;
using System.Linq;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class WorldTests
    {
        private static World NewWorld(ulong seed = 1, float tickSeconds = 0.1f, int maxSteps = 8) =>
            new World(new SimConfig(tickSeconds, maxSteps, seed));

        private sealed class Recorder : ISimSystem
        {
            private readonly string name;
            private readonly List<string> log;
            public Recorder(string name, List<string> log) { this.name = name; this.log = log; }
            public void Tick(World world) => log.Add($"{name}@{world.Tick}");
        }

        private sealed class Culler : ISimSystem
        {
            public int Visited;
            public void Tick(World world)
            {
                foreach (Entity entity in world.Entities)
                {
                    Visited++;
                    world.Despawn(entity.Id, "culled");
                    world.Spawn(EntityKind.Unit, "raptor", SeatId.None, SimVector2.Zero);
                }
            }
        }

        [Test]
        public void SpawnResolvesAtOnceAndJoinsTheListAtCommit()
        {
            World world = NewWorld();
            Entity survivor = world.Spawn(EntityKind.Unit, "survivor", new SeatId(1), new SimVector2(3f, 4f));

            Assert.That(world.TryGet(survivor.Id, out Entity found), Is.True);
            Assert.That(found, Is.SameAs(survivor));
            Assert.That(found.Owner, Is.EqualTo(new SeatId(1)));
            Assert.That(world.Entities, Is.Empty);

            world.Commit();
            Assert.That(world.Entities, Has.Count.EqualTo(1));
        }

        [Test]
        public void IdsAreNeverReusedAfterRemoval()
        {
            World world = NewWorld();
            Entity first = world.Spawn(EntityKind.Unit, "survivor", new SeatId(1), SimVector2.Zero);
            world.Commit();
            world.Despawn(first.Id, "test");
            world.Commit();
            Entity second = world.Spawn(EntityKind.Unit, "survivor", new SeatId(1), SimVector2.Zero);

            Assert.That(second.Id, Is.Not.EqualTo(first.Id));
            Assert.That(world.TryGet(first.Id, out _), Is.False);
        }

        [Test]
        public void RemovalIsImmediateForLivenessAndDeferredForStructure()
        {
            World world = NewWorld();
            Entity wall = world.Spawn(EntityKind.Building, "wall", new SeatId(1), SimVector2.Zero);
            world.Commit();

            Assert.That(world.Despawn(wall.Id, "destroyed"), Is.True);
            Assert.That(world.IsAlive(wall.Id), Is.False);
            Assert.That(world.TryGet(wall.Id, out _), Is.True, "still resolvable until commit so in-flight work can finish cleanly");
            Assert.That(world.Despawn(wall.Id, "again"), Is.False, "a second request must not raise a second event");

            world.Commit();
            Assert.That(world.TryGet(wall.Id, out _), Is.False);
            Assert.That(world.Entities, Is.Empty);
            Assert.That(world.Events.OfType<EntityRemoved>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void EventsAreVisibleOnlyAfterCommitAndReplacedByTheNextOne()
        {
            World world = NewWorld();
            world.Spawn(EntityKind.Unit, "survivor", new SeatId(1), SimVector2.Zero);
            Assert.That(world.Events, Is.Empty);

            world.Step();
            IReadOnlyList<SimEvent> firstBatch = world.Events;
            Assert.That(firstBatch, Has.Count.EqualTo(1));
            Assert.That(firstBatch[0], Is.InstanceOf<EntitySpawned>());
            Assert.That(firstBatch[0].Tick, Is.EqualTo(1));

            world.Spawn(EntityKind.Unit, "raptor", SeatId.None, SimVector2.Zero);
            Assert.That(firstBatch, Has.Count.EqualTo(1), "a held batch must not see the next tick filling up");

            world.Step();
            Assert.That(world.Events, Has.Count.EqualTo(1));
            Assert.That(world.Events[0].Tick, Is.EqualTo(2));
            world.Step();
            Assert.That(world.Events, Is.Empty);
        }

        [Test]
        public void SystemsRunInRegistrationOrderEveryTick()
        {
            World world = NewWorld();
            var log = new List<string>();
            world.AddSystem(new Recorder("orders", log));
            world.AddSystem(new Recorder("movement", log));

            world.Step();
            world.Step();

            Assert.That(log, Is.EqualTo(new[] { "orders@0", "movement@0", "orders@1", "movement@1" }));
        }

        [Test]
        public void SystemsMaySpawnAndDespawnWhileIterating()
        {
            World world = NewWorld();
            world.Spawn(EntityKind.Unit, "a", SeatId.None, SimVector2.Zero);
            world.Spawn(EntityKind.Unit, "b", SeatId.None, SimVector2.Zero);
            world.Commit();
            var culler = new Culler();
            world.AddSystem(culler);

            world.Step();

            Assert.That(culler.Visited, Is.EqualTo(2));
            Assert.That(world.Entities.Select(e => e.DefinitionId), Is.EqualTo(new[] { "raptor", "raptor" }));
        }

        [Test]
        public void AdvanceRunsWholeFixedStepsAndKeepsTheRemainder()
        {
            World world = NewWorld(tickSeconds: 0.1f);

            Assert.That(world.Advance(0.25f), Is.EqualTo(2));
            Assert.That(world.Advance(0.06f), Is.EqualTo(1), "0.05 carried over plus 0.06");
            Assert.That(world.Tick, Is.EqualTo(3));
            Assert.That(world.Time, Is.EqualTo(0.3).Within(1e-6));
        }

        [Test]
        public void AdvanceCapsCatchUpAfterAStall()
        {
            World world = NewWorld(tickSeconds: 0.1f, maxSteps: 4);

            Assert.That(world.Advance(10f), Is.EqualTo(4));
            Assert.That(world.Advance(0.05f), Is.EqualTo(0), "the backlog is dropped, not replayed");
        }

        [Test]
        public void TheSameSeedGivesTheSameSequence()
        {
            var a = new SimRandom(42);
            var b = new SimRandom(42);
            var c = new SimRandom(43);
            float[] fromA = Enumerable.Range(0, 16).Select(_ => a.Range(1.4f, 750f)).ToArray();
            float[] fromB = Enumerable.Range(0, 16).Select(_ => b.Range(1.4f, 750f)).ToArray();
            float[] fromC = Enumerable.Range(0, 16).Select(_ => c.Range(1.4f, 750f)).ToArray();

            Assert.That(fromA, Is.EqualTo(fromB));
            Assert.That(fromA, Is.Not.EqualTo(fromC));
            Assert.That(fromA, Is.All.InRange(1.4f, 750f));
        }

        [Test]
        public void IntegerRangeStaysInsideItsBounds()
        {
            var random = new SimRandom(0);
            for (int i = 0; i < 1000; i++) Assert.That(random.Range(-3, 4), Is.InRange(-3, 3));
        }
    }
}
