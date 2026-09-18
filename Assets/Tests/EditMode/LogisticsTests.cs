using System.Collections.Generic;
using System.Linq;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class LogisticsTests
    {
        private static readonly SeatId Red = new SeatId(1), Blue = new SeatId(2), Raiders = new SeatId(3);
        private const string Wood = "wood";

        private World world;
        private GridMap map;
        private DefinitionCatalog catalog;
        private Logistics goods;
        private TaskSystem tasks;
        private CommandRouter router;
        private readonly Dictionary<SeatId, CommandSender> senders = new Dictionary<SeatId, CommandSender>();
        private readonly List<SimEvent> log = new List<SimEvent>();
        private long expectedWood;

        [SetUp]
        public void SetUp()
        {
            log.Clear();
            senders.Clear();
            expectedWood = 0;
            world = new World(new SimConfig(10, 8, 1));
            map = FixtureMaps.CampValleyGrid();
            catalog = new DefinitionCatalog();
            catalog.Add(new EntityDefinition("survivor", 4f, storageCapacity: 5, gatherSecondsPerUnit: 0.2f));
            catalog.Add(new EntityDefinition("depot", 0f, storageCapacity: 100, isDepot: true));
            catalog.Add(new EntityDefinition("crate", 0f, storageCapacity: 8, isDepot: true));
            catalog.Add(new EntityDefinition("tree", 0f, nodeResource: Wood, nodeAmount: 12));
            catalog.Add(new EntityDefinition("sapling", 0f, nodeResource: Wood, nodeAmount: 7));
            catalog.Add(new EntityDefinition("pile", 0f));
            goods = new Logistics(world, new LogisticsConfig(reservationLifetimeTicks: 20, "pile"));
            var seats = new SeatRegistry(world);
            seats.Add(Red, "Red", 1, SeatController.Human);
            seats.Add(Blue, "Blue", 1, SeatController.Human);
            seats.Add(Raiders, "Raiders", 2, SeatController.Computer);
            var config = new TaskConfig(5, 3, 2, new PathOptions());
            tasks = new TaskSystem(new TaskContext(world, map, catalog, config, goods, seats));
            router = new CommandRouter(seats, new CommandRouterConfig(8, 16, 8));
            router.Register(CommandKind.Move, new MoveCommandHandler(tasks));
            router.Register(CommandKind.Stop, new StopCommandHandler(tasks));
            foreach (CommandKind kind in new[] { CommandKind.Gather, CommandKind.Deliver, CommandKind.Pickup })
                router.Register(kind, new HaulCommandHandler(tasks, kind));
            world.AddSystem(router);
            world.AddSystem(tasks);
            world.AddSystem(goods);
            foreach (SeatId seat in new[] { Red, Blue, Raiders }) senders[seat] = new CommandSender(router, seat, 1);
        }

        private Entity Place(string definitionId, SeatId owner, Cell cell, bool blocks = false)
        {
            catalog.TryGet(definitionId, out EntityDefinition definition);
            EntityKind kind = definition.NodeResource != null ? EntityKind.ResourceNode : definition.MoveSpeed > 0f ? EntityKind.Unit : EntityKind.Building;
            Entity entity = world.Spawn(kind, definitionId, owner, map.CenterOf(cell));
            goods.Attach(entity, definition);
            if (blocks) Assert.That(map.TryOccupy(new[] { cell }, entity.Id, destructible: true), Is.True);
            expectedWood += definition.NodeAmount;
            world.Commit();
            return entity;
        }

        private int Packed(Entity e) => goods.TryGetContainer(e.Id, out Container c) ? c.AmountOf(Wood) : 0;
        private int Left(Entity node) => goods.TryGetNode(node.Id, out ResourceNode n) ? n.Remaining : 0;

        /// <summary>Steps the world and checks, every single tick, the invariants that must never break.</summary>
        private void Run(int ticks, params Entity[] nodes)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.Step();
                log.AddRange(world.DrainEvents());
                Assert.That(goods.TotalOf(Wood) + goods.ConsumedOf(Wood), Is.EqualTo(expectedWood), $"wood appeared or vanished at tick {world.Tick}");
                foreach (Entity node in nodes)
                    if (goods.TryGetNode(node.Id, out ResourceNode n)) Assert.That(n.Unreserved, Is.GreaterThanOrEqualTo(0), "node stock oversold");
                foreach (Entity e in world.Entities)
                    if (goods.TryGetContainer(e.Id, out Container c)) Assert.That(c.FreeCapacity, Is.GreaterThanOrEqualTo(0), $"{e} promised more room than it has");
            }
        }

        private TaskStateChanged LastTaskEvent(Entity actor) => log.OfType<TaskStateChanged>().Last(e => e.Actor == actor.Id);

        [Test]
        public void AGathererEmptiesTheNodeIntoTheDepotTripByTrip()
        {
            Entity depot = Place("depot", Red, FixtureMaps.CampGround, blocks: true);
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity worker = Place("survivor", Red, new Cell(13, 5));
            senders[Red].Send(CommandKind.Gather, new[] { worker.Id }, targetEntity: tree.Id);

            Run(600, tree);

            Assert.That(Packed(depot), Is.EqualTo(12));
            Assert.That(Left(tree), Is.EqualTo(0));
            Assert.That(Packed(worker), Is.EqualTo(0));
            Assert.That(LastTaskEvent(worker).State, Is.EqualTo(TaskState.Completed));
            Assert.That(LastTaskEvent(worker).Reason, Is.EqualTo(TaskReason.NodeDepleted));
            Assert.That(goods.LiveReservationCount, Is.EqualTo(0));
            Assert.That(log.OfType<GoodsTransferred>().Count(e => e.To == depot.Id), Is.EqualTo(3), "12 units in packs of 5 is three trips");
            Assert.That(log.OfType<ResourceNodeDepleted>().Count(), Is.EqualTo(1));
        }

        [Test]
        public void GoodsOnlyChangeHandsBesideTheTarget()
        {
            Entity depot = Place("depot", Red, FixtureMaps.CampGround, blocks: true);
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity worker = Place("survivor", Red, new Cell(13, 5));
            senders[Red].Send(CommandKind.Gather, new[] { worker.Id }, targetEntity: tree.Id);

            int before = 0;
            for (int i = 0; i < 300; i++)
            {
                Run(1, tree);
                int now = Packed(worker);
                Cell at = map.CellAt(worker.Position);
                if (now > before) Assert.That(Mover.IsBeside(map, at, new[] { FixtureMaps.ResourceSpot }), Is.True, "gathered from a distance");
                if (now < before) Assert.That(Mover.IsBeside(map, at, new[] { FixtureMaps.CampGround }), Is.True, "delivered from a distance");
                before = now;
            }
            Assert.That(Packed(depot), Is.GreaterThan(0));
        }

        [Test]
        public void TwoGatherersNeverCountOnTheSameLastLog()
        {
            Entity depot = Place("depot", Red, FixtureMaps.CampGround, blocks: true);
            Entity sapling = Place("sapling", SeatId.None, FixtureMaps.ResourceSpot);
            Entity one = Place("survivor", Red, new Cell(13, 2)), two = Place("survivor", Blue, new Cell(12, 1));
            senders[Red].Send(CommandKind.Gather, new[] { one.Id }, targetEntity: sapling.Id);
            senders[Blue].Send(CommandKind.Gather, new[] { two.Id }, targetEntity: sapling.Id);

            Run(2, sapling);
            goods.TryGetNode(sapling.Id, out ResourceNode node);
            Assert.That(node.Unreserved, Is.EqualTo(0), "5 promised to the first, the last 2 to the second");

            Run(500, sapling);
            Assert.That(Packed(depot), Is.EqualTo(7), "an ally delivers into Red's depot");
            Assert.That(goods.LiveReservationCount, Is.EqualTo(0));
        }

        [Test]
        public void AFullDepotLeavesTheGoodsInThePackAndTheGathererBlocked()
        {
            Entity crate = Place("crate", Red, FixtureMaps.CampGround, blocks: true);
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity worker = Place("survivor", Red, new Cell(13, 5));
            senders[Red].Send(CommandKind.Gather, new[] { worker.Id }, targetEntity: tree.Id);

            Run(600, tree);

            Assert.That(Packed(crate), Is.EqualTo(8));
            Assert.That(Packed(worker) + Left(tree), Is.EqualTo(4));
            Assert.That(tasks.CurrentOf(worker.Id).State, Is.EqualTo(TaskState.Blocked));
            Assert.That(tasks.CurrentOf(worker.Id).Reason, Is.EqualTo(TaskReason.NoDepotAvailable));
        }

        [Test]
        public void AGathererKilledWhileCarryingDropsItsLoadWhereItStoodAndAnotherCanHaulItHome()
        {
            Entity depot = Place("depot", Red, FixtureMaps.CampGround, blocks: true);
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity worker = Place("survivor", Red, new Cell(13, 2));
            senders[Red].Send(CommandKind.Gather, new[] { worker.Id }, targetEntity: tree.Id);
            for (int i = 0; i < 200 && Packed(worker) < 5; i++) Run(1, tree);
            Run(5, tree);
            SimVector2 diedAt = worker.Position;

            world.Despawn(worker.Id, "eaten");
            Run(2, tree);

            GoodsDropped dropped = log.OfType<GoodsDropped>().Single();
            Assert.That(world.TryGet(dropped.Pile, out Entity pile), Is.True);
            Assert.That(pile.Position, Is.EqualTo(diedAt));
            Assert.That(Packed(pile), Is.EqualTo(5));
            Assert.That(goods.LiveReservationCount, Is.EqualTo(0), "the dead gatherer's promises are given back");

            Entity rescuer = Place("survivor", Blue, new Cell(13, 5));
            senders[Blue].Send(CommandKind.Pickup, new[] { rescuer.Id }, targetEntity: pile.Id);
            senders[Blue].Send(CommandKind.Deliver, new[] { rescuer.Id }, targetEntity: depot.Id, mode: CommandMode.Queue);
            Run(300, tree);

            Assert.That(Packed(depot), Is.EqualTo(5));
            Assert.That(world.IsAlive(pile.Id), Is.False, "an emptied pile removes itself");
        }

        [Test]
        public void StopKeepsTheGoodsInThePackAndGivesBackEveryPromise()
        {
            Place("depot", Red, FixtureMaps.CampGround, blocks: true);
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity worker = Place("survivor", Red, new Cell(13, 2));
            senders[Red].Send(CommandKind.Gather, new[] { worker.Id }, targetEntity: tree.Id);
            for (int i = 0; i < 200 && Packed(worker) < 2; i++) Run(1, tree);

            senders[Red].Send(CommandKind.Stop, new[] { worker.Id });
            Run(2, tree);

            Assert.That(Packed(worker), Is.GreaterThanOrEqualTo(2));
            Assert.That(goods.LiveReservationCount, Is.EqualTo(0));
            goods.TryGetNode(tree.Id, out ResourceNode node);
            Assert.That(node.Unreserved, Is.EqualTo(node.Remaining));
        }

        [Test]
        public void ADepotDestroyedOnTheWayThereSpillsItsStoreAndTheCarrierGoesToAnother()
        {
            Entity near = Place("depot", Red, FixtureMaps.CampGround, blocks: true);
            Entity far = Place("depot", Blue, new Cell(9, 5), blocks: true);
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity stocker = Place("survivor", Red, new Cell(7, 5));
            Entity worker = Place("survivor", Red, new Cell(13, 2));
            // Put 3 into the near depot first so there is a store to spill.
            goods.Gather(tree.Id, stocker.Id, 3, null, null);
            senders[Red].Send(CommandKind.Deliver, new[] { stocker.Id }, targetEntity: near.Id);
            Run(10, tree);
            Assert.That(Packed(near), Is.EqualTo(3));

            senders[Red].Send(CommandKind.Gather, new[] { worker.Id }, targetEntity: tree.Id);
            for (int i = 0; i < 200 && Packed(worker) < 5; i++) Run(1, tree);
            Run(3, tree);
            map.Release(near.Id);
            world.Despawn(near.Id, "destroyed");
            Run(400, tree);

            Assert.That(log.OfType<GoodsDropped>().Count(e => e.From == near.Id), Is.EqualTo(1));
            Assert.That(Packed(far), Is.GreaterThanOrEqualTo(5), "the carrier re-routed to the ally's depot");
        }

        [Test]
        public void ASecondHaulerIsToldThePileIsSpokenForInsteadOfWalkingThereForNothing()
        {
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity dropper = Place("survivor", Red, new Cell(10, 1));
            goods.Gather(tree.Id, dropper.Id, 3, null, null);
            world.Despawn(dropper.Id, "eaten");
            Run(2, tree);
            EntityId pile = log.OfType<GoodsDropped>().Single().Pile;

            Entity first = Place("survivor", Red, new Cell(2, 1)), second = Place("survivor", Blue, new Cell(3, 1));
            senders[Red].Send(CommandKind.Pickup, new[] { first.Id }, targetEntity: pile);
            Run(1, tree);
            senders[Blue].Send(CommandKind.Pickup, new[] { second.Id }, targetEntity: pile);
            Run(2, tree);

            Assert.That(LastTaskEvent(second).State, Is.EqualTo(TaskState.Failed));
            Assert.That(LastTaskEvent(second).Reason, Is.EqualTo(TaskReason.SourceEmpty));
            Run(200, tree);
            Assert.That(Packed(first), Is.EqualTo(3));
        }

        [Test]
        public void EnemyStoresAndOtherUnitsPacksAreClosed()
        {
            Entity enemyDepot = Place("depot", Raiders, new Cell(1, 9), blocks: true);
            Entity allyDepot = Place("depot", Blue, FixtureMaps.CampGround, blocks: true);
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity worker = Place("survivor", Red, new Cell(7, 5)), friend = Place("survivor", Blue, new Cell(8, 5));
            goods.Gather(tree.Id, worker.Id, 2, null, null);
            goods.Gather(tree.Id, friend.Id, 2, null, null);

            senders[Red].Send(CommandKind.Deliver, new[] { worker.Id }, targetEntity: enemyDepot.Id);
            senders[Red].Send(CommandKind.Pickup, new[] { worker.Id }, targetEntity: friend.Id);
            senders[Red].Send(CommandKind.Gather, new[] { worker.Id }, targetEntity: allyDepot.Id);
            senders[Red].Send(CommandKind.Gather, new[] { allyDepot.Id }, targetEntity: tree.Id);
            senders[Red].Send(CommandKind.Deliver, new[] { worker.Id }, targetEntity: allyDepot.Id);
            Run(1, tree);

            Assert.That(log.OfType<CommandResolved>().Select(r => r.Rejection), Is.EqualTo(new[]
            {
                CommandRejection.NotAllowedOnTarget, CommandRejection.NotAllowedOnTarget, CommandRejection.InvalidTarget,
                CommandRejection.NotOwner, CommandRejection.None,
            }));
            Run(20, tree);
            Assert.That(Packed(allyDepot), Is.EqualTo(2));
            Assert.That(Packed(friend), Is.EqualTo(2));
        }

        [Test]
        public void ATransferChangesBothSidesOrNeither()
        {
            Entity crate = Place("crate", Red, FixtureMaps.CampGround);
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity a = Place("survivor", Red, new Cell(7, 5)), b = Place("survivor", Red, new Cell(8, 5));
            goods.Gather(tree.Id, a.Id, 5, null, null);
            goods.Gather(tree.Id, b.Id, 5, null, null);

            Assert.That(goods.Transfer(a.Id, crate.Id, Wood, 5, null, null), Is.EqualTo(5));
            Assert.That(new[] { Packed(a), Packed(crate) }, Is.EqualTo(new[] { 0, 5 }));
            Assert.That(goods.Transfer(b.Id, crate.Id, Wood, 5, null, null), Is.EqualTo(3), "only what fits moves");
            Assert.That(new[] { Packed(b), Packed(crate) }, Is.EqualTo(new[] { 2, 8 }));
            Assert.That(goods.Transfer(b.Id, crate.Id, Wood, 2, null, null), Is.EqualTo(0));
            Assert.That(new[] { Packed(b), Packed(crate) }, Is.EqualTo(new[] { 2, 8 }));
            Run(1, tree);
            Assert.That(goods.Transfer(b.Id, b.Id, Wood, 2, null, null), Is.EqualTo(0));
            Assert.That(goods.Transfer(crate.Id, a.Id, "stone", 1, null, null), Is.EqualTo(0));
        }

        [Test]
        public void ReservedGoodsAreNotOnOfferAndRoomPromisedIsNotFree()
        {
            Entity crate = Place("crate", Red, FixtureMaps.CampGround);
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity a = Place("survivor", Red, new Cell(7, 5));
            goods.Gather(tree.Id, a.Id, 5, null, null);
            goods.Transfer(a.Id, crate.Id, Wood, 5, null, null);
            var mine = new TaskId(901); var theirs = new TaskId(902);

            Reservation claim = goods.Reserve(ReservationKind.Withdrawal, crate.Id, Wood, 4, mine);
            Assert.That(goods.AvailableIn(crate.Id, Wood), Is.EqualTo(1));
            Assert.That(goods.AvailableIn(crate.Id, Wood, mine), Is.EqualTo(5));
            Assert.That(goods.Reserve(ReservationKind.Withdrawal, crate.Id, Wood, 4, theirs).Amount, Is.EqualTo(1), "never promises more than is there");

            goods.TryGetContainer(crate.Id, out Container store);
            Reservation room = goods.Reserve(ReservationKind.Deposit, crate.Id, string.Empty, 10, mine);
            Assert.That(room.Amount, Is.EqualTo(3));
            Assert.That(store.FreeCapacity, Is.EqualTo(0));
            goods.Gather(tree.Id, a.Id, 2, null, null);
            Assert.That(goods.Transfer(a.Id, crate.Id, Wood, 2, null, null, theirs), Is.EqualTo(0), "promised room is not free to others");
            Assert.That(goods.Transfer(a.Id, crate.Id, Wood, 2, null, room, theirs), Is.EqualTo(0), "holding someone else's reservation object does not make it yours");
            Assert.That(room.Amount, Is.EqualTo(3), "and it is not spent by the attempt");
            Assert.That(goods.Transfer(a.Id, crate.Id, Wood, 2, null, room, mine), Is.EqualTo(2));
            Assert.That(room.Amount, Is.EqualTo(1));
            Assert.That(claim.Amount, Is.EqualTo(4));
        }

        [Test]
        public void AReservationNobodyRenewsExpiresAndReleaseNeverRefundsTwice()
        {
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            goods.TryGetNode(tree.Id, out ResourceNode node);
            Reservation forgotten = goods.Reserve(ReservationKind.NodeStock, tree.Id, Wood, 5, new TaskId(77));
            Assert.That(node.Unreserved, Is.EqualTo(7));

            Run(19, tree);
            Assert.That(goods.IsLive(forgotten), Is.True);
            Run(2, tree);
            Assert.That(goods.IsLive(forgotten), Is.False);
            Assert.That(node.Unreserved, Is.EqualTo(12));

            goods.Release(forgotten);
            goods.Release(forgotten);
            goods.Release(null);
            Assert.That(node.Unreserved, Is.EqualTo(12));
        }

        [Test]
        public void ABlockingTreeIsGatheredFromTheCellBesideIt()
        {
            Place("depot", Red, FixtureMaps.CampGround, blocks: true);
            Entity tree = Place("tree", SeatId.None, new Cell(13, 9), blocks: true);
            Entity worker = Place("survivor", Red, new Cell(1, 1));
            senders[Red].Send(CommandKind.Gather, new[] { worker.Id }, targetEntity: tree.Id);

            for (int i = 0; i < 400 && Packed(worker) == 0; i++)
            {
                Run(1, tree);
                Assert.That(map.CellAt(worker.Position), Is.Not.EqualTo(new Cell(13, 9)));
            }
            Assert.That(Packed(worker), Is.GreaterThan(0));
        }

        [Test]
        public void GoodsCanBePutDownOnAGroundPile()
        {
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity dropper = Place("survivor", Red, new Cell(10, 1));
            goods.Gather(tree.Id, dropper.Id, 2, null, null);
            world.Despawn(dropper.Id, "eaten");
            Run(2, tree);
            EntityId pile = log.OfType<GoodsDropped>().Single().Pile;
            world.TryGet(pile, out Entity pileEntity);

            Entity carrier = Place("survivor", Blue, new Cell(8, 1));
            goods.Gather(tree.Id, carrier.Id, 5, null, null);
            senders[Blue].Send(CommandKind.Deliver, new[] { carrier.Id }, targetEntity: pile);
            Run(60, tree);

            Assert.That(LastTaskEvent(carrier).Reason, Is.EqualTo(TaskReason.Delivered));
            Assert.That(Packed(pileEntity), Is.EqualTo(7));
            Assert.That(goods.LiveReservationCount, Is.EqualTo(0));
        }

        [Test]
        public void APartialDeliveryCompletesAndKeepsTheRestInThePackAndTheQueueAlive()
        {
            Entity crate = Place("crate", Red, FixtureMaps.CampGround, blocks: true);
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity filler = Place("survivor", Red, new Cell(7, 5)), carrier = Place("survivor", Red, new Cell(8, 5));
            goods.Gather(tree.Id, filler.Id, 5, null, null);
            goods.Transfer(filler.Id, crate.Id, Wood, 5, null, null);
            goods.Gather(tree.Id, carrier.Id, 5, null, null);

            senders[Red].Send(CommandKind.Deliver, new[] { carrier.Id }, targetEntity: crate.Id);
            senders[Red].Send(CommandKind.Move, new[] { carrier.Id }, map.CenterOf(new Cell(10, 5)), mode: CommandMode.Queue);
            Run(60, tree);

            Assert.That(new[] { Packed(crate), Packed(carrier) }, Is.EqualTo(new[] { 8, 2 }));
            Assert.That(log.OfType<TaskStateChanged>().Any(e => e.Reason == TaskReason.DeliveredPartly && e.State == TaskState.Completed), Is.True);
            Assert.That(map.CellAt(carrier.Position), Is.EqualTo(new Cell(10, 5)), "the queued move still ran");
        }

        [Test]
        public void ADestroyedNodeTakesItsStockWithItOnTheBooks()
        {
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            world.Despawn(tree.Id, "trampled");
            Run(2);

            Assert.That(goods.TotalOf(Wood), Is.EqualTo(0));
            Assert.That(goods.ConsumedTotal, Is.EqualTo(12));
        }

        [Test]
        public void ConsumingLeavesGoodsPromisedToAnotherTaskAlone()
        {
            Entity crate = Place("crate", Red, FixtureMaps.CampGround);
            Entity tree = Place("tree", SeatId.None, FixtureMaps.ResourceSpot);
            Entity a = Place("survivor", Red, new Cell(7, 5));
            goods.Gather(tree.Id, a.Id, 5, null, null);
            goods.Transfer(a.Id, crate.Id, Wood, 5, null, null);
            goods.Reserve(ReservationKind.Withdrawal, crate.Id, Wood, 4, new TaskId(500));

            Assert.That(goods.Consume(crate.Id, Wood, 5, new TaskId(501)), Is.EqualTo(1));
            Assert.That(Packed(crate), Is.EqualTo(4));
            Run(1, tree);
        }
    }
}
