using System.Collections.Generic;
using System.Linq;
using System.Text;
using JurassicPark.Presentation;
using JurassicPark.Simulation;
using NUnit.Framework;
using UnityEngine;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Tests.EditMode
{
    /// <summary>
    /// The HUD's pure parts: what the resource bar counts, what the minimap paints and what the command card offers. No scene
    /// and no drawing, so these run as fast as the rest of EditMode and say exactly which rule broke.
    /// </summary>
    public sealed class HudTests
    {
        private static readonly SeatId Red = new SeatId(1), Blue = new SeatId(2), Dinosaurs = new SeatId(8);

        private EntityCatalogAsset catalog;
        private MatchReadModel model;
        private readonly List<EntitySnapshot> entities = new List<EntitySnapshot>();
        private int nextId = 1;

        [SetUp]
        public void SetUp()
        {
            catalog = ScriptableObject.CreateInstance<EntityCatalogAsset>();
            catalog.entries = new[]
            {
                new EntityCatalogAsset.Entry { id = "survivor", kind = EntityKind.Unit, storageCapacity = 10 },
                new EntityCatalogAsset.Entry { id = "depot", kind = EntityKind.Building, isDepot = true, storageCapacity = 200 },
                new EntityCatalogAsset.Entry { id = "wall", kind = EntityKind.Building, buildCost = new[] { new EntityCatalogAsset.CostEntry { resource = "wood", amount = 6 } } },
                new EntityCatalogAsset.Entry { id = "gate", kind = EntityKind.Building, isGate = true, buildCost = new[] { new EntityCatalogAsset.CostEntry { resource = "wood", amount = 8 } } },
                new EntityCatalogAsset.Entry { id = "tree", kind = EntityKind.ResourceNode, nodeResource = "wood", nodeAmount = 40 },
            };
            model = new MatchReadModel(catalog, FixtureMaps.CampValley()) { LocalSeat = Red };
            model.SetSeats(new List<SeatSnapshot>
            {
                new SeatSnapshot { Id = Red, Team = 1, Controller = SeatController.Human },
                new SeatSnapshot { Id = Blue, Team = 1, Controller = SeatController.Computer },
                new SeatSnapshot { Id = Dinosaurs, Team = 2, Controller = SeatController.Computer },
            });
            entities.Clear();
            nextId = 1;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(catalog);

        /// <summary>A snapshot is a struct, so a tweak has to take it by reference or it changes a copy and nothing else.</summary>
        private delegate void Tweak(ref EntitySnapshot snapshot);

        private EntityId Add(string definitionId, SeatId owner, int packTotal = 0, Tweak tweak = null)
        {
            ushort index = (ushort)System.Array.FindIndex(catalog.entries, e => e.id == definitionId);
            var snapshot = new EntitySnapshot
            {
                Id = new EntityId(nextId++),
                DefinitionIndex = index,
                Kind = catalog.entries[index].kind,
                Owner = owner,
                PackTotal = (ushort)packTotal,
                PackCapacity = (ushort)catalog.entries[index].storageCapacity,
                HealthFraction = 255,
                BuildProgress = 255,
            };
            tweak?.Invoke(ref snapshot);
            entities.Add(snapshot);
            return snapshot.Id;
        }

        private void Apply() => model.Apply(model.Tick + 1, entities);

        [Test]
        public void TheResourceBarCountsOwnDepotsAlliedDepotsAndWhatOwnUnitsCarry()
        {
            Add("depot", Red, 60);
            Add("depot", Blue, 20);
            Add("depot", Dinosaurs, 500);        // another team's store is none of the local seat's business
            Add("survivor", Red, 7);
            Add("survivor", Red, 3);
            Add("survivor", Blue, 5);            // a team mate's pack is its own to deliver
            Apply();

            HudModel.Stores stores = HudModel.StoresOf(model);
            Assert.That(stores.Own, Is.EqualTo(60));
            Assert.That(stores.Allied, Is.EqualTo(20));
            Assert.That(stores.Carried, Is.EqualTo(10));
            Assert.That(stores.Total, Is.EqualTo(90));
        }

        [Test]
        public void TheClockAndTheNightRuleReadAsTheMatchStatesThem()
        {
            var text = new StringBuilder();
            HudModel.AppendClock(text, 125f);
            Assert.That(text.ToString(), Is.EqualTo("2:05"));
            text.Clear();
            HudModel.AppendClock(text, -3f);
            Assert.That(text.ToString(), Is.EqualTo("0:00"), "a finished countdown never shows a negative");
            text.Clear();
            HudModel.AppendTimeOfDay(text, 6.5f);
            Assert.That(text.ToString(), Is.EqualTo("06:30"));

            // The original's night wraps past midnight, so the test is the wrap, not the easy case.
            Assert.That(HudModel.IsNight(19f, 18f, 6f), Is.True);
            Assert.That(HudModel.IsNight(2f, 18f, 6f), Is.True);
            Assert.That(HudModel.IsNight(12f, 18f, 6f), Is.False);
        }

        [Test]
        public void TheMinimapPaintsTerrainUnderTheFogAndNothingOutsideTheReadModel()
        {
            MapDefinition map = FixtureMaps.CampValley();
            var fog = new FogSnapshot();
            var cells = new byte[map.Width * map.Height];
            Cell inside = FixtureMaps.CampGround, cliff = new Cell(0, 0), far = FixtureMaps.ResourceSpot;
            cells[map.IndexOf(inside)] = MinimapPainter.Visible;
            cells[map.IndexOf(new Cell(inside.X + 1, inside.Y))] = MinimapPainter.Explored;
            cells[map.IndexOf(cliff)] = MinimapPainter.Visible;
            fog.Set(map.Width, map.Height, cells, 7);

            var painter = new MinimapPainter();
            Assert.That(painter.NeedsPaint(map, fog), Is.True);
            painter.Paint(map, fog);
            Assert.That(painter.NeedsPaint(map, fog), Is.False, "the buffer is repainted only when the fog's revision moves");

            Assert.That(painter.Pixels[map.IndexOf(inside)], Is.EqualTo(MinimapPainter.Ground), "walkable ground in sight is the ground colour");
            Assert.That(painter.Pixels[map.IndexOf(cliff)], Is.EqualTo(MinimapPainter.Blocked), "a cliff in sight is the blocked colour");
            Assert.That(painter.Pixels[map.IndexOf(new Cell(inside.X + 1, inside.Y))], Is.EqualTo(MinimapPainter.Compose(true, MinimapPainter.Explored)));
            Assert.That(painter.Pixels[map.IndexOf(new Cell(inside.X + 1, inside.Y))].g, Is.LessThan(MinimapPainter.Ground.g), "explored ground is dimmer than ground in sight");
            Assert.That(painter.Pixels[map.IndexOf(far)], Is.EqualTo(MinimapPainter.Dark), "what the team has never seen is black");

            // A mask that does not cover this map is treated as unexplored, so a client without its fog shows nothing.
            var painted = new MinimapPainter();
            painted.Paint(map, new FogSnapshot());
            Assert.That(painted.Pixels.All(p => p.Equals(MinimapPainter.Dark)), Is.True);
        }

        [Test]
        public void MinimapDotsTellOwnFromAlliedFromHostileFromUnowned()
        {
            EntityId own = Add("survivor", Red);
            EntityId ally = Add("survivor", Blue);
            EntityId raptor = Add("survivor", Dinosaurs);
            EntityId tree = Add("tree", SeatId.None);
            EntityId remembered = Add("depot", Blue, tweak: (ref EntitySnapshot s) => s.Remembered = true);
            Apply();

            Color32 ownDot = DotOf(own), allyDot = DotOf(ally), hostileDot = DotOf(raptor), treeDot = DotOf(tree);
            Assert.That(ownDot.g, Is.GreaterThan(ownDot.r), "your own are green");
            Assert.That(allyDot.b, Is.GreaterThan(allyDot.r), "a team mate is blue");
            Assert.That(hostileDot.r, Is.GreaterThan(hostileDot.g), "another team is red");
            Assert.That(treeDot.g, Is.GreaterThan(treeDot.r), "an unowned tree is a dark green");
            Assert.That(treeDot.g, Is.LessThan(ownDot.g));
            Assert.That(DotOf(remembered).b, Is.LessThan(allyDot.b), "what is only remembered is drawn faintly");
        }

        private Color32 DotOf(EntityId id)
        {
            Assert.That(model.TryGet(id, out EntitySnapshot entity), Is.True);
            return MinimapPainter.DotOf(model, entity);
        }

        [Test]
        public void TheCardOffersStopAndTheCatalogsBuildingsForOwnUnitsAndSaysWhyWhenItCannot()
        {
            EntityId worker = Add("survivor", Red);
            EntityId allyWorker = Add("survivor", Blue);
            Apply();
            var buttons = new List<HudButton>();
            var scratch = new StringBuilder();

            CommandCard.Build(model, new[] { worker }, -1, buttons, scratch);
            Assert.That(buttons.Any(b => b.Action == HudAction.Stop && b.Enabled), Is.True);
            Assert.That(buttons.Where(b => b.Action == HudAction.Place).Select(b => b.DefinitionId), Is.EquivalentTo(new[] { "wall", "gate" }),
                "every buildable catalog entry becomes a button, with no list of unit types here");
            Assert.That(buttons.First(b => b.DefinitionId == "wall").Label, Does.Contain("6 wood"), "the price comes from the catalog");
            Assert.That(buttons.First(b => b.DefinitionId == "wall").Hotkey, Is.EqualTo("B"));
            Assert.That(buttons.Any(b => b.Action == HudAction.CancelPlacement), Is.False, "nothing is being placed");

            CommandCard.Build(model, new[] { worker }, 2, buttons, scratch);
            Assert.That(buttons.Any(b => b.Action == HudAction.CancelPlacement && b.Enabled), Is.True, "while placing, Esc is on the card");

            CommandCard.Build(model, new[] { allyWorker }, -1, buttons, scratch);
            Assert.That(buttons.Any(b => b.Enabled), Is.False, "nothing of yours is selected");
            Assert.That(buttons.First(b => b.Action == HudAction.Stop).Reason, Is.Not.Empty, "a greyed button says why");
        }

        [Test]
        public void AClickMaySelectAnOwnGateSoTheCardCanOfferToToggleIt()
        {
            EntityId gate = Add("gate", Red, tweak: (ref EntitySnapshot s) => s.GateOpen = false);
            EntityId alliedGate = Add("gate", Blue);
            EntityId lostGate = Add("gate", Red, tweak: (ref EntitySnapshot s) => s.Remembered = true);
            EntityId tree = Add("tree", SeatId.None, tweak: (ref EntitySnapshot s) => s.NodeRemaining = 40);
            EntityId worker = Add("survivor", Red);
            Apply();
            model.TryGet(gate, out EntitySnapshot gateSnapshot);
            model.TryGet(alliedGate, out EntitySnapshot alliedSnapshot);
            model.TryGet(lostGate, out EntitySnapshot lostSnapshot);
            model.TryGet(tree, out EntitySnapshot treeSnapshot);
            model.TryGet(worker, out EntitySnapshot workerSnapshot);

            // The same predicate a click uses: own units and own standing buildings only.
            Assert.That(SelectionController.IsOwnSelectable(model, gateSnapshot), Is.True);
            Assert.That(SelectionController.IsOwnSelectable(model, workerSnapshot), Is.True);
            Assert.That(SelectionController.IsOwnSelectable(model, alliedSnapshot), Is.False, "a team mate's gate is usable, not selectable");
            Assert.That(SelectionController.IsOwnSelectable(model, lostSnapshot), Is.False, "a memory cannot be selected");
            Assert.That(SelectionController.IsOwnSelectable(model, treeSnapshot), Is.False);

            var buttons = new List<HudButton>();
            CommandCard.Build(model, new[] { gate }, -1, buttons, new StringBuilder());
            Assert.That(buttons.Single(b => b.Action == HudAction.ToggleGate).Enabled, Is.True, "what a click selects, the card can toggle");
        }

        [Test]
        public void AnOwnGateOffersToggleGateAndATreeOffersNothing()
        {
            EntityId gate = Add("gate", Red, tweak: (ref EntitySnapshot s) => s.GateOpen = false);
            EntityId alliedGate = Add("gate", Blue);
            EntityId site = Add("gate", Red, tweak: (ref EntitySnapshot s) => { s.IsSite = true; s.BuildProgress = 40; });
            EntityId lostGate = Add("gate", Red, tweak: (ref EntitySnapshot s) => s.Remembered = true);
            EntityId tree = Add("tree", SeatId.None, tweak: (ref EntitySnapshot s) => s.NodeRemaining = 40);
            Apply();
            var buttons = new List<HudButton>();
            var scratch = new StringBuilder();

            CommandCard.Build(model, new[] { gate }, -1, buttons, scratch);
            HudButton toggle = buttons.Single(b => b.Action == HudAction.ToggleGate);
            Assert.That(toggle.Enabled, Is.True);
            Assert.That(toggle.Target, Is.EqualTo(gate));
            Assert.That(toggle.Label, Is.EqualTo("Open gate"), "a closed gate offers to open");

            CommandCard.Build(model, new[] { alliedGate }, -1, buttons, scratch);
            Assert.That(buttons.Any(b => b.Action == HudAction.ToggleGate), Is.True, "a team mate's gate is the local seat's to use");

            CommandCard.Build(model, new[] { site }, -1, buttons, scratch);
            Assert.That(buttons.Single(b => b.Action == HudAction.ToggleGate).Enabled, Is.False);
            Assert.That(buttons.Single(b => b.Action == HudAction.ToggleGate).Reason, Is.EqualTo("not built yet"));

            CommandCard.Build(model, new[] { lostGate }, -1, buttons, scratch);
            Assert.That(buttons.Single(b => b.Action == HudAction.ToggleGate).Enabled, Is.False, "something only remembered may be long gone");

            CommandCard.Build(model, new[] { tree }, -1, buttons, scratch);
            Assert.That(buttons.Any(b => b.Enabled), Is.False, "a tree takes no orders");
            Assert.That(buttons.Any(b => b.Action == HudAction.ToggleGate), Is.False);
        }

        [Test]
        public void TheSelectionPanelSaysWhatEachThingIsDoingAndCountsALargeSelection()
        {
            EntityId worker = Add("survivor", Red, 4, (ref EntitySnapshot s) =>
            {
                s.HealthFraction = 128;
                s.Task = TaskKindCode.Gather;
                s.TaskState = TaskState.Blocked;
                s.TaskReason = TaskReason.NoRoute;
            });
            EntityId tree = Add("tree", SeatId.None, tweak: (ref EntitySnapshot s) => { s.NodeRemaining = 17; s.Remembered = true; });
            EntityId gate = Add("gate", Red, tweak: (ref EntitySnapshot s) => s.GateOpen = true);
            Add("survivor", Red);
            Apply();

            var text = new StringBuilder();
            model.TryGet(worker, out EntitySnapshot workerSnapshot);
            HudModel.AppendSelected(text, model, workerSnapshot);
            string line = text.ToString();
            Assert.That(line, Does.Contain("survivor").And.Contain("yours").And.Contain("hp 50%").And.Contain("pack 4/10"));
            Assert.That(line, Does.Contain("Gather Blocked (NoRoute)"), "the task, its state and why it is stuck");

            text.Clear();
            model.TryGet(tree, out EntitySnapshot treeSnapshot);
            HudModel.AppendSelected(text, model, treeSnapshot);
            Assert.That(text.ToString(), Does.Contain("left 17").And.Contain("[remembered]").And.Contain("unowned"));

            text.Clear();
            model.TryGet(gate, out EntitySnapshot gateSnapshot);
            HudModel.AppendSelected(text, model, gateSnapshot);
            Assert.That(text.ToString(), Does.Contain("open"));

            var counts = new List<HudModel.DefinitionCount>();
            HudModel.CountByDefinition(model, entities.Select(e => e.Id).ToArray(), counts);
            Assert.That(counts.Select(c => c.Id + " x" + c.Count), Is.EqualTo(new[] { "survivor x2", "tree x1", "gate x1" }));
        }
    }
}
