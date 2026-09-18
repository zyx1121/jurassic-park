using System.Collections.Generic;
using System.Linq;
using JurassicPark.Presentation;
using JurassicPark.Simulation;
using NUnit.Framework;
using UnityEditor;
using EntityId = JurassicPark.Simulation.EntityId;

namespace JurassicPark.Tests.EditMode
{
    /// <summary>
    /// PLAN section 6's first cross-system delivery, run on the generated M1 data through the same commands a player sends:
    /// gather, haul, build a wall across the entrance, a dinosaur breaks the wall that is in its way and comes in, and the
    /// road and the screen's snapshot follow. Every step is asserted from the outside, never by reaching into a system.
    /// </summary>
    public sealed class AcceptanceScenarioTests
    {
        private static T Load<T>(string name) where T : UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>($"Assets/Data/M1/{name}.asset");

        private SimulationRuntime runtime;
        private CommandSender red;
        private readonly List<SimEvent> log = new List<SimEvent>();
        private readonly List<EntitySnapshot> snapshot = new List<EntitySnapshot>();
        private static readonly Cell Entrance = new Cell(21, 15), EntranceTwo = new Cell(21, 16), NorthGate = new Cell(13, 23);

        [SetUp]
        public void SetUp()
        {
            log.Clear();
            runtime = SimulationRuntime.Build(Load<SimulationSettingsAsset>("Simulation"), Load<MapDefinitionAsset>("Map"), Load<EntityCatalogAsset>("Catalog"), Load<ScenarioAsset>("Scenario"));
            // The scenario's computer ally would wall the same entrances; this scenario is about the player's own orders.
            runtime.Seats.SetController(new SeatId(2), SeatController.Human);
            runtime.World.Commit();
            runtime.Seats.TryGet(runtime.LocalSeat, out Seat seat);
            red = new CommandSender(runtime.Router, runtime.LocalSeat, seat.ControllerEpoch);
            runtime.World.DrainEvents();
        }

        private void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                runtime.World.Step();
                log.AddRange(runtime.World.DrainEvents());
                Assert.That(runtime.World.IsFaulted, Is.False);
                Assert.That(runtime.Logistics.TotalOf("wood") + runtime.Logistics.ConsumedOf("wood"), Is.EqualTo(60 + 20 + 14 * 40), "wood is conserved on every tick");
            }
        }

        private void RunUntil(System.Func<bool> done, int maxTicks, string what)
        {
            for (int i = 0; i < maxTicks && !done(); i++) Run(1);
            Assert.That(done(), Is.True, $"{what} did not happen within {maxTicks} ticks");
        }

        private EntityId[] Workers() => runtime.World.Entities.Where(e => e.DefinitionId == "survivor" && e.Owner == runtime.LocalSeat).Select(e => e.Id).ToArray();
        private Entity Depot() => runtime.World.Entities.First(e => e.DefinitionId == "depot" && e.Owner == runtime.LocalSeat);
        private int WoodIn(EntityId holder) => runtime.Logistics.TryGetContainer(holder, out Container c) ? c.AmountOf("wood") : 0;

        /// <summary>Wood in the depot and in the workers' packs: a builder uses what it already carries before going to the depot.</summary>
        private int WoodOnHand() => WoodIn(Depot().Id) + Workers().Sum(WoodIn);

        [Test]
        public void GatherHaulWallBreachEnter()
        {
            EntityId[] workers = Workers();
            Entity depot = Depot();
            Entity tree = runtime.World.Entities.Where(e => e.Kind == EntityKind.ResourceNode).OrderBy(e => SimVector2.Distance(e.Position, depot.Position)).First();
            Assert.That(runtime.World.Entities.Where(e => e.Owner == new SeatId(8)).All(e => SimVector2.Distance(e.Position, depot.Position) > 40f), Is.True, "the raptors start far away");

            // 1. Gather: the depot fills from the nearest grove through the entrance.
            red.Send(CommandKind.Gather, workers, tree.Position, tree.Id);
            Run(1);
            Assert.That(log.OfType<CommandResolved>().Single().Accepted, Is.True);
            RunUntil(() => WoodIn(depot.Id) >= 60 + 20, 900, "twenty logs delivered");
            Assert.That(log.OfType<GoodsTransferred>().Count(t => t.To == depot.Id), Is.GreaterThanOrEqualTo(2));

            // 2. Build: wall sites across the two-cell entrance and the north gate, taking their cells at once; the workers fetch what they lack.
            int wall = runtime.Catalog.IndexOf("wall");
            red.Send(CommandKind.Build, workers, runtime.Map.CenterOf(Entrance), argument: wall);
            red.Send(CommandKind.Build, workers, runtime.Map.CenterOf(EntranceTwo), argument: wall, mode: CommandMode.Queue);
            red.Send(CommandKind.Build, workers, runtime.Map.CenterOf(NorthGate), argument: wall, mode: CommandMode.Queue);
            Run(1);
            Assert.That(log.OfType<CommandResolved>().Skip(1).Select(a => a.Accepted), Is.All.True);
            Assert.That(runtime.Map.IsWalkable(Entrance) || runtime.Map.IsWalkable(EntranceTwo) || runtime.Map.IsWalkable(NorthGate), Is.False);
            Assert.That(log.OfType<PassabilityChanged>().Count(p => p.NowBlocked), Is.EqualTo(3));
            int woodBefore = WoodOnHand();
            RunUntil(() => runtime.Structures.SiteCount == 0, 2500, "all three walls built");
            Assert.That(woodBefore - WoodOnHand(), Is.EqualTo(18), "six logs per wall came out of the depot and the packs");
            Assert.That(runtime.Logistics.ConsumedOf("wood"), Is.EqualTo(18), "and became the walls");
            Assert.That(runtime.Logistics.LiveReservationCount, Is.EqualTo(0));
            Assert.That(log.OfType<BuildingCompleted>().Count(), Is.EqualTo(3));

            // 3. Sealed: for anyone without teeth, the camp cannot be entered.
            PathResult intoCamp = GridPathfinder.FindPath(runtime.Map, new Cell(30, 15), new Cell(15, 15), new PathOptions());
            Assert.That(intoCamp.Status, Is.EqualTo(PathStatus.NoRoute), "the entrance and the north gate walled off seal the camp");
            EntityId[] walls = log.OfType<BuildingCompleted>().Select(b => b.Building).ToArray();

            // 4. Breach: a raptor ordered onto a worker inside breaks exactly one wall that is in its way and comes in.
            Entity raptor = runtime.World.Entities.First(e => e.DefinitionId == "raptor");
            runtime.Seats.TryGet(new SeatId(8), out Seat dinoSeat);
            var dinos = new CommandSender(runtime.Router, new SeatId(8), dinoSeat.ControllerEpoch);
            Entity victim = runtime.World.Entities.First(e => e.Id == workers[0]);
            red.Send(CommandKind.Move, workers, runtime.Map.CenterOf(new Cell(10, 15)));
            Run(60);
            dinos.Send(CommandKind.Attack, new[] { raptor.Id }, victim.Position, victim.Id);
            Run(1);
            Assert.That(log.OfType<CommandResolved>().Last().Accepted, Is.True);
            RunUntil(() => walls.Any(w => !runtime.World.IsAlive(w)), 2500, "a wall broken");
            Assert.That(walls.Count(w => !runtime.World.IsAlive(w)), Is.EqualTo(1), "one wall was enough; the other is left standing");
            Assert.That(log.OfType<Attacked>().Select(a => a.Target).Distinct().Where(t => walls.Contains(t)).Count(), Is.EqualTo(1), "it never bit the wall that was not in its way");
            Assert.That(log.OfType<PassabilityChanged>().Last(p => p.Cause == walls.First(w => !runtime.World.IsAlive(w))).NowBlocked, Is.False, "the road reopened");
            Assert.That(GridPathfinder.FindPath(runtime.Map, new Cell(30, 15), new Cell(15, 15), new PathOptions()).Status, Is.EqualTo(PathStatus.Found));

            // 5. Enter: the raptor is inside the camp; the snapshot the screen reads says so too.
            RunUntil(() => runtime.Map.CellAt(raptor.Position).X < 21, 600, "the raptor entered the camp");
            SnapshotCapture.Entities(runtime, Load<EntityCatalogAsset>("Catalog"), snapshot);
            EntitySnapshot raptorOnScreen = snapshot.Single(s => s.Id == raptor.Id);
            Assert.That(runtime.Map.CellAt(raptorOnScreen.Position).X, Is.LessThan(21));
            Assert.That(snapshot.Count(s => walls.Contains(s.Id)), Is.EqualTo(2), "the broken wall is gone from the screen, the other two stand");
        }

        [Test]
        public void TheSameSeedReplaysTheScenarioToTheSameTick()
        {
            long Replay()
            {
                SetUp();
                EntityId[] workers = Workers();
                Entity tree = runtime.World.Entities.First(e => e.Kind == EntityKind.ResourceNode);
                red.Send(CommandKind.Gather, workers, tree.Position, tree.Id);
                RunUntil(() => WoodIn(Depot().Id) >= 70, 900, "ten logs");
                return runtime.World.Tick;
            }
            long first = Replay(), second = Replay();
            Assert.That(second, Is.EqualTo(first));
        }
    }
}
