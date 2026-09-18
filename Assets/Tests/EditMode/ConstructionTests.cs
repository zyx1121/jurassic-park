using System.Collections.Generic;
using System.Linq;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class ConstructionTests
    {
        private static readonly SeatId Red = new SeatId(1), Blue = new SeatId(2), Raiders = new SeatId(3);
        private const string Wood = "wood";

        private World world;
        private GridMap map;
        private DefinitionCatalog catalog;
        private Logistics goods;
        private Vitals vitals;
        private Structures structures;
        private TaskSystem tasks;
        private CommandRouter router;
        private CommandSender red;
        private readonly List<SimEvent> log = new List<SimEvent>();
        private long expectedWood;
        private int WallIndex => catalog.IndexOf("wall");
        private int GateIndex => catalog.IndexOf("gate");

        [SetUp]
        public void SetUp()
        {
            log.Clear();
            expectedWood = 0;
            world = new World(new SimConfig(10, 8, 1));
            map = FixtureMaps.CampValleyGrid();
            catalog = new DefinitionCatalog();
            catalog.Add(new EntityDefinition("survivor", 4f, storageCapacity: 5, gatherSecondsPerUnit: 0.2f));
            catalog.Add(new EntityDefinition("depot", 0f, storageCapacity: 100, isDepot: true, blocks: true, maxHealth: 50));
            catalog.Add(new EntityDefinition("tree", 0f, nodeResource: Wood, nodeAmount: 12, blocks: true, destructible: false));
            catalog.Add(new EntityDefinition("pile", 0f));
            catalog.Add(new EntityDefinition("wall", 0f, blocks: true, destructible: true, maxHealth: 30, buildCost: new Dictionary<string, int> { [Wood] = 4 }, buildWorkSeconds: 1f));
            catalog.Add(new EntityDefinition("gate", 0f, blocks: true, destructible: true, maxHealth: 30, buildCost: new Dictionary<string, int> { [Wood] = 2 }, buildWorkSeconds: 0.5f, isGate: true));
            goods = new Logistics(world, new LogisticsConfig(20, "pile"));
            vitals = new Vitals(world);
            var seats = new SeatRegistry(world);
            seats.Add(Red, "Red", 1, SeatController.Human);
            seats.Add(Blue, "Blue", 1, SeatController.Human);
            seats.Add(Raiders, "Raiders", 2, SeatController.Computer);
            structures = new Structures(world, map, goods, vitals, seats);
            tasks = new TaskSystem(new TaskContext(world, map, catalog, new TaskConfig(5, 3, 2, new PathOptions()), goods, seats, structures, vitals));
            router = new CommandRouter(seats, new CommandRouterConfig(8, 16, 8));
            router.Register(CommandKind.Move, new MoveCommandHandler(tasks));
            router.Register(CommandKind.Stop, new StopCommandHandler(tasks));
            router.Register(CommandKind.Gather, new HaulCommandHandler(tasks, CommandKind.Gather));
            router.Register(CommandKind.Build, new BuildCommandHandler(tasks));
            router.Register(CommandKind.Demolish, new StructureCommandHandler(tasks, CommandKind.Demolish));
            router.Register(CommandKind.ToggleGate, new StructureCommandHandler(tasks, CommandKind.ToggleGate));
            world.AddSystem(router);
            world.AddSystem(tasks);
            world.AddSystem(goods);
            world.AddSystem(structures);
            red = new CommandSender(router, Red, 1);
        }

        private Entity Place(string definitionId, SeatId owner, Cell cell)
        {
            catalog.TryGet(definitionId, out EntityDefinition definition);
            EntityKind kind = definition.NodeResource != null ? EntityKind.ResourceNode : definition.MoveSpeed > 0f ? EntityKind.Unit : EntityKind.Building;
            Entity entity = world.Spawn(kind, definitionId, owner, map.CenterOf(cell));
            goods.Attach(entity, definition);
            if (definition.Blocks)
            {
                List<Cell> footprint = Structures.FootprintAt(definition, cell);
                Assert.That(map.TryOccupy(footprint, entity.Id, definition.Destructible), Is.True);
                structures.AttachBuilt(entity, definition, footprint);
            }
            expectedWood += definition.NodeAmount;
            world.Commit();
            return entity;
        }

        private Entity StockedDepot(Cell cell, int wood)
        {
            Entity depot = Place("depot", Red, cell);
            Entity tree = Place("tree", SeatId.None, new Cell(14, 10));
            Entity porter = Place("survivor", Red, new Cell(13, 10));
            int moved = 0;
            while (moved < wood)
            {
                int batch = System.Math.Min(5, wood - moved);
                Assert.That(goods.Gather(tree.Id, porter.Id, batch, null, null), Is.EqualTo(batch));
                Assert.That(goods.Transfer(porter.Id, depot.Id, Wood, batch, null, null), Is.EqualTo(batch));
                moved += batch;
            }
            world.Despawn(tree.Id, "test");
            world.Despawn(porter.Id, "test");
            Run(1);
            return depot;
        }

        private void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.Step();
                log.AddRange(world.DrainEvents());
                Assert.That(goods.TotalOf(Wood) + goods.ConsumedTotal, Is.EqualTo(expectedWood), $"wood appeared or vanished at tick {world.Tick}");
                Assert.That(world.IsFaulted, Is.False);
            }
        }

        private CommandResolved LastAnswer() => log.OfType<CommandResolved>().Last();
        private TaskStateChanged LastTaskEvent(Entity actor) => log.OfType<TaskStateChanged>().Last(e => e.Actor == actor.Id);
        /// <summary>Open camp ground beside the depot, which itself blocks the fixture's camp cell.</summary>
        private static readonly Cell InsideCamp = new Cell(8, 4);

        private Entity SiteEntity() => world.Entities.First(e => e.Kind == EntityKind.Building && (e.DefinitionId == "wall" || e.DefinitionId == "gate"));

        [Test]
        public void ABuilderFetchesTheMaterialsWorksAndTheWallBlocksTheEntrance()
        {
            Entity depot = StockedDepot(FixtureMaps.CampGround, 10);
            long consumedBefore = goods.ConsumedTotal;
            Entity builder = Place("survivor", Red, new Cell(8, 5));
            Assert.That(GridPathfinder.FindPath(map, new Cell(14, 5), InsideCamp, new PathOptions()).Cells, Does.Contain(FixtureMaps.MainEntrance));

            red.Send(CommandKind.Build, new[] { builder.Id }, map.CenterOf(FixtureMaps.MainEntrance), argument: WallIndex);
            Run(1);
            Assert.That(LastAnswer().Accepted, Is.True);
            Entity site = SiteEntity();
            Assert.That(structures.TryGetSite(site.Id, out BuildSite pending), Is.True);
            Assert.That(map.IsWalkable(FixtureMaps.MainEntrance), Is.False, "the site takes its cells the moment it is accepted");
            Assert.That(map.IsDestructibleBlocker(FixtureMaps.MainEntrance), Is.True);
            Assert.That(log.OfType<PassabilityChanged>().Single(p => p.Cause == site.Id).NowBlocked, Is.True);

            Run(400);
            Assert.That(structures.TryGetSite(site.Id, out _), Is.False, "built");
            Assert.That(world.IsAlive(site.Id), Is.True);
            Assert.That(log.OfType<BuildingCompleted>().Single().Building, Is.EqualTo(site.Id));
            Assert.That(goods.TryGetContainer(depot.Id, out Container store) && store.AmountOf(Wood) == 6, Is.True, "four logs left the depot");
            Assert.That(goods.ConsumedTotal - consumedBefore, Is.EqualTo(4), "and became the wall");
            Assert.That(vitals.TryGet(site.Id, out int hp, out int max) && hp == 30 && max == 30, Is.True);
            Assert.That(LastTaskEvent(builder).Reason, Is.EqualTo(TaskReason.Built));
            Assert.That(goods.LiveReservationCount, Is.EqualTo(0));
            PathResult around = GridPathfinder.FindPath(map, new Cell(14, 5), InsideCamp, new PathOptions());
            Assert.That(around.Cells, Does.Contain(FixtureMaps.DetourGate), "the way in is now the far gate");
        }

        [Test]
        public void ThereIsNoFreeBuildingAndNoWorkWithoutMaterials()
        {
            Place("depot", Red, FixtureMaps.CampGround);
            Entity builder = Place("survivor", Red, new Cell(8, 5));
            red.Send(CommandKind.Build, new[] { builder.Id }, map.CenterOf(new Cell(9, 5)), argument: WallIndex);
            Run(60);

            Entity site = SiteEntity();
            Assert.That(structures.TryGetSite(site.Id, out BuildSite pending), Is.True);
            Assert.That(pending.Work, Is.EqualTo(0f));
            Assert.That(tasks.CurrentOf(builder.Id).State, Is.EqualTo(TaskState.Blocked));
            Assert.That(tasks.CurrentOf(builder.Id).Reason, Is.EqualTo(TaskReason.NoMaterialsAvailable));
            Assert.That(structures.Work(site.Id, 100f), Is.False, "even a direct call cannot build without materials");
        }

        [Test]
        public void TwoBuildersShareOneSiteWithoutFetchingTheSameLogTwice()
        {
            Entity depot = StockedDepot(FixtureMaps.CampGround, 4);
            Entity a = Place("survivor", Red, new Cell(8, 5)), b = Place("survivor", Blue, new Cell(9, 6));
            red.Send(CommandKind.Build, new[] { a.Id }, map.CenterOf(new Cell(10, 4)), argument: WallIndex);
            Run(1);
            Entity site = SiteEntity();
            var blue = new CommandSender(router, Blue, 1);
            // The ally lends a hand on the same site through a fresh Build order on the same cell? No: a site exists there. Assign directly.
            tasks.Assign(b.Id, new BuildTask(site.Id), CommandMode.Replace);
            Run(300);

            Assert.That(structures.TryGetSite(site.Id, out _), Is.False);
            Assert.That(goods.TryGetContainer(depot.Id, out Container store) && store.Total == 0, Is.True, "exactly four logs were taken, none stranded in a pack");
            Assert.That(goods.TryGetContainer(a.Id, out Container packA) && packA.Total == 0, Is.True);
            Assert.That(goods.TryGetContainer(b.Id, out Container packB) && packB.Total == 0, Is.True);
        }

        [Test]
        public void PlacementIsRefusedOnACliffOnAUnitOnAnotherBuildingAndWhereNobodyCouldStand()
        {
            Place("depot", Red, FixtureMaps.CampGround);
            Entity builder = Place("survivor", Red, new Cell(8, 5));
            Entity bystander = Place("survivor", Blue, new Cell(10, 6));
            catalog.TryGet("wall", out EntityDefinition wall);

            Assert.That(structures.CheckPlacement(wall, new Cell(3, 5), out _), Is.EqualTo(CommandRejection.SiteBlocked), "cliff");
            Assert.That(structures.CheckPlacement(wall, new Cell(10, 6), out _), Is.EqualTo(CommandRejection.SiteBlocked), "a unit stands there");
            Assert.That(structures.CheckPlacement(wall, FixtureMaps.CampGround, out _), Is.EqualTo(CommandRejection.SiteBlocked), "the depot stands there");
            Assert.That(structures.CheckPlacement(wall, FixtureMaps.ResourceSpot, out _), Is.EqualTo(CommandRejection.SiteBlocked), "walkable but not buildable ground");
            Assert.That(structures.CheckPlacement(wall, new Cell(10, 4), out _), Is.EqualTo(CommandRejection.None));

            red.Send(CommandKind.Build, new[] { builder.Id }, map.CenterOf(new Cell(10, 6)), argument: WallIndex);
            red.Send(CommandKind.Build, new[] { builder.Id }, map.CenterOf(new Cell(10, 4)), argument: 999);
            Run(1);
            Assert.That(log.OfType<CommandResolved>().Select(r => r.Rejection), Is.EqualTo(new[] { CommandRejection.SiteBlocked, CommandRejection.NotBuildable }));
            Assert.That(structures.SiteCount, Is.EqualTo(0));
        }

        [Test]
        public void DemolishingASiteDropsItsDeliveredMaterialsAndFreesItsCells()
        {
            StockedDepot(FixtureMaps.CampGround, 10);
            Entity builder = Place("survivor", Red, new Cell(8, 5));
            red.Send(CommandKind.Build, new[] { builder.Id }, map.CenterOf(FixtureMaps.MainEntrance), argument: WallIndex);
            Run(1);
            Entity site = SiteEntity();
            for (int i = 0; i < 300 && !(goods.TryGetContainer(site.Id, out Container m) && m.Total > 0); i++) Run(1);
            goods.TryGetContainer(site.Id, out Container delivered);
            Assert.That(delivered.Total, Is.GreaterThan(0));
            int onSite = delivered.Total;

            red.Send(CommandKind.Demolish, new[] { builder.Id }, targetEntity: site.Id);
            Run(2);

            Assert.That(world.IsAlive(site.Id), Is.False);
            Assert.That(map.IsWalkable(FixtureMaps.MainEntrance), Is.True, "the cells are given back");
            GoodsDropped drop = log.OfType<GoodsDropped>().Single(d => d.From == site.Id);
            world.TryGet(drop.Pile, out Entity pile);
            Assert.That(goods.TryGetContainer(pile.Id, out Container ground) && ground.AmountOf(Wood) == onSite, Is.True, "delivered materials fall to the ground, not into the void");
            Assert.That(LastTaskEvent(builder).Reason, Is.EqualTo(TaskReason.SiteGone));
            Assert.That(goods.LiveReservationCount, Is.EqualTo(0));
        }

        [Test]
        public void AnEnemyCannotDemolishYourWallAndABuiltWallGivesNothingBack()
        {
            StockedDepot(FixtureMaps.CampGround, 10);
            Entity builder = Place("survivor", Red, new Cell(8, 5));
            Entity raider = Place("survivor", Raiders, new Cell(14, 1));
            red.Send(CommandKind.Build, new[] { builder.Id }, map.CenterOf(new Cell(10, 4)), argument: WallIndex);
            Run(400);
            Entity wall = SiteEntity();
            Assert.That(structures.TryGetSite(wall.Id, out _), Is.False);

            new CommandSender(router, Raiders, 1).Send(CommandKind.Demolish, new[] { raider.Id }, targetEntity: wall.Id);
            Run(1);
            Assert.That(LastAnswer().Rejection, Is.EqualTo(CommandRejection.NotAllowedOnTarget));

            red.Send(CommandKind.Demolish, new[] { builder.Id }, targetEntity: wall.Id);
            Run(2);
            Assert.That(world.IsAlive(wall.Id), Is.False);
            Assert.That(log.OfType<GoodsDropped>().Any(d => d.From == wall.Id), Is.False, "built-in materials are gone for good");
            Assert.That(map.IsWalkable(new Cell(10, 4)), Is.True);
        }

        [Test]
        public void AGateOpensForYourUnitsClosesBehindThemAndRefusesToCloseOnOne()
        {
            StockedDepot(FixtureMaps.CampGround, 10);
            Entity builder = Place("survivor", Red, new Cell(8, 5));
            Entity walker = Place("survivor", Red, new Cell(9, 7));
            red.Send(CommandKind.Build, new[] { builder.Id }, map.CenterOf(FixtureMaps.MainEntrance), argument: GateIndex);
            Run(400);
            Entity gate = SiteEntity();
            Assert.That(structures.TryGetSite(gate.Id, out _), Is.False);
            Assert.That(map.IsWalkable(FixtureMaps.MainEntrance), Is.False, "a new gate is closed");

            red.Send(CommandKind.ToggleGate, new[] { builder.Id }, targetEntity: gate.Id);
            Run(1);
            Assert.That(structures.IsGateOpen(gate.Id), Is.True);
            Assert.That(map.IsWalkable(FixtureMaps.MainEntrance), Is.True);
            Assert.That(log.OfType<PassabilityChanged>().Last().NowBlocked, Is.False);

            red.Send(CommandKind.Move, new[] { walker.Id }, map.CenterOf(FixtureMaps.MainEntrance));
            for (int i = 0; i < 200 && map.CellAt(walker.Position) != FixtureMaps.MainEntrance; i++) Run(1);
            Assert.That(map.CellAt(walker.Position), Is.EqualTo(FixtureMaps.MainEntrance));
            red.Send(CommandKind.ToggleGate, new[] { builder.Id }, targetEntity: gate.Id);
            Run(1);
            Assert.That(LastAnswer().Rejection, Is.EqualTo(CommandRejection.SiteBlocked), "not on a unit's head");
            Assert.That(structures.IsGateOpen(gate.Id), Is.True);

            red.Send(CommandKind.Move, new[] { walker.Id }, map.CenterOf(new Cell(14, 5)));
            Run(30);
            red.Send(CommandKind.ToggleGate, new[] { builder.Id }, targetEntity: gate.Id);
            Run(1);
            Assert.That(structures.IsGateOpen(gate.Id), Is.False);
            Assert.That(map.IsWalkable(FixtureMaps.MainEntrance), Is.False);
            Assert.That(new CommandSender(router, Raiders, 1).Send(CommandKind.ToggleGate, new[] { Place("survivor", Raiders, new Cell(14, 1)).Id }, targetEntity: gate.Id), Is.EqualTo(SubmitOutcome.Queued));
            Run(1);
            Assert.That(LastAnswer().Rejection, Is.EqualTo(CommandRejection.NotAllowedOnTarget));
        }

        [Test]
        public void AWalkerAlreadyOnItsWayReroutesWhenTheGateClosesInFrontOfIt()
        {
            StockedDepot(FixtureMaps.CampGround, 10);
            Entity builder = Place("survivor", Red, new Cell(8, 5));
            Entity walker = Place("survivor", Red, new Cell(14, 5));
            red.Send(CommandKind.Build, new[] { builder.Id }, map.CenterOf(FixtureMaps.MainEntrance), argument: GateIndex);
            Run(400);
            Entity gate = SiteEntity();
            red.Send(CommandKind.ToggleGate, new[] { builder.Id }, targetEntity: gate.Id);
            Run(1);

            red.Send(CommandKind.Move, new[] { walker.Id }, map.CenterOf(InsideCamp));
            Run(3);
            red.Send(CommandKind.ToggleGate, new[] { builder.Id }, targetEntity: gate.Id);
            var visited = new HashSet<Cell>();
            for (int i = 0; i < 400 && tasks.CurrentOf(walker.Id) != null; i++)
            {
                Run(1);
                Assert.That(map.CellAt(walker.Position), Is.Not.EqualTo(FixtureMaps.MainEntrance));
                visited.Add(map.CellAt(walker.Position));
            }
            Assert.That(visited, Does.Contain(FixtureMaps.DetourGate));
            Assert.That(map.CellAt(walker.Position), Is.EqualTo(InsideCamp));
        }

        [Test]
        public void ADestroyedWallGivesItsCellsBackAndAGatheredOutTreeFalls()
        {
            StockedDepot(FixtureMaps.CampGround, 10);
            Entity builder = Place("survivor", Red, new Cell(8, 5));
            red.Send(CommandKind.Build, new[] { builder.Id }, map.CenterOf(FixtureMaps.MainEntrance), argument: WallIndex);
            Run(400);
            Entity wall = SiteEntity();

            Assert.That(vitals.Damage(wall.Id, 12, "bitten"), Is.EqualTo(12));
            Assert.That(vitals.TryGet(wall.Id, out int hp, out _) && hp == 18, Is.True);
            Assert.That(log.Count(e => e is HealthChanged), Is.EqualTo(0), "not committed yet");
            Assert.That(vitals.Damage(wall.Id, 100, "bitten"), Is.EqualTo(18), "never more than it had");
            Run(1);
            Assert.That(world.IsAlive(wall.Id), Is.False);
            Assert.That(map.IsWalkable(FixtureMaps.MainEntrance), Is.True);
            Assert.That(log.OfType<PassabilityChanged>().Last().NowBlocked, Is.False);

            Entity tree = Place("tree", SeatId.None, new Cell(13, 9));
            Entity lumberjack = Place("survivor", Red, new Cell(12, 9));
            Assert.That(map.IsWalkable(new Cell(13, 9)), Is.False);
            red.Send(CommandKind.Gather, new[] { lumberjack.Id }, targetEntity: tree.Id);
            Run(600);
            Assert.That(world.IsAlive(tree.Id), Is.False, "felled");
            Assert.That(map.IsWalkable(new Cell(13, 9)), Is.True, "and the grove opened");
        }
    }
}
