using System.Linq;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class KnowledgeTests
    {
        private static readonly SeatId Red = new SeatId(1), Blue = new SeatId(2), Dinos = new SeatId(8);

        private World world;
        private GridMap map;
        private DefinitionCatalog catalog;
        private SeatRegistry seats;
        private Knowledge knowledge;
        private bool night;

        [SetUp]
        public void SetUp()
        {
            world = new World(new SimConfig(10, 8, 1));
            map = FixtureMaps.OpenGrid(30, 30);
            catalog = new DefinitionCatalog();
            catalog.Add(new EntityDefinition("survivor", 4f, sightDay: 11f, sightNight: 3f));
            catalog.Add(new EntityDefinition("campfire", 0f, sightDay: 6f, sightNight: 6f));
            catalog.Add(new EntityDefinition("raptor", 6f, sightDay: 8f, sightNight: 8f));
            catalog.Add(new EntityDefinition("blind", 4f));
            seats = new SeatRegistry(world);
            seats.Add(Red, "Red", 1, SeatController.Human);
            seats.Add(Blue, "Blue", 1, SeatController.Human);
            seats.Add(Dinos, "Dinosaurs", 2, SeatController.Computer);
            night = false;
            knowledge = new Knowledge(world, map, catalog, seats, () => night, updateIntervalTicks: 2);
            world.AddSystem(knowledge);
        }

        private Entity Spawn(string id, SeatId owner, Cell cell)
        {
            Entity e = world.Spawn(EntityKind.Unit, id, owner, map.CenterOf(cell));
            world.Commit();
            return e;
        }

        private int Count(int team, Visibility v) => knowledge.CellsOf(team).Count(b => b == (byte)v);

        [Test]
        public void NothingIsKnownUntilSomethingLooksAndSightIsARoundedDisc()
        {
            Assert.That(knowledge.At(Red, new Cell(15, 15)), Is.EqualTo(Visibility.Unexplored));
            Spawn("survivor", Red, new Cell(15, 15));
            world.Step();
            world.Step();

            Assert.That(knowledge.At(Red, new Cell(15, 15)), Is.EqualTo(Visibility.Visible));
            Assert.That(knowledge.At(Red, new Cell(20, 15)), Is.EqualTo(Visibility.Visible), "10 m along the axis is inside 11 m");
            Assert.That(knowledge.At(Red, new Cell(21, 15)), Is.EqualTo(Visibility.Unexplored), "12 m is not");
            Assert.That(knowledge.At(Red, new Cell(19, 19)), Is.EqualTo(Visibility.Unexplored), "the corner of the square is outside the disc");
            Assert.That(Count(1, Visibility.Visible), Is.EqualTo(knowledge.VisibleCountOf(1)));
            Assert.That(knowledge.At(Dinos, new Cell(15, 15)), Is.EqualTo(Visibility.Unexplored), "another team knows nothing of it");
        }

        [Test]
        public void ExploredNeverShrinksVisibleFollowsTheUnit()
        {
            Entity scout = Spawn("survivor", Red, new Cell(5, 5));
            world.Step(); world.Step();
            int exploredThen = Count(1, Visibility.Explored) + Count(1, Visibility.Visible);
            // The scout "walks" far away: the same eyes at a new place.
            world.Despawn(scout.Id, "test");
            Spawn("survivor", Red, new Cell(24, 24));
            world.Step(); world.Step();

            Assert.That(knowledge.At(Red, new Cell(5, 5)), Is.EqualTo(Visibility.Explored), "seen before, not now");
            Assert.That(knowledge.At(Red, new Cell(24, 24)), Is.EqualTo(Visibility.Visible));
            Assert.That(Count(1, Visibility.Explored) + Count(1, Visibility.Visible), Is.GreaterThan(exploredThen));
        }

        [Test]
        public void AlliesShareSightAndNightShrinksIt()
        {
            Spawn("survivor", Blue, new Cell(15, 15));
            world.Step(); world.Step();
            Assert.That(knowledge.At(Red, new Cell(19, 15)), Is.EqualTo(Visibility.Visible), "Red sees through Blue's eyes");

            night = true;
            world.Step(); world.Step();
            Assert.That(knowledge.At(Red, new Cell(19, 15)), Is.EqualTo(Visibility.Explored), "at night the survivor sees 3 m");
            Assert.That(knowledge.At(Red, new Cell(16, 15)), Is.EqualTo(Visibility.Visible));

            Spawn("campfire", Red, new Cell(22, 15));
            world.Step(); world.Step();
            Assert.That(knowledge.At(Red, new Cell(24, 15)), Is.EqualTo(Visibility.Visible), "a campfire lights the same radius day and night");
        }

        [Test]
        public void ASeatSeesItsOwnAndAlliedThingsAlwaysAndEnemiesOnlyInSight()
        {
            Entity mine = Spawn("survivor", Red, new Cell(15, 15));
            Entity allied = Spawn("blind", Blue, new Cell(2, 2));
            Entity nearRaptor = Spawn("raptor", Dinos, new Cell(18, 15));
            Entity farRaptor = Spawn("raptor", Dinos, new Cell(28, 28));
            world.Step(); world.Step();

            Assert.That(knowledge.CanSee(Red, mine), Is.True);
            Assert.That(knowledge.CanSee(Red, allied), Is.True, "an ally's unit in unexplored ground is still known to its friends");
            Assert.That(knowledge.CanSee(Red, nearRaptor), Is.True);
            Assert.That(knowledge.CanSee(Red, farRaptor), Is.False);
            Assert.That(knowledge.CanSee(Dinos, mine), Is.True, "the raptor 6 m away sees the survivor");
            Assert.That(knowledge.CanSee(Dinos, allied), Is.False);
        }

        [Test]
        public void TheRevisionMovesOnlyWhenTheMaskChanges()
        {
            Spawn("survivor", Red, new Cell(15, 15));
            world.Step(); world.Step();
            long after = knowledge.RevisionOf(1);
            world.Step(); world.Step();
            Assert.That(knowledge.RevisionOf(1), Is.EqualTo(after), "nothing moved, nothing changed");
            Assert.That(knowledge.RevisionOf(2), Is.EqualTo(0));
        }
    }
}
