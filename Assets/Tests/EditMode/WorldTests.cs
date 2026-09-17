using System.Collections.Generic;
using System.Linq;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class WorldTests
    {
        private static World NewWorld(ulong seed = 1, int ticksPerSecond = 10, int maxSteps = 8) =>
            new World(new SimConfig(ticksPerSecond, maxSteps, seed));

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
        public void EventsAreVisibleOnlyAfterCommitAndAHeldBatchIsNeverAppendedTo()
        {
            World world = NewWorld();
            world.Spawn(EntityKind.Unit, "survivor", new SeatId(1), SimVector2.Zero);
            Assert.That(world.Events, Is.Empty);

            world.Step();
            IReadOnlyList<SimEvent> firstBatch = world.Events;
            Assert.That(firstBatch, Has.Count.EqualTo(1));
            Assert.That(firstBatch[0], Is.InstanceOf<EntitySpawned>());

            world.BeginEventBatch();
            world.Spawn(EntityKind.Unit, "raptor", SeatId.None, SimVector2.Zero);
            world.Step();

            Assert.That(firstBatch, Has.Count.EqualTo(1));
            Assert.That(world.Events, Has.Count.EqualTo(1));
            Assert.That(world.Events[0].Tick, Is.EqualTo(2));
        }

        private sealed class SpawnEveryTick : ISimSystem
        {
            public readonly List<long> TicksSeen = new List<long>();
            public void Tick(World world)
            {
                TicksSeen.Add(world.Tick);
                world.Spawn(EntityKind.Unit, "raptor", SeatId.None, SimVector2.Zero);
            }
        }

        [Test]
        public void AdvanceKeepsTheEventsOfEveryTickItRuns()
        {
            World world = NewWorld();
            world.AddSystem(new SpawnEveryTick());

            Assert.That(world.Advance(0.35f), Is.EqualTo(3));

            Assert.That(world.Events.Select(e => e.Tick), Is.EqualTo(new long[] { 1, 2, 3 }));

            world.Advance(0.1f);
            Assert.That(world.Events.Select(e => e.Tick), Is.EqualTo(new long[] { 4 }), "a new Advance starts a new batch");
        }

        [Test]
        public void ASystemSeesTheSameTickItsEventsAreStampedWith()
        {
            World world = NewWorld();
            var system = new SpawnEveryTick();
            world.AddSystem(system);

            world.Step();
            world.Step();

            Assert.That(system.TicksSeen, Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(world.Events.Select(e => e.Tick), Is.EqualTo(system.TicksSeen));
            Assert.That(world.Time, Is.EqualTo(0.2).Within(1e-12));
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

            Assert.That(log, Is.EqualTo(new[] { "orders@1", "movement@1", "orders@2", "movement@2" }));
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
        public void AnEntityBornAndRemovedInOneTickLeavesNoTraceButItsEvents()
        {
            World world = NewWorld();
            Entity shortLived = world.Spawn(EntityKind.GroundPile, "wood", SeatId.None, SimVector2.Zero);
            world.Despawn(shortLived.Id, "picked up");
            Entity next = world.Spawn(EntityKind.GroundPile, "wood", SeatId.None, SimVector2.Zero);
            world.Step();

            Assert.That(world.TryGet(shortLived.Id, out _), Is.False);
            Assert.That(world.Entities.Select(e => e.Id), Is.EqualTo(new[] { next.Id }));
            Assert.That(next.Id.Value, Is.GreaterThan(shortLived.Id.Value), "ids only ever grow, even within one tick");
            Assert.That(world.Events.Select(e => e.GetType()), Is.EqualTo(new[] { typeof(EntitySpawned), typeof(EntityRemoved), typeof(EntitySpawned) }));
        }

        [Test]
        public void EntitiesIsAReadOnlyView()
        {
            World world = NewWorld();
            Assert.That(world.Entities, Is.Not.InstanceOf<List<Entity>>());
        }

        private sealed class Thrower : ISimSystem
        {
            public void Tick(World world)
            {
                foreach (Entity entity in world.Entities) world.Despawn(entity.Id, "half done");
                throw new System.InvalidOperationException("boom");
            }
        }

        [Test]
        public void ASystemExceptionFaultsTheWorldInsteadOfCommittingATornTick()
        {
            World world = NewWorld();
            world.Spawn(EntityKind.Unit, "survivor", new SeatId(1), SimVector2.Zero);
            world.Commit();
            world.BeginEventBatch();
            world.AddSystem(new Thrower());

            Assert.Throws<System.InvalidOperationException>(() => world.Step());

            Assert.That(world.IsFaulted, Is.True);
            Assert.That(world.Events, Is.Empty, "the half-applied removal must not be published");
            Assert.Throws<System.InvalidOperationException>(() => world.Step());
            Assert.Throws<System.InvalidOperationException>(() => world.Advance(1f));
            Assert.Throws<System.InvalidOperationException>(() => world.Commit());
        }

        private sealed class Reentrant : ISimSystem
        {
            public System.Exception FromStep, FromCommit, FromAddSystem;
            public void Tick(World world)
            {
                try { world.Step(); } catch (System.InvalidOperationException e) { FromStep = e; }
                try { world.Commit(); } catch (System.InvalidOperationException e) { FromCommit = e; }
                try { world.AddSystem(this); } catch (System.InvalidOperationException e) { FromAddSystem = e; }
            }
        }

        [Test]
        public void ATickCannotBeReenteredFromInsideASystem()
        {
            World world = NewWorld();
            var system = new Reentrant();
            world.AddSystem(system);

            world.Step();

            Assert.That(system.FromStep, Is.Not.Null);
            Assert.That(system.FromCommit, Is.Not.Null);
            Assert.That(system.FromAddSystem, Is.Not.Null);
            Assert.That(world.IsFaulted, Is.False);
            Assert.That(world.Tick, Is.EqualTo(1));
        }

        [Test]
        public void AdvanceRunsWholeFixedStepsAndKeepsTheRemainder()
        {
            World world = NewWorld(ticksPerSecond: 10);

            Assert.That(world.Advance(0.25f), Is.EqualTo(2));
            Assert.That(world.Advance(0.06f), Is.EqualTo(1), "0.05 carried over plus 0.06");
            Assert.That(world.Tick, Is.EqualTo(3));
            Assert.That(world.Time, Is.EqualTo(0.3).Within(1e-12));
        }

        [Test]
        public void AdvanceCapsCatchUpAfterAStallButKeepsThePhase()
        {
            World world = NewWorld(ticksPerSecond: 10, maxSteps: 4);

            Assert.That(world.Advance(10.07f), Is.EqualTo(4));
            Assert.That(world.Advance(0.02f), Is.EqualTo(0), "whole ticks of backlog are dropped");
            Assert.That(world.Advance(0.02f), Is.EqualTo(1), "but the 0.07 sub-tick remainder was kept");
        }

        [Test]
        public void AdvanceRejectsTimeThatIsNotAFiniteNonNegativeNumber()
        {
            World world = NewWorld();
            Assert.Throws<System.ArgumentOutOfRangeException>(() => world.Advance(float.NaN));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => world.Advance(-0.1f));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => world.Advance(float.PositiveInfinity));
            Assert.That(world.Advance(0.1f), Is.EqualTo(1), "a rejected call must not poison the accumulator");
        }

        [Test]
        public void TheSameSeedGivesTheSameSequence()
        {
            var a = new SimRandom(42);
            var b = new SimRandom(42);
            var c = new SimRandom(43);
            float[] fromA = Enumerable.Range(0, 16).Select(_ => a.RangeInclusive(1.4f, 750f)).ToArray();
            float[] fromB = Enumerable.Range(0, 16).Select(_ => b.RangeInclusive(1.4f, 750f)).ToArray();
            float[] fromC = Enumerable.Range(0, 16).Select(_ => c.RangeInclusive(1.4f, 750f)).ToArray();

            Assert.That(fromA, Is.EqualTo(fromB));
            Assert.That(fromA, Is.Not.EqualTo(fromC));
            Assert.That(fromA, Is.All.InRange(1.4f, 750f));
        }

        [Test]
        public void IntegerRangeCoversEveryValueAndExcludesTheUpperBound()
        {
            var random = new SimRandom(0);
            var seen = new HashSet<int>();
            for (int i = 0; i < 2000; i++) seen.Add(random.Range(-3, 4));

            Assert.That(seen.OrderBy(v => v), Is.EqualTo(new[] { -3, -2, -1, 0, 1, 2, 3 }));
        }
    }
}
