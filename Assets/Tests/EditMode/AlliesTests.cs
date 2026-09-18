using System.Collections.Generic;
using System.Linq;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class AlliesTests
    {
        private static readonly SeatId Red = new SeatId(1), Blue = new SeatId(2), Dinos = new SeatId(8);
        private const string Wood = "wood";

        private World world;
        private GridMap map;
        private DefinitionCatalog catalog;
        private Logistics goods;
        private Structures structures;
        private TaskSystem tasks;
        private CommandRouter router;
        private EntitySpawner spawner;
        private SeatRegistry seats;
        private readonly List<SimEvent> log = new List<SimEvent>();

        [SetUp]
        public void SetUp()
        {
            log.Clear();
            world = new World(new SimConfig(10, 8, 3));
            map = FixtureMaps.CampValleyGrid();
            catalog = new DefinitionCatalog();
            catalog.Add(new EntityDefinition("survivor", 4f, storageCapacity: 5, gatherSecondsPerUnit: 0.2f, maxHealth: 40));
            catalog.Add(new EntityDefinition("depot", 0f, storageCapacity: 100, isDepot: true, blocks: true, maxHealth: 50));
            catalog.Add(new EntityDefinition("tree", 0f, nodeResource: Wood, nodeAmount: 400, blocks: true, destructible: false));
            catalog.Add(new EntityDefinition("pile", 0f));
            catalog.Add(new EntityDefinition("wall", 0f, blocks: true, destructible: true, maxHealth: 30, buildCost: new Dictionary<string, int> { [Wood] = 4 }, buildWorkSeconds: 1f));
            catalog.Add(new EntityDefinition("gate", 0f, blocks: true, destructible: true, maxHealth: 30, buildCost: new Dictionary<string, int> { [Wood] = 4 }, buildWorkSeconds: 1f, isGate: true));
            goods = new Logistics(world, new LogisticsConfig(20, "pile"));
            var vitals = new Vitals(world);
            seats = new SeatRegistry(world);
            seats.Add(Red, "Red", 1, SeatController.Human);
            seats.Add(Blue, "Blue", 1, SeatController.Computer);
            seats.Add(Dinos, "Dinosaurs", 2, SeatController.Computer);
            structures = new Structures(world, map, goods, vitals, seats);
            var context = new TaskContext(world, map, catalog, new TaskConfig(5, 3, 2, new PathOptions()), goods, seats, structures, vitals);
            tasks = new TaskSystem(context);
            spawner = new EntitySpawner(world, map, catalog, goods, vitals, structures);
            router = new CommandRouter(seats, new CommandRouterConfig(8, 16, 8));
            router.Register(CommandKind.Move, new MoveCommandHandler(tasks));
            router.Register(CommandKind.Gather, new HaulCommandHandler(tasks, CommandKind.Gather));
            router.Register(CommandKind.Build, new BuildCommandHandler(tasks));
            router.Register(CommandKind.ToggleGate, new StructureCommandHandler(tasks, CommandKind.ToggleGate));
            world.AddSystem(router);
            world.AddSystem(tasks);
            world.AddSystem(goods);
            world.AddSystem(structures);
            world.AddSystem(new Allies(context, router, new AllyConfig(thinkIntervalTicks: 10, woodReserve: 8, "wall", "gate", Wood)));
        }

        private Entity Place(string id, SeatId owner, Cell cell)
        {
            Entity e = spawner.Spawn(id, owner, cell, out string problem);
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

        private int WoodIn(Entity depot) => goods.TryGetContainer(depot.Id, out Container c) ? c.AmountOf(Wood) : 0;

        [Test]
        public void TheAllyGathersUntilItsReserveThenWallsEveryEntranceThenGathersAgain()
        {
            Entity depot = Place("depot", Blue, FixtureMaps.CampGround);
            Place("tree", SeatId.None, new Cell(13, 9));
            Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity worker = Place("survivor", Blue, new Cell(8, 5));
            Run(11);

            Assert.That(tasks.CurrentOf(worker.Id), Is.InstanceOf<GatherTask>(), "poor: gather first");
            Assert.That(log.OfType<CommandResolved>().All(a => a.Seat == Blue && a.Accepted), Is.True, "through the door, and accepted");

            bool Closed() => world.Entities.Any(e => e.Kind == EntityKind.Building && map.CellAt(e.Position) == FixtureMaps.MainEntrance)
                && world.Entities.Any(e => e.Kind == EntityKind.Building && map.CellAt(e.Position) == FixtureMaps.DetourGate);
            for (int i = 0; i < 3000 && !Closed(); i++) Run(1);
            Assert.That(Closed(), Is.True, "something stands in the main entrance and in the far gate");
            Assert.That(log.OfType<CommandResolved>().Where(a => !a.Accepted).Select(a => a.Rejection).ToArray(), Is.Empty, "it never asked for something it could not have");

            for (int i = 0; i < 1500 && structures.SiteCount > 0; i++) Run(1);
            Assert.That(world.Entities.Any(e => e.DefinitionId == "gate" && map.CellAt(e.Position) == FixtureMaps.MainEntrance), Is.True, "the first entrance got a gate, not a wall");
            var site = world.Entities.FirstOrDefault(e => structures.TryGetSite(e.Id, out _));
            string siteInfo = "none";
            if (site != null)
            {
                goods.TryGetContainer(site.Id, out Container siteMaterials);
                structures.TryGetSite(site.Id, out BuildSite pending);
                siteInfo = map.CellAt(site.Position) + " materials=" + (siteMaterials == null ? -1 : siteMaterials.Total) + " reservedRoom=" + goods.ReservedRoomIn(site.Id) + " work=" + pending.Work;
            }
            goods.TryGetContainer(worker.Id, out Container workerPack);
            SimTask workerTask = tasks.CurrentOf(worker.Id);
            string workerInfo = map.CellAt(worker.Position) + " pack=" + (workerPack == null ? -1 : workerPack.Total) + " " + workerTask?.Kind + "/" + workerTask?.State + "/" + workerTask?.Reason;
            Assert.That(structures.SiteCount, Is.EqualTo(0), "both walls finished; worker " + workerInfo + ", depot wood " + WoodIn(depot) + ", live reservations " + goods.LiveReservationCount + ", site " + siteInfo);
            for (int i = 0; i < 100 && !(tasks.CurrentOf(worker.Id) is GatherTask); i++) Run(1);
            Assert.That(tasks.CurrentOf(worker.Id), Is.InstanceOf<GatherTask>(), "camp closed: back to the trees");
            Entity gate = world.Entities.Single(e => e.DefinitionId == "gate");
            for (int i = 0; i < 100 && !structures.IsGateOpen(gate.Id); i++) Run(1);
            Assert.That(structures.IsGateOpen(gate.Id), Is.True, "the gate opens while a worker is out");
            int woodNow = WoodIn(depot);
            for (int i = 0; i < 800 && WoodIn(depot) <= woodNow; i++) Run(1);
            Assert.That(WoodIn(depot), Is.GreaterThan(woodNow), "and wood keeps coming through it");
        }

        [Test]
        public void AHumanSeatIsLeftAloneAndASeatWithoutADepotDoesNothing()
        {
            Place("depot", Red, FixtureMaps.CampGround);
            Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity human = Place("survivor", Red, new Cell(8, 5));
            Entity orphan = Place("survivor", Blue, new Cell(9, 5));
            Run(40);

            Assert.That(tasks.CurrentOf(human.Id), Is.Null, "a human's units wait for the human");
            Assert.That(tasks.CurrentOf(orphan.Id), Is.Null, "no depot, no plan");
            Assert.That(log.OfType<CommandResolved>(), Is.Empty);
        }

        [Test]
        public void WhenAHumanDropsTheComputerPicksUpTheirUnitsAndHandsThemBackOnReturn()
        {
            Place("depot", Red, FixtureMaps.CampGround);
            Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity unit = Place("survivor", Red, new Cell(8, 5));
            Run(20);
            Assert.That(tasks.CurrentOf(unit.Id), Is.Null);

            seats.SetController(Red, SeatController.Computer);
            Run(20);
            Assert.That(tasks.CurrentOf(unit.Id), Is.InstanceOf<GatherTask>(), "the computer took over the dropped seat");

            seats.SetController(Red, SeatController.Human);
            tasks.Stop(unit.Id, TaskReason.Stopped);
            Run(20);
            Assert.That(tasks.CurrentOf(unit.Id), Is.Null, "and stopped thinking for it once the human was back");
        }

        [Test]
        public void TwoIdleWorkersWallTwoDifferentEntrancesNotTheSameOne()
        {
            Entity depot = Place("depot", Blue, FixtureMaps.CampGround);
            goods.Seed(depot.Id, Wood, 40);
            Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Place("survivor", Blue, new Cell(8, 5));
            Place("survivor", Blue, new Cell(9, 5));
            Run(11);

            Assert.That(log.OfType<CommandResolved>().Count(a => a.Accepted), Is.EqualTo(2));
            Assert.That(structures.SiteCount, Is.EqualTo(2));
            Assert.That(world.Entities.Count(e => e.DefinitionId == "gate"), Is.EqualTo(1), "one gate, one wall");
            Assert.That(map.IsWalkable(FixtureMaps.MainEntrance), Is.False);
            Assert.That(map.IsWalkable(FixtureMaps.DetourGate), Is.False);
        }
    }
}
