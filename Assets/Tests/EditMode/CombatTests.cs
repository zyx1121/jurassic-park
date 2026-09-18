using System.Collections.Generic;
using System.Linq;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class CombatTests
    {
        private static readonly SeatId Red = new SeatId(1), Dinos = new SeatId(8);
        private const string Wood = "wood";

        private World world;
        private GridMap map;
        private DefinitionCatalog catalog;
        private Logistics goods;
        private Vitals vitals;
        private Structures structures;
        private TaskSystem tasks;
        private CommandRouter router;
        private CommandSender red, dinos;
        private readonly List<SimEvent> log = new List<SimEvent>();

        [SetUp]
        public void SetUp() => Build(breachCost: TaskConfig.DefaultBreachCost, predators: false);

        private void Build(int breachCost, bool predators)
        {
            log.Clear();
            world = new World(new SimConfig(10, 8, 1));
            map = FixtureMaps.CampValleyGrid();
            catalog = new DefinitionCatalog();
            catalog.Add(new EntityDefinition("survivor", 4f, storageCapacity: 5, gatherSecondsPerUnit: 0.2f, maxHealth: 40));
            catalog.Add(new EntityDefinition("raptor", 6f, maxHealth: 80, attackDamage: 10, attackSeconds: 0.5f, perceptionRadius: 40f, canBreach: true));
            catalog.Add(new EntityDefinition("depot", 0f, storageCapacity: 100, isDepot: true, blocks: true, maxHealth: 50));
            catalog.Add(new EntityDefinition("tree", 0f, nodeResource: Wood, nodeAmount: 12, blocks: true, destructible: false));
            catalog.Add(new EntityDefinition("pile", 0f));
            catalog.Add(new EntityDefinition("wall", 0f, blocks: true, destructible: true, maxHealth: 30, buildCost: new Dictionary<string, int> { [Wood] = 4 }, buildWorkSeconds: 1f));
            goods = new Logistics(world, new LogisticsConfig(20, "pile"));
            vitals = new Vitals(world);
            var seats = new SeatRegistry(world);
            seats.Add(Red, "Red", 1, SeatController.Human);
            seats.Add(Dinos, "Dinosaurs", 2, SeatController.Computer);
            structures = new Structures(world, map, goods, vitals, seats);
            var context = new TaskContext(world, map, catalog, new TaskConfig(5, 3, 2, new PathOptions(), new PathOptions(allowBreach: true, breachCost: breachCost)), goods, seats, structures, vitals);
            tasks = new TaskSystem(context);
            router = new CommandRouter(seats, new CommandRouterConfig(8, 16, 8));
            router.Register(CommandKind.Move, new MoveCommandHandler(tasks));
            router.Register(CommandKind.Attack, new AttackCommandHandler(tasks));
            world.AddSystem(router);
            world.AddSystem(tasks);
            world.AddSystem(goods);
            world.AddSystem(structures);
            if (predators) world.AddSystem(new Predators(context, router, scanIntervalTicks: 5));
            red = new CommandSender(router, Red, 1);
            dinos = new CommandSender(router, Dinos, 1);
        }

        private Entity Place(string definitionId, SeatId owner, Cell cell)
        {
            catalog.TryGet(definitionId, out EntityDefinition definition);
            EntityKind kind = definition.NodeResource != null ? EntityKind.ResourceNode : definition.MoveSpeed > 0f ? EntityKind.Unit : EntityKind.Building;
            Entity entity = world.Spawn(kind, definitionId, owner, map.CenterOf(cell));
            goods.Attach(entity, definition);
            vitals.Attach(entity, definition);
            if (definition.Blocks)
            {
                List<Cell> footprint = Structures.FootprintAt(definition, cell);
                Assert.That(map.TryOccupy(footprint, entity.Id, definition.Destructible), Is.True);
                if (kind == EntityKind.Building) structures.AttachBuilt(entity, definition, footprint); else structures.TrackBlocker(entity.Id);
            }
            world.Commit();
            return entity;
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

        private IEnumerable<EntityId> Victims(Entity attacker) => log.OfType<Attacked>().Where(a => a.Attacker == attacker.Id).Select(a => a.Target).Distinct();

        [Test]
        public void ARaptorOrderedOntoASurvivorWalksInThroughTheOpenEntranceAndKillsIt()
        {
            Entity survivor = Place("survivor", Red, new Cell(8, 5));
            Entity raptor = Place("raptor", Dinos, new Cell(14, 5));
            dinos.Send(CommandKind.Attack, new[] { raptor.Id }, targetEntity: survivor.Id);

            var visited = new HashSet<Cell>();
            for (int i = 0; i < 300 && world.IsAlive(survivor.Id); i++) { Run(1); visited.Add(map.CellAt(raptor.Position)); }
            Run(1); // the task notices the kill on the tick after it

            Assert.That(world.IsAlive(survivor.Id), Is.False);
            Assert.That(visited, Does.Contain(FixtureMaps.MainEntrance));
            Assert.That(log.OfType<Attacked>().Count(a => a.Target == survivor.Id), Is.EqualTo(4), "40 hit points at 10 per bite");
            Assert.That(log.OfType<TaskStateChanged>().Last(e => e.Actor == raptor.Id).Reason, Is.EqualTo(TaskReason.TargetDestroyed));
            Assert.That(tasks.CurrentOf(raptor.Id), Is.Null);
        }

        [Test]
        public void WithEveryWayInWalledTheRaptorBreaksTheWallInItsWayAndOnlyThatOne()
        {
            Entity survivor = Place("survivor", Red, new Cell(8, 5));
            Entity entranceWall = Place("wall", Red, FixtureMaps.MainEntrance);
            Entity gateWall = Place("wall", Red, FixtureMaps.DetourGate);
            Entity uselessWall = Place("wall", Red, new Cell(13, 4));
            vitals.Damage(uselessWall.Id, 29, "test");
            Entity raptor = Place("raptor", Dinos, new Cell(14, 5));
            dinos.Send(CommandKind.Attack, new[] { raptor.Id }, targetEntity: survivor.Id);

            Run(20);
            Assert.That(log.OfType<TaskStateChanged>().Any(e => e.Actor == raptor.Id && e.Reason == TaskReason.Breaching), Is.True);
            for (int i = 0; i < 400 && world.IsAlive(survivor.Id); i++) Run(1);

            Assert.That(world.IsAlive(entranceWall.Id), Is.False, "the wall in the way came down");
            Assert.That(map.IsWalkable(FixtureMaps.MainEntrance), Is.True);
            Assert.That(world.IsAlive(uselessWall.Id), Is.True, "a weak wall that leads nowhere is left alone");
            Assert.That(world.IsAlive(gateWall.Id), Is.True, "only one wall needed to come down");
            Assert.That(Victims(raptor), Is.EqualTo(new[] { entranceWall.Id, survivor.Id }));
            Assert.That(world.IsAlive(survivor.Id), Is.False);
        }

        [Test]
        public void ARaptorWalksAroundWhenTheDetourIsCheaperThanBreakingThrough()
        {
            Build(breachCost: 200, predators: false);
            Entity survivor = Place("survivor", Red, new Cell(8, 5));
            Entity entranceWall = Place("wall", Red, FixtureMaps.MainEntrance);
            Entity raptor = Place("raptor", Dinos, new Cell(14, 5));
            dinos.Send(CommandKind.Attack, new[] { raptor.Id }, targetEntity: survivor.Id);

            var visited = new HashSet<Cell>();
            for (int i = 0; i < 400 && world.IsAlive(survivor.Id); i++) { Run(1); visited.Add(map.CellAt(raptor.Position)); }

            Assert.That(world.IsAlive(entranceWall.Id), Is.True);
            Assert.That(visited, Does.Contain(FixtureMaps.DetourGate));
            Assert.That(Victims(raptor), Is.EqualTo(new[] { survivor.Id }));
        }

        [Test]
        public void ARaptorBreaksThroughWhenTheDetourIsDearerThanTheWall()
        {
            Build(breachCost: 10, predators: false);
            Entity survivor = Place("survivor", Red, new Cell(8, 5));
            Entity entranceWall = Place("wall", Red, FixtureMaps.MainEntrance);
            Entity raptor = Place("raptor", Dinos, new Cell(14, 5));
            dinos.Send(CommandKind.Attack, new[] { raptor.Id }, targetEntity: survivor.Id);
            for (int i = 0; i < 400 && world.IsAlive(survivor.Id); i++) Run(1);

            Assert.That(world.IsAlive(entranceWall.Id), Is.False);
            Assert.That(Victims(raptor), Is.EqualTo(new[] { entranceWall.Id, survivor.Id }));
        }

        [Test]
        public void ABlueprintCanBeWreckedButATreeCannot()
        {
            Entity survivor = Place("survivor", Red, new Cell(8, 5));
            catalog.TryGet("wall", out EntityDefinition wall);
            Entity site = structures.PlaceSite(wall, Red, FixtureMaps.MainEntrance);
            Place("wall", Red, FixtureMaps.DetourGate);
            world.Commit();
            Assert.That(vitals.TryGet(site.Id, out int hp, out _) && hp == 30 / Structures.SiteHealthDivisor, Is.True, "a site has a fraction of the finished wall's hit points");
            Entity raptor = Place("raptor", Dinos, new Cell(14, 5));
            dinos.Send(CommandKind.Attack, new[] { raptor.Id }, targetEntity: survivor.Id);
            for (int i = 0; i < 400 && world.IsAlive(survivor.Id); i++) Run(1);

            Assert.That(world.IsAlive(site.Id), Is.False);
            Assert.That(world.IsAlive(survivor.Id), Is.False);

            Build(breachCost: 10, predators: false);
            Entity prey = Place("survivor", Red, new Cell(1, 10));
            foreach (Cell c in new[] { new Cell(1, 8), new Cell(2, 8), new Cell(3, 9), new Cell(3, 10) }) Place("tree", SeatId.None, c);
            Entity hunter = Place("raptor", Dinos, new Cell(14, 1));
            dinos.Send(CommandKind.Attack, new[] { hunter.Id }, targetEntity: prey.Id);
            Run(60);
            Assert.That(world.IsAlive(prey.Id), Is.True);
            Assert.That(log.OfType<Attacked>().Any(a => a.Attacker == hunter.Id), Is.False, "trees are not breakable and are never bitten");
            Assert.That(tasks.CurrentOf(hunter.Id).State, Is.EqualTo(TaskState.Blocked));
            Assert.That(tasks.CurrentOf(hunter.Id).Reason, Is.EqualTo(TaskReason.NoRoute));
        }

        [Test]
        public void AnIdleRaptorThatSeesASurvivorAttacksOnItsOwnThroughTheCommandDoor()
        {
            Build(breachCost: TaskConfig.DefaultBreachCost, predators: true);
            Entity survivor = Place("survivor", Red, new Cell(8, 5));
            Entity raptor = Place("raptor", Dinos, new Cell(14, 5));
            Run(6);

            CommandResolved order = log.OfType<CommandResolved>().Single(r => r.Seat == Dinos);
            Assert.That(order.Accepted, Is.True);
            Assert.That(tasks.CurrentOf(raptor.Id), Is.InstanceOf<AttackTask>());
            Assert.That(((AttackTask)tasks.CurrentOf(raptor.Id)).Target, Is.EqualTo(survivor.Id));
            for (int i = 0; i < 300 && world.IsAlive(survivor.Id); i++) Run(1);
            Assert.That(world.IsAlive(survivor.Id), Is.False);
            Assert.That(log.OfType<CommandResolved>().Count(r => r.Seat == Dinos), Is.EqualTo(1), "one order for the hunt, none while busy");
        }

        [Test]
        public void AttackOrdersAreRefusedOnFriendsTreesAndByThingsThatCannotFight()
        {
            Entity survivor = Place("survivor", Red, new Cell(8, 5));
            Entity other = Place("survivor", Red, new Cell(9, 5));
            Entity tree = Place("tree", SeatId.None, new Cell(13, 9));
            Entity raptor = Place("raptor", Dinos, new Cell(14, 5));
            Entity packmate = Place("raptor", Dinos, new Cell(14, 6));

            red.Send(CommandKind.Attack, new[] { survivor.Id }, targetEntity: other.Id);
            dinos.Send(CommandKind.Attack, new[] { raptor.Id }, targetEntity: tree.Id);
            dinos.Send(CommandKind.Attack, new[] { raptor.Id }, targetEntity: packmate.Id);
            red.Send(CommandKind.Attack, new[] { survivor.Id }, targetEntity: raptor.Id);
            Run(1);

            Assert.That(log.OfType<CommandResolved>().Select(r => r.Rejection), Is.EqualTo(new[]
            {
                CommandRejection.NotAllowedOnTarget, CommandRejection.InvalidTarget, CommandRejection.NotAllowedOnTarget, CommandRejection.ActorsLackAbility,
            }));
        }

        [Test]
        public void AnAttackerWhoseTargetWalksAwayFollowsIt()
        {
            Entity survivor = Place("survivor", Red, new Cell(12, 1));
            Entity raptor = Place("raptor", Dinos, new Cell(14, 1));
            dinos.Send(CommandKind.Attack, new[] { raptor.Id }, targetEntity: survivor.Id);
            red.Send(CommandKind.Move, new[] { survivor.Id }, map.CenterOf(new Cell(1, 10)));
            for (int i = 0; i < 400 && world.IsAlive(survivor.Id); i++) Run(1);

            Assert.That(world.IsAlive(survivor.Id), Is.False, "a raptor is faster than a survivor");
        }
    }
}
