using System.Collections.Generic;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class GridPathfinderTests
    {
        private static readonly EntityId EntranceWall = new EntityId(101);
        private static readonly EntityId DetourWall = new EntityId(102);
        private static readonly EntityId FarWall = new EntityId(103);

        private const int BreachCost = 30;

        private static readonly string[] PocketRows =
        {
            ".....", // y = 4
            ".###.", // y = 3
            ".#.#.", // y = 2
            ".###.", // y = 1
            "....."  // y = 0
        };

        private static readonly Cell Pocket = new Cell(2, 2);

        /// <summary>The route through the walled main entrance, hand counted on the fixture: 14 + 3 * 10 up the east lane, 10 through the gap, 6 * 10 west to the camp ground.</summary>
        private const int ThroughTheEntrance = 114;

        /// <summary>The way round through the far gate, hand counted: 14 + 7 * 10 north, 6 * 10 west, 10 + 10 in through the gate, then 14 + 10 to the camp ground.</summary>
        private const int RoundTheDetour = 188;

        private static PathOptions Walk(int maxExpandedNodes = PathOptions.BudgetFromMapSize) =>
            new PathOptions(false, 0, maxExpandedNodes);

        private static PathOptions Breach(int breachCost = BreachCost) =>
            new PathOptions(true, breachCost);

        private static PathResult Route(GridMap map, PathOptions options) =>
            GridPathfinder.FindPath(map, FixtureMaps.ResourceSpot, FixtureMaps.CampGround, options);

        [Test]
        public void AStraightRouteCostsOneStraightStepPerCell()
        {
            GridMap map = FixtureMaps.OpenGrid(5, 5);

            PathResult path = GridPathfinder.FindPath(map, Cell.Zero, new Cell(3, 0), Walk());

            Assert.That(path.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(path.Cost, Is.EqualTo(3 * PathOptions.DefaultStraightCost));
            Assert.That(path.Cells, Has.Count.EqualTo(4));
            Assert.That(path.Cells[0], Is.EqualTo(Cell.Zero));
            Assert.That(path.Cells[3], Is.EqualTo(new Cell(3, 0)));
            Assert.That(path.Breached, Is.Empty);
            Assert.That(path.MapVersion, Is.EqualTo(map.Version));
        }

        [Test]
        public void ADiagonalRouteCostsTheDiagonalPriceAndMixesWithStraightSteps()
        {
            GridMap map = FixtureMaps.OpenGrid(6, 6);

            PathResult diagonal = GridPathfinder.FindPath(map, Cell.Zero, new Cell(3, 3), Walk());
            PathResult mixed = GridPathfinder.FindPath(map, Cell.Zero, new Cell(4, 2), Walk());

            Assert.That(diagonal.Cost, Is.EqualTo(3 * PathOptions.DefaultDiagonalCost));
            Assert.That(diagonal.Cells, Has.Count.EqualTo(4));
            Assert.That(mixed.Cost, Is.EqualTo(2 * PathOptions.DefaultDiagonalCost + 2 * PathOptions.DefaultStraightCost));
            Assert.That(mixed.Cells, Has.Count.EqualTo(5));
        }

        [Test]
        public void ADiagonalGapBetweenTwoBlockedCellsIsNotAShortcut()
        {
            GridMap oneCorner = FixtureMaps.GridFromRows(new[] { "...", "...", ".#." });
            GridMap bothCorners = FixtureMaps.GridFromRows(new[] { "...", "#..", ".#." });

            PathResult around = GridPathfinder.FindPath(oneCorner, Cell.Zero, new Cell(1, 1), Walk());
            PathResult squeeze = GridPathfinder.FindPath(bothCorners, Cell.Zero, new Cell(1, 1), Walk());

            Assert.That(around.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(around.Cost, Is.EqualTo(2 * PathOptions.DefaultStraightCost), "cutting the corner would have cost one diagonal step");
            Assert.That(around.Cells, Has.No.Member(new Cell(1, 0)));
            Assert.That(squeeze.Status, Is.EqualTo(PathStatus.NoRoute), "a diagonal between two blocked cells is not a gap");
        }

        [Test]
        public void TheSameQueryTwiceGivesTheSameCells()
        {
            MapDefinition definition = FixtureMaps.CampValley();
            var first = new GridMap(definition);
            var second = new GridMap(definition);

            PathResult once = Route(first, Walk());
            PathResult twice = Route(first, Walk());
            PathResult onAFreshMap = Route(second, Walk());

            Assert.That(once.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(twice.Cells, Is.EqualTo(once.Cells));
            Assert.That(onAFreshMap.Cells, Is.EqualTo(once.Cells));
            Assert.That(twice.Cost, Is.EqualTo(once.Cost));
        }

        [Test]
        public void WithoutBreachAWalledEntranceForcesTheLongWayRound()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            PathResult direct = Route(map, Walk());
            map.TryOccupy(new[] { FixtureMaps.MainEntrance }, EntranceWall, true);

            PathResult detour = Route(map, Walk());

            Assert.That(detour.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(detour.Cells, Has.Member(FixtureMaps.DetourGate));
            Assert.That(detour.Cells, Has.No.Member(FixtureMaps.MainEntrance));
            Assert.That(detour.Cost, Is.GreaterThan(direct.Cost));
            Assert.That(detour.Breached, Is.Empty);
            Assert.That(detour.MapVersion, Is.EqualTo(map.Version));
        }

        [Test]
        public void WithoutBreachAWalledEntranceAndWalledDetourLeaveNoRoute()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            map.TryOccupy(new[] { FixtureMaps.MainEntrance }, EntranceWall, true);
            map.TryOccupy(new[] { FixtureMaps.DetourGate }, DetourWall, true);

            PathResult path = Route(map, Walk());

            Assert.That(path.Status, Is.EqualTo(PathStatus.NoRoute));
            Assert.That(path.Cells, Is.Empty);
        }

        [Test]
        public void WithBreachTheRouteGoesThroughExactlyTheWallThatOpensIt()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            map.TryOccupy(new[] { FixtureMaps.MainEntrance }, EntranceWall, true);
            map.TryOccupy(new[] { FixtureMaps.DetourGate }, DetourWall, true);

            PathResult path = Route(map, Breach());

            Assert.That(path.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(path.Cells, Has.Member(FixtureMaps.MainEntrance));
            Assert.That(path.Breached, Is.EqualTo(new[] { EntranceWall }), "the short way in is the cheaper breach");
        }

        [Test]
        public void TheOpenRouteIsUsedUntilTheBreachIsActuallyCheaper()
        {
            // PLAN section 6, P4: the choice between walking round and breaking through has to be explainable, so it
            // is the arithmetic that decides it. Through the walled entrance is 114 plus the breach, round is 188.
            GridMap map = FixtureMaps.CampValleyGrid();
            map.TryOccupy(new[] { FixtureMaps.MainEntrance }, EntranceWall, true);

            PathResult round = Route(map, Breach(200));
            PathResult through = Route(map, Breach(50));

            Assert.That(round.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(round.Cost, Is.EqualTo(RoundTheDetour));
            Assert.That(round.Cells, Has.Member(FixtureMaps.DetourGate));
            Assert.That(round.Breached, Is.Empty, "a breach dearer than the way round is not taken");

            Assert.That(through.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(through.Cost, Is.EqualTo(ThroughTheEntrance + 50));
            Assert.That(through.Cells, Has.Member(FixtureMaps.MainEntrance));
            Assert.That(through.Breached, Is.EqualTo(new[] { EntranceWall }));
        }

        [Test]
        public void AWeakWallThatIsNotOnTheWayIsNeverBreached()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            map.TryOccupy(new[] { FixtureMaps.MainEntrance }, EntranceWall, true);
            map.TryOccupy(new[] { FixtureMaps.DetourGate }, DetourWall, true);
            var offRoute = new Cell(2, 5);
            map.TryOccupy(new[] { offRoute }, FarWall, true);

            PathResult path = Route(map, Breach());

            Assert.That(map.IsDestructibleBlocker(offRoute), Is.True, "the far wall really is breakable");
            Assert.That(path.Breached, Is.EqualTo(new[] { EntranceWall }));
            Assert.That(path.Cells, Has.No.Member(offRoute));
        }

        [Test]
        public void AnIndestructibleBlockerIsNeverBreached()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            map.TryOccupy(new[] { FixtureMaps.MainEntrance }, EntranceWall, false);
            map.TryOccupy(new[] { FixtureMaps.DetourGate }, DetourWall, true);

            PathResult path = Route(map, Breach());

            Assert.That(path.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(path.Cells, Has.Member(FixtureMaps.DetourGate));
            Assert.That(path.Cells, Has.No.Member(FixtureMaps.MainEntrance));
            Assert.That(path.Breached, Is.EqualTo(new[] { DetourWall }));
        }

        [Test]
        public void TwoIndestructibleBlockersSealTheCampEvenWithBreachAllowed()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            map.TryOccupy(new[] { FixtureMaps.MainEntrance }, EntranceWall, false);
            map.TryOccupy(new[] { FixtureMaps.DetourGate }, DetourWall, false);

            PathResult path = Route(map, Breach());

            Assert.That(path.Status, Is.EqualTo(PathStatus.NoRoute));
            Assert.That(path.Breached, Is.Empty);
        }

        [Test]
        public void CliffsAreNeverPassableEvenWhenABreachIsFree()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            map.TryOccupy(new[] { FixtureMaps.MainEntrance }, EntranceWall, true);

            PathResult path = Route(map, Breach(0));

            Assert.That(path.Status, Is.EqualTo(PathStatus.Found));
            foreach (Cell cell in path.Cells)
            {
                Assert.That(map.IsStaticWalkable(cell), Is.True, $"{cell} is terrain nothing can cross");
            }
        }

        [Test]
        public void ReleasingTheWallReopensTheShortRoute()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            PathResult before = Route(map, Walk());
            map.TryOccupy(new[] { FixtureMaps.MainEntrance }, EntranceWall, true);
            PathResult walled = Route(map, Walk());

            Assert.That(map.Release(EntranceWall), Is.True);
            PathResult reopened = Route(map, Walk());

            Assert.That(walled.Cost, Is.GreaterThan(before.Cost));
            Assert.That(reopened.Cells, Is.EqualTo(before.Cells));
            Assert.That(reopened.Cells, Has.Member(FixtureMaps.MainEntrance));
            Assert.That(reopened.MapVersion, Is.GreaterThan(before.MapVersion), "the route is the same but it was computed on a later version");
        }

        [Test]
        public void EndpointsOutsideTheMapOrOnATerrainWallAreInvalid()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            var cliff = new Cell(3, 4);

            Assert.That(GridPathfinder.FindPath(map, new Cell(-1, 5), FixtureMaps.CampGround, Walk()).Status, Is.EqualTo(PathStatus.InvalidEndpoint));
            Assert.That(GridPathfinder.FindPath(map, FixtureMaps.CampGround, new Cell(16, 5), Walk()).Status, Is.EqualTo(PathStatus.InvalidEndpoint));
            Assert.That(GridPathfinder.FindPath(map, cliff, FixtureMaps.CampGround, Walk()).Status, Is.EqualTo(PathStatus.InvalidEndpoint));
            Assert.That(GridPathfinder.FindPath(map, FixtureMaps.CampGround, cliff, Walk()).Status, Is.EqualTo(PathStatus.InvalidEndpoint));
        }

        [Test]
        public void AStartThatIsAlreadyTheGoalIsAZeroCostPath()
        {
            GridMap map = FixtureMaps.CampValleyGrid();

            PathResult path = GridPathfinder.FindPath(map, FixtureMaps.CampGround, FixtureMaps.CampGround, Walk());

            Assert.That(path.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(path.Cells, Is.EqualTo(new[] { FixtureMaps.CampGround }));
            Assert.That(path.Cost, Is.Zero);
        }

        [Test]
        public void ABudgetCutOffIsReportedApartFromAProvenLackOfRoute()
        {
            GridMap open = FixtureMaps.OpenGrid(20, 20);
            GridMap pocket = FixtureMaps.GridFromRows(PocketRows);

            PathResult starved = GridPathfinder.FindPath(open, Cell.Zero, new Cell(19, 19), Walk(1));
            PathResult searchedOut = GridPathfinder.FindPath(pocket, Cell.Zero, Pocket, Walk());

            Assert.That(starved.Status, Is.EqualTo(PathStatus.BudgetExceeded));
            Assert.That(searchedOut.Status, Is.EqualTo(PathStatus.NoRoute));
            Assert.That(GridPathfinder.FindPath(open, Cell.Zero, new Cell(19, 19), Walk()).Status, Is.EqualTo(PathStatus.Found), "the same query answers once the budget allows it");
        }

        [Test]
        public void ASearchForManyGoalsEndsAtTheCheapestOne()
        {
            GridMap map = FixtureMaps.OpenGrid(9, 9);
            var goals = new[] { new Cell(8, 8), new Cell(2, 0), new Cell(0, 5) };

            PathResult path = GridPathfinder.FindPathToAny(map, Cell.Zero, goals, Walk());

            Assert.That(path.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(path.Cells[path.Cells.Count - 1], Is.EqualTo(new Cell(2, 0)));
            Assert.That(path.Cost, Is.EqualTo(2 * PathOptions.DefaultStraightCost));
            Assert.That(GridPathfinder.FindPathToAny(map, Cell.Zero, new Cell[0], Walk()).Status, Is.EqualTo(PathStatus.NoRoute), "nowhere to go is not a broken query");
        }

        [Test]
        public void ADefaultBudgetLetsALargeMapBeSearchedOutInsteadOfTimingOut()
        {
            // A fixed budget smaller than the map would report BudgetExceeded forever and NoRoute could never happen.
            var pocket = new Cell(128, 128);
            GridMap map = FixtureMaps.OpenGridWithSealedCell(256, pocket);

            PathResult path = GridPathfinder.FindPath(map, Cell.Zero, pocket, Walk());

            Assert.That(path.Status, Is.EqualTo(PathStatus.NoRoute));
            Assert.That(path.Expanded, Is.LessThanOrEqualTo(map.Width * map.Height));
            Assert.That(GridPathfinder.FindPath(map, Cell.Zero, pocket, Walk(1000)).Status, Is.EqualTo(PathStatus.BudgetExceeded), "an explicit budget still cuts the search off");
        }

        [Test]
        public void EverySideOfAFootprintIsAnsweredByOneSearch()
        {
            // Sixteen candidates sealed behind a cliff ring: one search per candidate would sweep the map sixteen times.
            GridMap map = FixtureMaps.OpenGridWithWalledInBuilding(128, out List<Cell> footprint);

            bool found = GridPathfinder.TryFindApproachCell(map, new Cell(1, 1), footprint, Walk(), out Cell approach, out PathResult path);

            Assert.That(found, Is.False);
            Assert.That(path.Status, Is.EqualTo(PathStatus.NoRoute));
            Assert.That(approach, Is.EqualTo(default(Cell)));
            // Measured here: 16,335 expansions, one sweep of the reachable ground. Asking each candidate on its own
            // cost 16 sweeps, 261,360 expansions, for the same answer.
            Assert.That(path.Expanded, Is.LessThanOrEqualTo(map.Width * map.Height), "the whole question costs at most one sweep of the map");
        }

        [Test]
        public void AnApproachCellIsNextToTheFootprintAndNeverInsideIt()
        {
            GridMap map = FixtureMaps.CampValleyGrid();
            var footprint = new List<Cell> { new Cell(8, 4), new Cell(9, 4), new Cell(8, 5), new Cell(9, 5) };
            map.TryOccupy(footprint, new EntityId(7), false);

            bool found = GridPathfinder.TryFindApproachCell(map, FixtureMaps.ResourceSpot, footprint, Walk(), out Cell approach, out PathResult path);

            Assert.That(found, Is.True);
            Assert.That(footprint, Has.No.Member(approach));
            Assert.That(map.IsWalkable(approach), Is.True);
            Assert.That(IsNextTo(approach, footprint), Is.True);
            Assert.That(path.Status, Is.EqualTo(PathStatus.Found));
            Assert.That(path.Cells[0], Is.EqualTo(FixtureMaps.ResourceSpot));
            Assert.That(path.Cells[path.Cells.Count - 1], Is.EqualTo(approach));
        }

        [Test]
        public void AnApproachCellIsTakenOnTheSideTheUnitComesFrom()
        {
            GridMap map = FixtureMaps.OpenGrid(7, 7);
            var footprint = new List<Cell> { new Cell(3, 3) };

            bool found = GridPathfinder.TryFindApproachCell(map, new Cell(6, 3), footprint, Walk(), out Cell approach, out PathResult path);

            Assert.That(found, Is.True);
            Assert.That(approach, Is.EqualTo(new Cell(4, 3)));
            Assert.That(path.Cost, Is.EqualTo(2 * PathOptions.DefaultStraightCost));
        }

        [Test]
        public void AFullyEnclosedTargetHasNoApproachCell()
        {
            GridMap map = FixtureMaps.GridFromRows(PocketRows);
            var footprint = new List<Cell> { Pocket };

            bool found = GridPathfinder.TryFindApproachCell(map, Cell.Zero, footprint, Walk(), out Cell approach, out PathResult path);

            Assert.That(found, Is.False);
            Assert.That(path.Status, Is.EqualTo(PathStatus.NoRoute));
            Assert.That(path.Cells, Is.Empty);
            Assert.That(approach, Is.EqualTo(default(Cell)));
        }

        private static bool IsNextTo(Cell cell, IReadOnlyList<Cell> footprint)
        {
            for (int i = 0; i < footprint.Count; i++)
            {
                int dx = cell.X - footprint[i].X;
                int dy = cell.Y - footprint[i].Y;
                if (dx >= -1 && dx <= 1 && dy >= -1 && dy <= 1) return true;
            }

            return false;
        }
    }
}
