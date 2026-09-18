using System.Collections.Generic;
using System.Linq;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class MatchFlowTests
    {
        private static readonly SeatId Red = new SeatId(1), Blue = new SeatId(2), Dinos = new SeatId(8);

        private World world;
        private GridMap map;
        private DefinitionCatalog catalog;
        private SeatRegistry seats;
        private EntitySpawner spawner;
        private TaskSystem tasks;
        private CommandRouter router;
        private MatchFlow match;
        private CommandSender red, blue;
        private readonly List<SimEvent> log = new List<SimEvent>();

        private static MatchRules Rules(float selection = 2f, float survival = 30f, float helicopter = 5f, IReadOnlyList<SpawnTimerRule> timers = null) => new MatchRules(
            selectionWindowSeconds: selection,
            modes: new[] { new MatchMode("A", "short", survival), new MatchMode("B", "long", survival * 2f) },
            defaultModeIndex: 0, difficultyCount: 6, defaultDifficulty: 2,
            helicopterWindowSeconds: helicopter, startTimeOfDay: 17.5f, freezeTimeOfDayAtEvacuation: 3f, dayLengthSeconds: 48f,
            nightStartsAt: 18f, nightEndsAt: 6f, spawnTimers: timers ?? new SpawnTimerRule[0], helicopterDefinitionId: "helicopter", dinosaurSeat: Dinos);

        private static SpawnTimerRule Timer(string id, float enabledAt, int[] gate, float min, float max, params (string def, int count)[] group) =>
            new SpawnTimerRule(id, enabledAt, gate, min, max, new SpawnBatch(new[] { (IReadOnlyList<KeyValuePair<string, int>>)group.Select(g => new KeyValuePair<string, int>(g.def, g.count)).ToList() }));

        [SetUp]
        public void SetUp() => Build(Rules());

        private void Build(MatchRules rules, ulong seed = 7)
        {
            log.Clear();
            world = new World(new SimConfig(10, 8, seed));
            map = FixtureMaps.CampValleyGrid();
            catalog = new DefinitionCatalog();
            catalog.Add(new EntityDefinition("survivor", 4f, storageCapacity: 5, maxHealth: 40));
            catalog.Add(new EntityDefinition("raptor", 6f, maxHealth: 80, attackDamage: 10, attackSeconds: 0.5f, perceptionRadius: 12f, canBreach: true));
            catalog.Add(new EntityDefinition("helicopter", 0f));
            catalog.Add(new EntityDefinition("pile", 0f));
            var goods = new Logistics(world, new LogisticsConfig(20, "pile"));
            var vitals = new Vitals(world);
            seats = new SeatRegistry(world);
            seats.Add(Red, "Red", 1, SeatController.Human);
            seats.Add(Blue, "Blue", 1, SeatController.Human);
            seats.Add(Dinos, "Dinosaurs", 2, SeatController.Computer);
            var structures = new Structures(world, map, goods, vitals, seats);
            spawner = new EntitySpawner(world, map, catalog, goods, vitals, structures);
            var context = new TaskContext(world, map, catalog, new TaskConfig(5, 3, 2, new PathOptions()), goods, seats, structures, vitals);
            tasks = new TaskSystem(context);
            match = new MatchFlow(world, rules, seats, map, catalog, spawner);
            router = new CommandRouter(seats, new CommandRouterConfig(8, 16, 8));
            router.Register(CommandKind.Move, new MoveCommandHandler(tasks));
            router.Register(CommandKind.Attack, new AttackCommandHandler(tasks));
            router.Register(CommandKind.ChooseMatch, new ChooseMatchCommandHandler(match));
            router.Register(CommandKind.Board, new BoardCommandHandler(tasks, match));
            world.AddSystem(router);
            world.AddSystem(tasks);
            world.AddSystem(goods);
            world.AddSystem(structures);
            world.AddSystem(new Predators(context, router, 5));
            world.AddSystem(match);
            red = new CommandSender(router, Red, 1);
            blue = new CommandSender(router, Blue, 1);
        }

        private Entity Survivor(SeatId owner, Cell cell)
        {
            Entity e = spawner.Spawn("survivor", owner, cell, out string problem);
            Assert.That(e, Is.Not.Null, problem);
            world.Commit();
            return e;
        }

        private void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.Step();
                log.AddRange(world.DrainEvents());
                Assert.That(world.IsFaulted, Is.False);
            }
        }

        private int Dinosaurs() => world.Entities.Count(e => e.IsAlive && e.Owner == Dinos);

        [Test]
        public void WithNobodyChoosingTheDefaultsApplyWhenTheWindowCloses()
        {
            Survivor(Red, new Cell(8, 5));
            Run(19);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Setup));
            Assert.That(match.SecondsLeft, Is.EqualTo(0.1).Within(1e-6));
            Run(1);

            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Survival));
            Assert.That((match.ModeIndex, match.Difficulty), Is.EqualTo((0, 2)));
            Assert.That(log.OfType<MatchPhaseChanged>().Single().Phase, Is.EqualTo(MatchPhase.Survival));
            Assert.That(match.SecondsLeft, Is.EqualTo(30).Within(1e-6));
        }

        [Test]
        public void TheFirstSeatToChooseSetsTheMatchAndLaterChoicesAreRefused()
        {
            Entity r = Survivor(Red, new Cell(8, 5)), b = Survivor(Blue, new Cell(9, 5));
            blue.Send(CommandKind.ChooseMatch, new[] { b.Id }, argument: 1 * 10 + 5);
            red.Send(CommandKind.ChooseMatch, new[] { r.Id }, argument: 0 * 10 + 1);
            Run(1);

            Assert.That((match.ModeIndex, match.Difficulty), Is.EqualTo((1, 5)));
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Survival), "choosing starts the match at once");
            Assert.That(log.OfType<CommandResolved>().Select(a => a.Rejection), Is.EqualTo(new[] { CommandRejection.None, CommandRejection.InvalidTarget }));
            Assert.That(log.OfType<MatchOptionsChosen>().Single().By, Is.EqualTo(Blue));
            Assert.That(match.SecondsLeft, Is.EqualTo(60).Within(1e-6), "the long mode");

            red.Send(CommandKind.ChooseMatch, new[] { r.Id }, argument: 99);
            Run(1);
            Assert.That(log.OfType<CommandResolved>().Last().Rejection, Is.EqualTo(CommandRejection.InvalidTarget));
        }

        [Test]
        public void SpawnTimersFireOnTheirPeriodsInTheSpawnRegionAndOnlyForTheirDifficulties()
        {
            Build(Rules(selection: 0.5f, survival: 60f, timers: new[]
            {
                Timer("pack", 0f, new[] { 2, 3 }, 2f, 2f, ("raptor", 2)),
                Timer("late", 10f, null, 3f, 3f, ("raptor", 1)),
                Timer("hard", 0f, new[] { 5, 6 }, 1f, 1f, ("raptor", 5)),
            }));
            Survivor(Red, new Cell(1, 10)); // out of the raptors' 12 m sight, so the match is not decided by a wipe
            Run(5);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Survival));

            Run(20);
            Assert.That(Dinosaurs(), Is.EqualTo(2), "one firing of the pack timer at 2 s; the hard timer is not for difficulty 2; the late one has not started");
            Assert.That(log.OfType<DinosaursSpawned>().Single().TimerId, Is.EqualTo("pack"));
            foreach (Entity dino in world.Entities.Where(e => e.Owner == Dinos))
            {
                Cell at = map.CellAt(dino.Position);
                Assert.That(at.X, Is.InRange(13, 14));
                Assert.That(at.Y, Is.InRange(1, 3));
            }

            Run(110); // 13 s into survival
            int packFirings = log.OfType<DinosaursSpawned>().Count(e => e.TimerId == "pack");
            int lateFirings = log.OfType<DinosaursSpawned>().Count(e => e.TimerId == "late");
            Assert.That(packFirings, Is.EqualTo(6), "every 2 s: at 2, 4, 6, 8, 10 and 12 s");
            Assert.That(lateFirings, Is.EqualTo(1), "started at 10 s, first firing at 13 s");
            Assert.That(log.OfType<DinosaursSpawned>().Any(e => e.TimerId == "hard"), Is.False);
        }

        [Test]
        public void ARandomPeriodStaysInsideItsRangeAndTheSameSeedGivesTheSameSpawns()
        {
            List<long> Ticks()
            {
                Build(Rules(selection: 0.5f, survival: 80f, timers: new[] { Timer("t", 0f, null, 3f, 7f, ("raptor", 1)) }));
                Survivor(Red, new Cell(1, 10));
                Run(700);
                return log.OfType<DinosaursSpawned>().Select(e => e.Tick).ToList();
            }
            List<long> first = Ticks(), second = Ticks();

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first.Count, Is.GreaterThan(8));
            for (int i = 1; i < first.Count; i++) Assert.That(first[i] - first[i - 1], Is.InRange(30, 71));
        }

        [Test]
        public void TheClockRunsFromDuskFreezesAtTheEvacuationAndKnowsNightFromDay()
        {
            Build(Rules(selection: 0.5f, survival: 20f));
            Survivor(Red, new Cell(8, 5));
            Assert.That(match.Clock.TimeOfDay, Is.EqualTo(17.5f));
            Assert.That(match.Clock.IsNight, Is.False);
            Run(5);
            Run(20); // 2 s of a 48 s day = 1 hour
            Assert.That(match.Clock.TimeOfDay, Is.EqualTo(18.5f).Within(0.01f));
            Assert.That(match.Clock.IsNight, Is.True);
            Run(200);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Evacuation));
            Assert.That(match.Clock.TimeOfDay, Is.EqualTo(3f));
            Assert.That(match.Clock.Frozen, Is.True);
            Run(50);
            Assert.That(match.Clock.TimeOfDay, Is.EqualTo(3f), "frozen means frozen");
        }

        [Test]
        public void WhoBoardsTheHelicopterWinsWhoDoesNotLoses()
        {
            Build(Rules(selection: 0.5f, survival: 3f, helicopter: 6f));
            Entity r = Survivor(Red, new Cell(3, 9)), b = Survivor(Blue, new Cell(14, 1));
            Run(40);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Evacuation));
            Entity helicopter = world.Entities.Single(e => e.DefinitionId == "helicopter");
            Cell landed = map.CellAt(helicopter.Position);
            Assert.That(landed.X, Is.InRange(1, 2));
            Assert.That(landed.Y, Is.InRange(9, 10), "in the evacuation region");
            Assert.That(log.OfType<HelicopterLanded>().Single().Helicopter, Is.EqualTo(helicopter.Id));

            red.Send(CommandKind.Board, new[] { r.Id });
            Run(30);
            Assert.That(world.IsAlive(r.Id), Is.False, "aboard");
            Assert.That(match.BoardedOf(Red), Is.EqualTo(1));
            Assert.That(match.OutcomeOf(Red), Is.EqualTo(SeatOutcome.Won), "everyone of Red is aboard: decided at once");
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Evacuation), "Blue is still out there");

            Run(40);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Ended));
            Assert.That(match.OutcomeOf(Blue), Is.EqualTo(SeatOutcome.Lost));
            Assert.That(world.IsAlive(b.Id), Is.True, "left behind, not killed");
            Assert.That(log.OfType<SeatOutcomeDecided>().Select(e => (e.Seat, e.Outcome)), Is.EqualTo(new[] { (Red, SeatOutcome.Won), (Blue, SeatOutcome.Lost) }));
        }

        [Test]
        public void ASeatWithNobodyLeftLosesAtOnceAndBoardingIsRefusedOutsideTheEvacuation()
        {
            Build(Rules(selection: 0.5f, survival: 40f));
            Entity r = Survivor(Red, new Cell(8, 5));
            Survivor(Blue, new Cell(9, 5));
            red.Send(CommandKind.Board, new[] { r.Id });
            Run(6);
            Assert.That(log.OfType<CommandResolved>().Last().Rejection, Is.EqualTo(CommandRejection.InvalidTarget), "no helicopter yet");

            world.Despawn(r.Id, "eaten");
            Run(2);
            Assert.That(match.OutcomeOf(Red), Is.EqualTo(SeatOutcome.Lost));
            Assert.That(match.OutcomeOf(Blue), Is.EqualTo(SeatOutcome.Undecided));
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Survival), "the match goes on for Blue");
        }

        [Test]
        public void SpawnedDinosaursHuntOnTheirOwn()
        {
            Build(Rules(selection: 0.5f, survival: 120f, timers: new[] { Timer("t", 0f, null, 1f, 1f, ("raptor", 1)) }));
            Entity prey = Survivor(Red, new Cell(14, 4));
            for (int i = 0; i < 600 && world.IsAlive(prey.Id); i++) Run(1);
            Assert.That(world.IsAlive(prey.Id), Is.False);
        }

        [Test]
        public void ABuiltOverEvacuationRegionStillGetsAHelicopterSomewhereAndAWipeEndsTheMatchAtOnce()
        {
            Build(Rules(selection: 0.5f, survival: 3f, helicopter: 6f));
            catalog.Add(new EntityDefinition("boulder", 0f, blocks: true, destructible: false));
            // The fixture's evacuation region is (1,9)-(2,10): fill every cell of it.
            foreach (Cell c in new[] { new Cell(1, 9), new Cell(2, 9), new Cell(1, 10), new Cell(2, 10) }) Assert.That(spawner.Spawn("boulder", SeatId.None, c, out _), Is.Not.Null);
            Entity r = Survivor(Red, new Cell(8, 5));
            Run(40);

            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Evacuation));
            Assert.That(match.Helicopter.IsNone, Is.False, "it landed elsewhere on the map instead of leaving everyone to lose in silence");
            Assert.That(log.OfType<HelicopterLanded>().Count(), Is.EqualTo(1));

            Build(Rules(selection: 0.5f, survival: 60f));
            Entity a = Survivor(Red, new Cell(8, 5)), b = Survivor(Blue, new Cell(9, 5));
            Run(10);
            world.Despawn(a.Id, "eaten");
            world.Despawn(b.Id, "eaten");
            Run(2);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Ended), "everyone gone: no helicopter to wait for");
            Assert.That(log.OfType<SeatOutcomeDecided>().Select(e => e.Outcome), Is.All.EqualTo(SeatOutcome.Lost));
        }

        [Test]
        public void ASpawnRegionWithNoRoomIsReportedAndDifferentSeedsSpawnDifferently()
        {
            Build(Rules(selection: 0.5f, survival: 60f, timers: new[] { Timer("t", 0f, null, 1f, 1f, ("raptor", 1)) }));
            catalog.Add(new EntityDefinition("boulder", 0f, blocks: true, destructible: false));
            for (int x = 13; x <= 14; x++) for (int y = 1; y <= 3; y++) spawner.Spawn("boulder", SeatId.None, new Cell(x, y), out _);
            Survivor(Red, new Cell(8, 5));
            Run(30);
            Assert.That(log.OfType<SpawnSkipped>().Count(), Is.GreaterThan(0), "a full region is a definite result");
            Assert.That(Dinosaurs(), Is.EqualTo(0), "and nothing was put on an illegal cell");

            List<Cell> CellsWithSeed(ulong seed)
            {
                Build(Rules(selection: 0.5f, survival: 60f, timers: new[] { Timer("t", 0f, null, 1f, 1f, ("raptor", 1)) }), seed);
                Survivor(Red, new Cell(1, 10));
                Run(60);
                return world.Entities.Where(e => e.Owner == Dinos).Select(e => map.CellAt(e.Position)).ToList();
            }
            Assert.That(CellsWithSeed(1), Is.Not.EqualTo(CellsWithSeed(2)), "same map, different seed: different dynamic events");
        }

        [Test]
        public void AWeightedBatchRollsByItsWeightsAndAlwaysIncludesTheCommonGroup()
        {
            var batch = new SpawnBatch(new IReadOnlyList<KeyValuePair<string, int>>[]
            {
                new[] { new KeyValuePair<string, int>("a", 2), new KeyValuePair<string, int>("c", 1) },
                new[] { new KeyValuePair<string, int>("b", 2), new KeyValuePair<string, int>("c", 1) },
            }, new[] { 1, 2 });
            Assert.That(batch.TotalWeight, Is.EqualTo(3));
            Assert.That(new[] { batch.Pick(0), batch.Pick(1), batch.Pick(2) }, Is.EqualTo(new[] { 0, 1, 1 }), "one third the first, two thirds the second");
        }
    }
}
