using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class GridMapTests
    {
        private static readonly EntityId Wall = new EntityId(41);
        private static readonly EntityId Depot = new EntityId(42);

        [Test]
        public void OccupyingMarksEveryFootprintCellAndMovesTheVersion()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            long before = map.Version;
            var footprint = new[] { new Cell(5, 4), new Cell(6, 4) };

            Assert.That(map.TryOccupy(footprint, Depot, false), Is.True);

            Assert.That(map.Version, Is.EqualTo(before + 1));
            Assert.That(map.BlockerAt(new Cell(5, 4)), Is.EqualTo(Depot));
            Assert.That(map.BlockerAt(new Cell(6, 4)), Is.EqualTo(Depot));
            Assert.That(map.IsWalkable(new Cell(5, 4)), Is.False);
            Assert.That(map.IsBuildable(new Cell(6, 4)), Is.False);
            Assert.That(map.IsDestructibleBlocker(new Cell(5, 4)), Is.False);
        }

        [Test]
        public void ADestructibleBlockerIsMarkedAsOneAndAnIndestructibleOneIsNot()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            map.TryOccupy(new[] { FixtureMaps.MainEntrance }, Wall, true);
            map.TryOccupy(new[] { new Cell(5, 4) }, Depot, false);

            Assert.That(map.IsDestructibleBlocker(FixtureMaps.MainEntrance), Is.True);
            Assert.That(map.IsDestructibleBlocker(new Cell(5, 4)), Is.False);
            Assert.That(map.IsDestructibleBlocker(new Cell(6, 6)), Is.False);
        }

        [Test]
        public void OccupationIsAllOrNothingWhenOneCellIsUnavailable()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            map.TryOccupy(new[] { new Cell(6, 4) }, Depot, false);
            long after = map.Version;

            // The second cell of each attempt is taken, a cliff and outside the map in turn.
            Assert.That(map.TryOccupy(new[] { new Cell(5, 4), new Cell(6, 4) }, Wall, true), Is.False);
            Assert.That(map.TryOccupy(new[] { new Cell(5, 4), new Cell(3, 4) }, Wall, true), Is.False);
            Assert.That(map.TryOccupy(new[] { new Cell(5, 4), new Cell(99, 99) }, Wall, true), Is.False);

            Assert.That(map.Version, Is.EqualTo(after));
            Assert.That(map.BlockerAt(new Cell(5, 4)).IsNone, Is.True);
            Assert.That(map.FootprintOf(Wall), Is.Empty);
        }

        [Test]
        public void AnEmptyFootprintAndASecondClaimByTheSameBlockerAreRefused()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            map.TryOccupy(new[] { new Cell(5, 4) }, Depot, false);
            long after = map.Version;

            Assert.That(map.TryOccupy(new Cell[0], Wall, true), Is.False);
            Assert.That(map.TryOccupy(new[] { new Cell(7, 4) }, Depot, false), Is.False);
            Assert.That(map.Version, Is.EqualTo(after));
            Assert.That(map.BlockerAt(new Cell(7, 4)).IsNone, Is.True);
        }

        [Test]
        public void ReleaseFreesTheCellsAndMovesTheVersion()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            map.TryOccupy(new[] { new Cell(5, 4), new Cell(6, 4) }, Depot, false);
            long occupied = map.Version;

            Assert.That(map.Release(Depot), Is.True);

            Assert.That(map.Version, Is.EqualTo(occupied + 1));
            Assert.That(map.IsWalkable(new Cell(5, 4)), Is.True);
            Assert.That(map.IsWalkable(new Cell(6, 4)), Is.True);
            Assert.That(map.FootprintOf(Depot), Is.Empty);
        }

        [Test]
        public void ReleasingSomethingThatHoldsNothingChangesNothing()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            long before = map.Version;

            Assert.That(map.Release(Wall), Is.False);
            Assert.That(map.Version, Is.EqualTo(before));
        }

        [Test]
        public void TerrainDecidesWalkableAndBuildableBeforeAnythingIsBuilt()
        {
            GridMap map = FixtureMaps.CampValleyGrid();

            Assert.That(map.IsWalkable(new Cell(3, 4)), Is.False, "cliff");
            Assert.That(map.IsBuildable(new Cell(3, 4)), Is.False, "cliff");
            Assert.That(map.IsWalkable(FixtureMaps.ResourceSpot), Is.True);
            Assert.That(map.IsBuildable(FixtureMaps.ResourceSpot), Is.False, "the resource spot is walkable ground nobody may build on");
            Assert.That(map.IsWalkable(FixtureMaps.CampGround), Is.True);
            Assert.That(map.IsBuildable(FixtureMaps.CampGround), Is.True);
            Assert.That(map.IsWalkable(new Cell(-1, 5)), Is.False, "outside the map");
            Assert.That(map.BlockerAt(new Cell(-1, 5)).IsNone, Is.True);
        }

        [Test]
        public void CellAtAndCenterOfRoundTripIncludingNegativeAndEdgeCoordinates()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            var cells = new[]
            {
                Cell.Zero,
                new Cell(15, 11),
                new Cell(0, 11),
                new Cell(-1, -1),
                new Cell(-4, 7),
                new Cell(20, -3)
            };

            foreach (Cell cell in cells)
            {
                Assert.That(map.CellAt(map.CenterOf(cell)), Is.EqualTo(cell), $"round trip for {cell}");
            }
        }

        [Test]
        public void CellAtPutsAPositionInTheCellThatContainsItOnBothSidesOfTheOrigin()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            float size = FixtureMaps.CellSize;

            Assert.That(map.CellAt(SimVector2.Zero), Is.EqualTo(Cell.Zero));
            Assert.That(map.CellAt(new SimVector2(size - 0.01f, size - 0.01f)), Is.EqualTo(Cell.Zero), "the far edge still belongs to the same cell");
            Assert.That(map.CellAt(new SimVector2(size, size)), Is.EqualTo(new Cell(1, 1)), "the next cell starts exactly on the boundary");
            Assert.That(map.CellAt(new SimVector2(-0.01f, -0.01f)), Is.EqualTo(new Cell(-1, -1)), "just below the origin is the cell before it, not cell zero");
            Assert.That(map.CellAt(new SimVector2(-size, -size)), Is.EqualTo(new Cell(-1, -1)));
            Assert.That(map.CenterOf(Cell.Zero), Is.EqualTo(new SimVector2(size * 0.5f, size * 0.5f)));
        }

        [Test]
        public void AGridMapRefusesADefinitionWhoseFlagsDoNotCoverIt()
        {
            var broken = new MapDefinition(
                4,
                4,
                FixtureMaps.CellSize,
                new[] { CellFlags.Walkable },
                new CampDefinition[0],
                new RegionDefinition[0]);

            Assert.That(() => new GridMap(broken), Throws.ArgumentException);
        }
    }
}
