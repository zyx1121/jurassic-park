using System.Collections.Generic;
using System.Linq;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class MapDefinitionTests
    {
        private static readonly string[] SealedCampRows =
        {
            "........", // y = 6
            "........", // y = 5
            "..####..", // y = 4
            "..#..#..", // y = 3
            "..#..#..", // y = 2
            "..####..", // y = 1
            "........"  // y = 0
        };

        private static MapDefinition SealedCampMap(IReadOnlyList<CampDefinition> camps, IReadOnlyList<RegionDefinition> regions) =>
            FixtureMaps.FromRows(SealedCampRows, camps, regions);

        /// <summary>Validation errors are read by a human, so the tests assert on the text that human would read.</summary>
        private static void AssertReports(IReadOnlyList<string> errors, params string[] fragments)
        {
            foreach (string fragment in fragments)
            {
                Assert.That(
                    errors.Any(error => error.Contains(fragment)),
                    Is.True,
                    $"no error mentioned '{fragment}' in: {string.Join(" | ", errors)}");
            }
        }

        private static CampDefinition Camp(string id, CellBounds bounds, params Cell[] entrances) =>
            new CampDefinition(id, id, bounds, entrances);

        [Test]
        public void TheCampValleyFixtureValidatesClean()
        {
            IReadOnlyList<string> errors = FixtureMaps.CampValley().Validate();

            Assert.That(errors, Is.Empty, string.Join(" | ", errors));
        }

        [Test]
        public void ValidateReportsAFlagsArrayThatDoesNotCoverTheMap()
        {
            var definition = new MapDefinition(
                4,
                4,
                FixtureMaps.CellSize,
                new[] { CellFlags.Walkable, CellFlags.Walkable },
                new List<CampDefinition>(),
                new List<RegionDefinition>());

            AssertReports(definition.Validate(), "Terrain flags hold 2 cells", "16 cells");
        }

        [Test]
        public void ValidateReportsACampOutsideTheMap()
        {
            MapDefinition definition = SealedCampMap(
                new[] { Camp("far-camp", new CellBounds(6, 5, 20, 20), new Cell(7, 5)) },
                new List<RegionDefinition>());

            AssertReports(definition.Validate(), "Camp 'far-camp'", "lie outside");
        }

        [Test]
        public void ValidateReportsARegionOutsideTheMap()
        {
            MapDefinition definition = SealedCampMap(
                new List<CampDefinition>(),
                new[] { new RegionDefinition("evac-far", RegionKind.Evacuation, new CellBounds(5, 5, 9, 9)) });

            AssertReports(definition.Validate(), "Region 'evac-far'", "lie outside");
        }

        [Test]
        public void ValidateReportsAnEntranceOnUnwalkableTerrain()
        {
            MapDefinition definition = SealedCampMap(
                new[] { Camp("cliff-camp", new CellBounds(3, 2, 4, 3), new Cell(2, 1)) },
                new List<RegionDefinition>());

            AssertReports(definition.Validate(), "entrance Cell(2, 1) is not walkable");
        }

        [Test]
        public void ValidateReportsACampWithNoEntrance()
        {
            MapDefinition definition = SealedCampMap(
                new[] { Camp("closed-camp", new CellBounds(3, 2, 4, 3)) },
                new List<RegionDefinition>());

            AssertReports(definition.Validate(), "Camp 'closed-camp' has no entrance");
        }

        [Test]
        public void ValidateReportsAnEntranceCutOffFromTheOpenGround()
        {
            // The camp interior is walkable but its cliff ring has no gap, so nothing outside can ever reach it.
            MapDefinition definition = SealedCampMap(
                new[] { Camp("walled-camp", new CellBounds(3, 2, 4, 3), new Cell(3, 3)) },
                new List<RegionDefinition>());

            AssertReports(definition.Validate(), "entrance Cell(3, 3) is cut off from the map's open ground");
        }

        [Test]
        public void ValidateReportsACampWithNoWalkableCell()
        {
            // The whole rectangle is the cliff ring, so the camp is a wall, not a place.
            MapDefinition definition = SealedCampMap(
                new[] { Camp("cliff-block", new CellBounds(2, 4, 5, 4), new Cell(2, 5)) },
                new List<RegionDefinition>());

            AssertReports(definition.Validate(), "Camp 'cliff-block' bounds [2,4..5,4] hold no walkable cell");
        }

        [Test]
        public void ValidateReportsAnEntranceThatIsNowhereNearItsCamp()
        {
            // Open ground far away used to satisfy every entrance rule while the camp itself stayed sealed.
            MapDefinition definition = SealedCampMap(
                new[] { Camp("sealed-camp", new CellBounds(3, 2, 4, 3), Cell.Zero) },
                new List<RegionDefinition>());

            AssertReports(definition.Validate(), "entrance Cell(0, 0) is neither inside camp bounds [3,2..4,3] nor beside them");
        }

        [Test]
        public void ValidateReportsAnEntranceThatCannotReachInsideTheCamp()
        {
            // The gap is beside the camp and on the open ground, but the cliff ring between them has no opening.
            MapDefinition definition = SealedCampMap(
                new[] { Camp("ringed-camp", new CellBounds(2, 1, 5, 4), new Cell(2, 0)) },
                new List<RegionDefinition>());

            AssertReports(definition.Validate(), "entrance Cell(2, 0) cannot reach any walkable cell inside camp bounds [2,1..5,4]");
        }

        [Test]
        public void ValidateReportsBuildableTerrainThatIsNotWalkable()
        {
            var flags = new CellFlags[4];
            for (int i = 0; i < flags.Length; i++) flags[i] = CellFlags.Walkable | CellFlags.Buildable;
            flags[3] = CellFlags.Buildable;

            var definition = new MapDefinition(2, 2, FixtureMaps.CellSize, flags, new List<CampDefinition>(), new List<RegionDefinition>());

            AssertReports(definition.Validate(), "Cell(1, 1) is buildable but not walkable (1 cells like it)");
        }

        [Test]
        public void BoundsWithSwappedCornersAreRefusedWhereTheyAreBuilt()
        {
            // The simulation never holds an inverted rectangle, so authoring has to straighten it before this point.
            Assert.That(() => new CellBounds(4, 3, 1, 1), Throws.ArgumentException);
            Assert.That(() => new CellBounds(new Cell(0, 0), new Cell(0, -1)), Throws.ArgumentException);
        }

        [Test]
        public void ValidateReportsDuplicateCampAndRegionIds()
        {
            MapDefinition definition = SealedCampMap(
                new[]
                {
                    Camp("camp", new CellBounds(0, 0, 1, 1), new Cell(0, 0)),
                    Camp("camp", new CellBounds(6, 5, 7, 6), new Cell(7, 6))
                },
                new[]
                {
                    new RegionDefinition("zone", RegionKind.Evacuation, new CellBounds(0, 5, 1, 6)),
                    new RegionDefinition("zone", RegionKind.Supply, new CellBounds(6, 0, 7, 1))
                });

            AssertReports(definition.Validate(), "Camp id 'camp' is used more than once", "Region id 'zone' is used more than once");
        }

        [Test]
        public void ValidateReportsAnEvacuationRegionWithNoWalkableCell()
        {
            MapDefinition definition = SealedCampMap(
                new List<CampDefinition>(),
                new[] { new RegionDefinition("evac-cliff", RegionKind.Evacuation, new CellBounds(2, 4, 5, 4)) });

            AssertReports(definition.Validate(), "Region 'evac-cliff' (Evacuation) has no walkable cell");
        }

        [Test]
        public void ValidateAcceptsTheOriginalsTwelveCampsAndTenEvacuationRegions()
        {
            var camps = new List<CampDefinition>();
            for (int i = 0; i < 12; i++)
            {
                int y = i * 3;
                camps.Add(Camp($"camp-{i}", new CellBounds(1, y, 3, y + 1), new Cell(4, y)));
            }

            var regions = new List<RegionDefinition>();
            for (int i = 0; i < 10; i++)
            {
                regions.Add(new RegionDefinition($"evac-{i}", RegionKind.Evacuation, new CellBounds(20, i * 3, 22, i * 3 + 1)));
            }

            var definition = new MapDefinition(
                32,
                36,
                FixtureMaps.CellSize,
                FixtureMaps.OpenGround(32, 36).Flags,
                camps,
                regions);

            Assert.That(definition.Camps, Has.Count.EqualTo(12));
            Assert.That(definition.Regions, Has.Count.EqualTo(10));
            Assert.That(definition.Validate(), Is.Empty);
        }

        [Test]
        public void ADefinitionDoesNotChangeWhenTheListsItWasBuiltFromDo()
        {
            var flags = new List<CellFlags> { CellFlags.Walkable, CellFlags.Walkable, CellFlags.Walkable, CellFlags.Walkable };
            var camps = new List<CampDefinition> { Camp("camp", new CellBounds(0, 0, 1, 1), new Cell(0, 0)) };
            var regions = new List<RegionDefinition>();
            var definition = new MapDefinition(2, 2, FixtureMaps.CellSize, flags, camps, regions);

            flags[0] = CellFlags.None;
            camps.Clear();
            regions.Add(new RegionDefinition("late", RegionKind.Supply, new CellBounds(0, 0, 0, 0)));

            Assert.That(definition.IsStaticWalkable(Cell.Zero), Is.True);
            Assert.That(definition.Camps, Has.Count.EqualTo(1));
            Assert.That(definition.Regions, Is.Empty);
        }
    }
}
