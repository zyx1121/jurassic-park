using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace JurassicPark.Infrastructure.Tests
{
    public sealed class InfraSimulationTests
    {
        const float Dt = .05f;
        long nextCommand;

        [SetUp]
        public void SetUp() => nextCommand = 1;

        static Vector2Int C(int x, int y = 1) => new Vector2Int(x, y);

        static InfraRules FastRules() => new InfraRules
        {
            MoveSpeed = 4,
            GatherSeconds = .1f,
            TransferSeconds = .1f,
            BuildSeconds = .3f,
            AttackInterval = .1f,
            AttackDamage = 10,
            ReplanSeconds = .2f
        };

        static InfraWorld World(int width = 9, int height = 3, InfraRules rules = null) =>
            new InfraWorld(new InfraMap(width, height, 1), rules ?? FastRules());

        InfraCommandResult Send(InfraWorld world, InfraEntity actor, InfraCommandKind kind,
            Vector2Int cell, int target = 0) =>
            world.Submit(new InfraCommand(nextCommand++, actor.Owner, actor.Id, kind, cell, target));

        static void Conserved(InfraWorld world)
        {
            Assert.That(world.TotalPhysicalMaterials + world.ConsumedMaterials, Is.EqualTo(world.InitialMaterials),
                "Material conservation at t=" + world.Time);
            foreach (InfraEntity entity in world.Entities.Values)
            {
                Assert.That(entity.Stored, Is.GreaterThanOrEqualTo(0), "Stored entity " + entity.Id);
                Assert.That(entity.Carried, Is.GreaterThanOrEqualTo(0), "Carried entity " + entity.Id);
                Assert.That(entity.Delivered, Is.GreaterThanOrEqualTo(0), "Delivered entity " + entity.Id);
                Assert.That(entity.Reserved, Is.GreaterThanOrEqualTo(0), "Reserved entity " + entity.Id);
                if (entity.Kind == InfraEntityKind.Depot)
                    Assert.That(entity.Stored, Is.LessThanOrEqualTo(entity.Capacity), "Depot overflow");
                if (entity.Kind == InfraEntityKind.Worker)
                    Assert.That(entity.Carried, Is.LessThanOrEqualTo(entity.Capacity), "Worker overflow");
            }
        }

        static string Describe(InfraWorld world)
        {
            var text = new StringBuilder("Timed out at t=" + world.Time);
            foreach (InfraEntity entity in world.Entities.Values)
                text.Append("\n").Append(entity.Id).Append(" ").Append(entity.Kind).Append(" ")
                    .Append(entity.Cell).Append(" ").Append(entity.TaskStatus).Append(" ")
                    .Append(entity.Action).Append(" ").Append(entity.Reason)
                    .Append(" stock=").Append(entity.Stored).Append(" carry=").Append(entity.Carried)
                    .Append(" delivered=").Append(entity.Delivered).Append(" reserved=").Append(entity.Reserved);
            return text.ToString();
        }

        static void Until(InfraWorld world, Func<bool> predicate, int maxTicks = 1200)
        {
            for (int tick = 0; tick < maxTicks && !predicate(); tick++)
            {
                world.Tick(Dt);
                Conserved(world);
            }
            Assert.That(predicate(), Is.True, Describe(world));
        }

        static void Advance(InfraWorld world, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++)
            {
                world.Tick(Dt);
                Conserved(world);
            }
        }

        [Test]
        public void MapCoordinatesAreCenteredAndBoundsAreExplicit()
        {
            var map = new InfraMap(4, 2, 2);
            Assert.That(map.CellCenter(C(0, 0)), Is.EqualTo(new Vector2(-3, -1)));
            Assert.That(map.CellCenter(C(3, 1)), Is.EqualTo(new Vector2(3, 1)));
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                    Assert.That(map.WorldToCell(map.CellCenter(C(x, y))), Is.EqualTo(C(x, y)));
            Assert.That(map.WorldToCell(new Vector2(4, 0)), Is.EqualTo(C(4, 1)));
            Assert.That(map.Contains(C(-1, 0)), Is.False);
            Assert.That(map.Contains(C(4, 1)), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => map.TileAt(C(4, 0)));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.SetTile(C(0, 2), InfraTileKind.Cliff));
            map.SetTile(C(2, 1), InfraTileKind.Cliff, 7);
            Assert.That(map.TileAt(C(2, 1)), Is.EqualTo(InfraTileKind.Cliff));
            Assert.That(map.ElevationAt(C(2, 1)), Is.EqualTo(7));
            map.Camps.Add(C(1, 1));
            Assert.That(new InfraWorld(map, FastRules()).Entities, Is.Empty, "Map must not spawn actors.");
        }

        [Test]
        public void RulesAndCommandExposeTheRequiredDefaultsAndPayload()
        {
            var rules = new InfraRules();
            Assert.That(rules.MoveSpeed, Is.EqualTo(5));
            Assert.That(rules.WallCost, Is.EqualTo(10));
            Assert.That(rules.CarryCapacity, Is.EqualTo(5));
            Assert.That(rules.DepotCapacity, Is.EqualTo(200));
            var command = new InfraCommand(123, 2, 7, InfraCommandKind.Gather, C(3, 4), 9);
            Assert.That(command.CommandId, Is.EqualTo(123));
            Assert.That(command.Owner, Is.EqualTo(2));
            Assert.That(command.ActorId, Is.EqualTo(7));
            Assert.That(command.Kind, Is.EqualTo(InfraCommandKind.Gather));
            Assert.That(command.Cell, Is.EqualTo(C(3, 4)));
            Assert.That(command.TargetId, Is.EqualTo(9));
        }

        [Test]
        public void MoveIsContinuousAndRejectsBlockedOrOutOfBoundsCells()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            world.Map.SetTile(C(2), InfraTileKind.Water);
            Assert.That(Send(world, worker, InfraCommandKind.Move, C(2)).Accepted, Is.False);
            Assert.That(Send(world, worker, InfraCommandKind.Move, C(9)).Accepted, Is.False);
            Assert.That(Send(world, worker, InfraCommandKind.Move, C(6)).Accepted, Is.True);
            Vector2 start = worker.Position;
            world.Tick(Dt);
            float moved = Vector2.Distance(start, worker.Position);
            Assert.That(moved, Is.GreaterThan(0).And.LessThan(world.Map.CellSize));
            Assert.That(moved, Is.EqualTo(world.Rules.MoveSpeed * Dt).Within(.0001f));
            Until(world, () => worker.TaskStatus == InfraTaskStatus.Completed);
            Assert.That(worker.Position, Is.EqualTo(world.Map.CellCenter(C(6))));
        }

        [Test]
        public void StopAndReplacementNeverResumeTheOldRoute()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            Send(world, worker, InfraCommandKind.Move, C(8));
            Advance(world, 3);
            Assert.That(Send(world, worker, InfraCommandKind.Stop, worker.Cell).Accepted, Is.True);
            Vector2 stopped = worker.Position;
            Advance(world, 30);
            Assert.That(worker.Position, Is.EqualTo(stopped));
            Assert.That(worker.TaskStatus, Is.EqualTo(InfraTaskStatus.Cancelled));
            Send(world, worker, InfraCommandKind.Move, C(0, 2));
            Until(world, () => worker.TaskStatus == InfraTaskStatus.Completed);
            Advance(world, 30);
            Assert.That(worker.Position, Is.EqualTo(world.Map.CellCenter(C(0, 2))));
        }

        [Test]
        public void ActiveRouteIsCachedAndReplansBeforeCrossingNewOccupancy()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            Send(world, worker, InfraCommandKind.Move, C(8));
            world.Tick(Dt);
            int planned = world.PathPlans;
            Advance(world, 2);
            Assert.That(world.PathPlans, Is.EqualTo(planned), "Walking must use its cached route.");
            InfraEntity wall = world.AddEntity(InfraEntityKind.Wall, C(3));
            Until(world, () =>
            {
                Assert.That(worker.Cell, Is.Not.EqualTo(wall.Cell), "Crossed a newly blocked footprint.");
                return worker.TaskStatus == InfraTaskStatus.Completed;
            });
            Assert.That(world.PathPlans, Is.GreaterThan(planned));
            Assert.That(worker.Position, Is.EqualTo(world.Map.CellCenter(C(8))));
        }

        [Test]
        public void UnreachableMoveWaitsThenResumesAfterWallRemoval()
        {
            InfraWorld world = World(6, 1);
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0, 0));
            InfraEntity wall = world.AddEntity(InfraEntityKind.Wall, C(2, 0));
            Send(world, worker, InfraCommandKind.Move, C(5, 0));
            Advance(world, 10);
            Assert.That(worker.TaskStatus, Is.EqualTo(InfraTaskStatus.Blocked));
            Assert.That(worker.Position, Is.EqualTo(world.Map.CellCenter(C(0, 0))));
            Assert.That(world.PathPlans, Is.LessThan(6), "Blocked searches must be throttled.");
            world.Damage(wall.Id, wall.Health);
            Until(world, () => worker.TaskStatus == InfraTaskStatus.Completed);
            Assert.That(worker.Position, Is.EqualTo(world.Map.CellCenter(C(5, 0))));
        }

        [Test]
        public void GatherPhysicallyPicksUpCarriesAndDepositsWithoutCountingReservations()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2), stock: 13);
            InfraEntity depot = world.AddEntity(InfraEntityKind.Depot, C(8));
            Assert.That(Send(world, worker, InfraCommandKind.Gather, source.Cell, source.Id).Accepted, Is.True);
            world.Tick(Dt);
            Assert.That(source.Stored, Is.EqualTo(13));
            Assert.That(source.Reserved, Is.EqualTo(5));
            Assert.That(source.OutgoingReserved, Is.EqualTo(5));
            Assert.That(source.IncomingReserved, Is.Zero);
            Assert.That(depot.Reserved, Is.EqualTo(5));
            Assert.That(depot.IncomingReserved, Is.EqualTo(5));
            Assert.That(depot.OutgoingReserved, Is.Zero);
            Assert.That(world.TotalPhysicalMaterials, Is.EqualTo(13));
            Until(world, () => worker.Carried > 0);
            Assert.That(worker.Carried, Is.EqualTo(5));
            Assert.That(source.Stored, Is.EqualTo(8));
            Assert.That(source.Reserved, Is.Zero);
            Assert.That(depot.Stored, Is.Zero);
            Assert.That(worker.Cell, Is.Not.EqualTo(source.Cell));
            Until(world, () => worker.TaskStatus == InfraTaskStatus.Completed);
            Assert.That(source.Stored, Is.Zero);
            Assert.That(worker.Carried, Is.Zero);
            Assert.That(depot.Stored, Is.EqualTo(13));
            Assert.That(worker.Cell, Is.Not.EqualTo(depot.Cell));
            Assert.That(depot.Reserved, Is.Zero);
        }

        [Test]
        public void TwoWorkersReserveStockAndCapacityWithoutOverfillingDepot()
        {
            InfraRules rules = FastRules();
            rules.DepotCapacity = 7;
            InfraWorld world = World(rules: rules);
            InfraEntity a = world.AddEntity(InfraEntityKind.Worker, C(0, 0));
            InfraEntity b = world.AddEntity(InfraEntityKind.Worker, C(0, 2));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2), stock: 20);
            InfraEntity depot = world.AddEntity(InfraEntityKind.Depot, C(8));
            Send(world, a, InfraCommandKind.Gather, source.Cell, source.Id);
            Send(world, b, InfraCommandKind.Gather, source.Cell, source.Id);
            world.Tick(Dt);
            Assert.That(source.Reserved, Is.EqualTo(7));
            Assert.That(depot.Reserved, Is.EqualTo(7));
            Conserved(world);
            Until(world, () => depot.Stored == 7 && a.Carried == 0 && b.Carried == 0);
            Advance(world, 20);
            Assert.That(source.Stored, Is.EqualTo(13));
            Assert.That(source.Reserved, Is.Zero);
            Assert.That(depot.Reserved, Is.Zero);
            Assert.That(a.TaskStatus, Is.EqualTo(InfraTaskStatus.Blocked));
            Assert.That(b.TaskStatus, Is.EqualTo(InfraTaskStatus.Blocked));
            StringAssert.Contains("full", a.Reason);
        }

        [Test]
        public void CancellingAStockClaimLetsTheWaitingWorkerUseIt()
        {
            InfraWorld world = World();
            InfraEntity a = world.AddEntity(InfraEntityKind.Worker, C(0, 0));
            InfraEntity b = world.AddEntity(InfraEntityKind.Worker, C(0, 2));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2), stock: 5);
            InfraEntity depot = world.AddEntity(InfraEntityKind.Depot, C(8));
            Send(world, a, InfraCommandKind.Gather, source.Cell, source.Id);
            Send(world, b, InfraCommandKind.Gather, source.Cell, source.Id);
            world.Tick(Dt);
            Assert.That(source.Reserved, Is.EqualTo(5));
            Assert.That(b.TaskStatus, Is.EqualTo(InfraTaskStatus.Blocked));
            Send(world, a, InfraCommandKind.Stop, a.Cell);
            Assert.That(source.Reserved, Is.Zero);
            Assert.That(depot.Reserved, Is.Zero);
            Until(world, () => depot.Stored == 5);
            Assert.That(a.Carried, Is.Zero);
            Assert.That(source.Stored, Is.Zero);
        }

        [Test]
        public void BuildHaulsMultipleTripsAndConsumesExactlyTheWallCost()
        {
            InfraRules rules = FastRules();
            rules.CarryCapacity = 3;
            InfraWorld world = World(rules: rules);
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            InfraEntity depot = world.AddEntity(InfraEntityKind.Depot, C(1, 0), stock: 25);
            InfraCommandResult result = Send(world, worker, InfraCommandKind.Build, C(7));
            Assert.That(result.Accepted, Is.True, result.Reason);
            InfraEntity site = world.Entities[result.TargetId];
            Assert.That(site.Kind, Is.EqualTo(InfraEntityKind.Blueprint));
            int previousStock = depot.Stored;
            int pickups = 0;
            Until(world, () =>
            {
                if (depot.Stored < previousStock)
                    pickups++;
                previousStock = depot.Stored;
                return site.Kind == InfraEntityKind.Wall;
            });
            Assert.That(pickups, Is.EqualTo(4));
            Assert.That(depot.Stored, Is.EqualTo(15));
            Assert.That(site.Delivered, Is.Zero);
            Assert.That(world.ConsumedMaterials, Is.EqualTo(10));
            Assert.That(worker.Carried, Is.Zero);
            Assert.That(worker.TaskStatus, Is.EqualTo(InfraTaskStatus.Completed));
            Conserved(world);
        }

        [Test]
        public void TwoBuildersShareAResourceWithoutDoubleSpending()
        {
            InfraWorld world = World(10, 5);
            InfraEntity a = world.AddEntity(InfraEntityKind.Worker, C(0, 1));
            InfraEntity b = world.AddEntity(InfraEntityKind.Worker, C(0, 3));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2, 2), stock: 20);
            int aSite = Send(world, a, InfraCommandKind.Build, C(8, 1)).TargetId;
            int bSite = Send(world, b, InfraCommandKind.Build, C(8, 3)).TargetId;
            Until(world, () => world.Entities[aSite].Kind == InfraEntityKind.Wall &&
                world.Entities[bSite].Kind == InfraEntityKind.Wall);
            Assert.That(source.Stored, Is.Zero);
            Assert.That(source.Reserved, Is.Zero);
            Assert.That(world.ConsumedMaterials, Is.EqualTo(20));
            Assert.That(world.TotalPhysicalMaterials, Is.Zero);
        }

        [Test]
        public void DuplicateCommandsAreIdempotentAndChangedPayloadIsRejected()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            var command = new InfraCommand(44, 0, worker.Id, InfraCommandKind.Build, C(5));
            InfraCommandResult first = world.Submit(command);
            InfraCommandResult duplicate = world.Submit(command);
            Assert.That(first.Accepted, Is.True);
            Assert.That(duplicate.TargetId, Is.EqualTo(first.TargetId));
            Assert.That(duplicate.Reason, Is.EqualTo(first.Reason));
            Assert.That(world.Entities.Values.Count(e => e.Kind == InfraEntityKind.Blueprint), Is.EqualTo(1));
            InfraCommandResult conflict = world.Submit(new InfraCommand(44, 0, worker.Id, InfraCommandKind.Build, C(6)));
            Assert.That(conflict.Accepted, Is.False);
            StringAssert.Contains("reused", conflict.Reason);
            Assert.That(worker.TargetId, Is.EqualTo(first.TargetId));
            Assert.That(world.Entities[first.TargetId].IsAlive, Is.True);
            Assert.That(world.Submit(command).TargetId, Is.EqualTo(first.TargetId));
        }

        [Test]
        public void InvalidOwnerTargetAndCapabilityNeverReplaceValidWork()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            InfraEntity dino = world.AddEntity(InfraEntityKind.Dinosaur, C(0, 0), owner: 1);
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2), stock: 5);
            Assert.That(Send(world, worker, InfraCommandKind.Move, C(8)).Accepted, Is.True);
            Assert.That(world.Submit(new InfraCommand(20, 1, worker.Id, InfraCommandKind.Stop, worker.Cell)).Accepted, Is.False);
            Assert.That(Send(world, worker, InfraCommandKind.Gather, C(3), 9999).Accepted, Is.False);
            Assert.That(Send(world, worker, InfraCommandKind.Attack, dino.Cell, dino.Id).Accepted, Is.False);
            Assert.That(Send(world, dino, InfraCommandKind.Build, C(4)).Accepted, Is.False);
            Assert.That(Send(world, source, InfraCommandKind.Move, C(3)).Accepted, Is.False);
            Until(world, () => worker.TaskStatus == InfraTaskStatus.Completed);
            Assert.That(worker.Position, Is.EqualTo(world.Map.CellCenter(C(8))));
            world.Damage(worker.Id, worker.Health);
            Assert.That(Send(world, worker, InfraCommandKind.Move, C(0)).Accepted, Is.False);
            Assert.That(world.Events.Any(e => e.Message.Contains("Rejected")), Is.True);
        }

        [Test]
        public void BuildRejectsOverlapActorFootprintsAndUnreachableConstructionSides()
        {
            InfraWorld world = World(7, 3);
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            InfraEntity other = world.AddEntity(InfraEntityKind.Worker, C(1));
            Assert.That(Send(world, worker, InfraCommandKind.Build, worker.Cell).Accepted, Is.False);
            Assert.That(Send(world, worker, InfraCommandKind.Build, other.Cell).Accepted, Is.False);
            int site = Send(world, worker, InfraCommandKind.Build, C(3)).TargetId;
            Assert.That(Send(world, other, InfraCommandKind.Build, C(3)).Accepted, Is.False);
            for (int y = 0; y < 3; y++)
                world.Map.SetTile(C(4, y), InfraTileKind.Cliff);
            Assert.That(Send(world, worker, InfraCommandKind.Build, C(6)).Accepted, Is.False);
            Assert.That(world.Entities[site].IsAlive, Is.True, "Rejected order cancelled valid work.");
            Assert.That(worker.TargetId, Is.EqualTo(site));
        }

        [Test]
        public void PlacementPreviewAndBuildSubmissionUseTheSameValidation()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            InfraEntity dinosaur = world.AddEntity(InfraEntityKind.Dinosaur, C(0, 0), owner: 1);
            world.Map.SetTile(C(4), InfraTileKind.Water);
            Send(world, worker, InfraCommandKind.Move, C(8));
            int entityCount = world.Entities.Count;
            Assert.That(world.CanBuild(0, worker.Id, C(5), out string validReason), Is.True, validReason);
            Assert.That(validReason, Is.Empty);
            Assert.That(world.Entities.Count, Is.EqualTo(entityCount), "Preview created a blueprint.");
            Assert.That(worker.TaskStatus, Is.EqualTo(InfraTaskStatus.Queued), "Preview replaced active work.");
            var invalid = new[]
            {
                new InfraCommand(nextCommand++, -1, worker.Id, InfraCommandKind.Build, C(5)),
                new InfraCommand(nextCommand++, 1, worker.Id, InfraCommandKind.Build, C(5)),
                new InfraCommand(nextCommand++, 0, 9999, InfraCommandKind.Build, C(5)),
                new InfraCommand(nextCommand++, 1, dinosaur.Id, InfraCommandKind.Build, C(5)),
                new InfraCommand(nextCommand++, 0, worker.Id, InfraCommandKind.Build, worker.Cell),
                new InfraCommand(nextCommand++, 0, worker.Id, InfraCommandKind.Build, C(4)),
                new InfraCommand(nextCommand++, 0, worker.Id, InfraCommandKind.Build, C(9))
            };
            foreach (InfraCommand command in invalid)
            {
                Assert.That(world.CanBuild(command.Owner, command.ActorId, command.Cell, out string reason), Is.False);
                InfraCommandResult result = world.Submit(command);
                Assert.That(result.Accepted, Is.False);
                Assert.That(result.Reason, Is.EqualTo(reason));
                Assert.That(worker.TaskStatus, Is.EqualTo(InfraTaskStatus.Queued));
            }
            InfraCommandResult build = Send(world, worker, InfraCommandKind.Build, C(5));
            Assert.That(build.Accepted, Is.True, build.Reason);
            Assert.That(world.CanBuild(0, worker.Id, C(5), out string overlap), Is.False);
            StringAssert.Contains("reserved", overlap);
        }

        [Test]
        public void PublicWalkabilityIncludesTerrainAndFootprintsButNotActorsOrPiles()
        {
            InfraWorld world = World();
            world.AddEntity(InfraEntityKind.Worker, C(0));
            world.AddEntity(InfraEntityKind.GroundPile, C(1), stock: 5);
            InfraEntity wall = world.AddEntity(InfraEntityKind.Wall, C(2));
            world.Map.SetTile(C(3), InfraTileKind.Cliff);
            world.Map.SetTile(C(4), InfraTileKind.Water);
            Assert.That(world.IsWalkable(C(0)), Is.True);
            Assert.That(world.IsWalkable(C(1)), Is.True);
            Assert.That(world.IsWalkable(C(2)), Is.False);
            Assert.That(world.IsWalkable(C(3)), Is.False);
            Assert.That(world.IsWalkable(C(4)), Is.False);
            Assert.That(world.IsWalkable(C(-1)), Is.False);
            Assert.That(world.IsWalkable(C(9)), Is.False);
            world.Damage(wall.Id, wall.Health);
            Assert.That(world.IsWalkable(C(2)), Is.True);
            Conserved(world);
        }

        [Test]
        public void CancelAfterPickupKeepsCarriedGoodsAndGatherCanDeliverThem()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2), stock: 20);
            InfraEntity depot = world.AddEntity(InfraEntityKind.Depot, C(8));
            int site = Send(world, worker, InfraCommandKind.Build, C(6)).TargetId;
            Until(world, () => worker.Carried > 0);
            Assert.That(Send(world, worker, InfraCommandKind.CancelBuild, C(6), site).Accepted, Is.True);
            Assert.That(worker.Carried, Is.EqualTo(5));
            Assert.That(worker.TaskStatus, Is.EqualTo(InfraTaskStatus.Cancelled));
            Assert.That(world.Entities[site].IsAlive, Is.False);
            Assert.That(world.Entities[site].Reserved, Is.Zero);
            Assert.That(source.Reserved, Is.Zero);
            Conserved(world);
            Send(world, worker, InfraCommandKind.Gather, source.Cell, source.Id);
            Until(world, () => worker.TaskStatus == InfraTaskStatus.Completed);
            Assert.That(depot.Stored, Is.EqualTo(20));
            Assert.That(world.ConsumedMaterials, Is.Zero);
        }

        [Test]
        public void CancelDeliveredBlueprintDropsRecoverableUnconsumedMaterials()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2), stock: 10);
            InfraEntity depot = world.AddEntity(InfraEntityKind.Depot, C(8));
            int id = Send(world, worker, InfraCommandKind.Build, C(6)).TargetId;
            InfraEntity site = world.Entities[id];
            Until(world, () => site.Delivered == 5);
            Send(world, worker, InfraCommandKind.CancelBuild, site.Cell, id);
            InfraEntity pile = world.Entities.Values.Single(e => e.IsAlive && e.Kind == InfraEntityKind.GroundPile);
            Assert.That(pile.Stored, Is.EqualTo(5));
            Assert.That(site.Delivered, Is.Zero);
            Assert.That(world.ConsumedMaterials, Is.Zero);
            Conserved(world);
            Send(world, worker, InfraCommandKind.Gather, pile.Cell, pile.Id);
            Until(world, () => worker.TaskStatus == InfraTaskStatus.Completed);
            Assert.That(depot.Stored, Is.EqualTo(5));
            Assert.That(source.Stored, Is.EqualTo(5));
        }

        [Test]
        public void CancellationAfterConsumptionDoesNotRefundConstruction()
        {
            InfraRules rules = FastRules();
            rules.BuildSeconds = 2;
            InfraWorld world = World(rules: rules);
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            world.AddEntity(InfraEntityKind.Depot, C(1, 0), stock: 10);
            int id = Send(world, worker, InfraCommandKind.Build, C(5)).TargetId;
            Until(world, () => world.ConsumedMaterials == 10);
            Assert.That(world.Entities[id].Kind, Is.EqualTo(InfraEntityKind.Blueprint));
            Send(world, worker, InfraCommandKind.CancelBuild, C(5), id);
            Assert.That(world.TotalPhysicalMaterials, Is.Zero);
            Assert.That(world.ConsumedMaterials, Is.EqualTo(10));
            Assert.That(world.Entities.Values.Any(e => e.IsAlive && e.Kind == InfraEntityKind.GroundPile), Is.False);
            Conserved(world);
        }

        [Test]
        public void StopBuildingCancelsItsFootprintAndPreservesDeliveredMaterials()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            world.AddEntity(InfraEntityKind.Source, C(2), stock: 10);
            int id = Send(world, worker, InfraCommandKind.Build, C(6)).TargetId;
            InfraEntity site = world.Entities[id];
            Until(world, () => site.Delivered == 5);
            Send(world, worker, InfraCommandKind.Stop, worker.Cell);
            Assert.That(worker.TaskStatus, Is.EqualTo(InfraTaskStatus.Cancelled));
            Assert.That(site.IsAlive, Is.False);
            Assert.That(site.TaskStatus, Is.EqualTo(InfraTaskStatus.Cancelled));
            Assert.That(world.Entities.Values.Where(e => e.Kind == InfraEntityKind.GroundPile).Sum(e => e.Stored),
                Is.EqualTo(5));
            Send(world, worker, InfraCommandKind.Move, site.Cell);
            Until(world, () => worker.TaskStatus == InfraTaskStatus.Completed);
            Assert.That(worker.Position, Is.EqualTo(site.Position), "Cancelled footprint should be walkable.");
            Conserved(world);
        }

        [Test]
        public void NextBuildUsesExistingCargoAndKeepsTheExcess()
        {
            InfraRules rules = FastRules();
            rules.WallCost = 3;
            InfraWorld world = World(rules: rules);
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2), stock: 10);
            InfraEntity depot = world.AddEntity(InfraEntityKind.Depot, C(8));
            Send(world, worker, InfraCommandKind.Gather, source.Cell, source.Id);
            Until(world, () => worker.Carried == 5);
            int site = Send(world, worker, InfraCommandKind.Build, C(6)).TargetId;
            Assert.That(depot.Reserved, Is.Zero, "Replacement must release old incoming capacity claims.");
            Until(world, () => world.Entities[site].Kind == InfraEntityKind.Wall);
            Assert.That(worker.Carried, Is.EqualTo(2));
            Assert.That(source.Stored, Is.EqualTo(5), "Existing cargo must precede any new pickup.");
            Assert.That(depot.Stored, Is.Zero);
            Assert.That(world.ConsumedMaterials, Is.EqualTo(3));
            Conserved(world);
        }

        [Test]
        public void GatherWaitsForAnOwnedDepotBeforeReservingOrTakingStock()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0), owner: 2);
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2), stock: 5);
            InfraEntity foreignDepot = world.AddEntity(InfraEntityKind.Depot, C(8), owner: 0);
            Send(world, worker, InfraCommandKind.Gather, source.Cell, source.Id);
            Advance(world, 20);
            Assert.That(worker.TaskStatus, Is.EqualTo(InfraTaskStatus.Blocked));
            Assert.That(worker.Carried, Is.Zero);
            Assert.That(source.Stored, Is.EqualTo(5));
            Assert.That(source.Reserved, Is.Zero);
            InfraEntity ownedDepot = world.AddEntity(InfraEntityKind.Depot, C(7, 0), owner: 2);
            Until(world, () => worker.TaskStatus == InfraTaskStatus.Completed);
            Assert.That(ownedDepot.Stored, Is.EqualTo(5));
            Assert.That(foreignDepot.Stored, Is.Zero);
        }

        [Test]
        public void WorkerDeathDropsCargoAndReleasesAllClaims()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2), stock: 13);
            InfraEntity depot = world.AddEntity(InfraEntityKind.Depot, C(8));
            Send(world, worker, InfraCommandKind.Gather, source.Cell, source.Id);
            Until(world, () => worker.Carried == 5);
            Vector2Int deathCell = worker.Cell;
            world.Damage(worker.Id, worker.Health);
            InfraEntity pile = world.Entities.Values.Single(e => e.IsAlive && e.Kind == InfraEntityKind.GroundPile);
            Assert.That(pile.Cell, Is.EqualTo(deathCell));
            Assert.That(pile.Stored, Is.EqualTo(5));
            Assert.That(worker.Carried, Is.Zero);
            Assert.That(worker.TaskStatus, Is.EqualTo(InfraTaskStatus.Failed));
            Assert.That(source.Reserved, Is.Zero);
            Assert.That(depot.Reserved, Is.Zero);
            Conserved(world);
            InfraEntity rescuer = world.AddEntity(InfraEntityKind.Worker, C(0, 2));
            Send(world, rescuer, InfraCommandKind.Gather, pile.Cell, pile.Id);
            Until(world, () => rescuer.TaskStatus == InfraTaskStatus.Completed);
            Assert.That(depot.Stored, Is.EqualTo(5));
            Assert.That(world.InitialMaterials, Is.EqualTo(13));
        }

        [Test]
        public void SourceDestructionPreservesStockAndBuilderReplansToItsPile()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2), stock: 10);
            int site = Send(world, worker, InfraCommandKind.Build, C(6)).TargetId;
            world.Tick(Dt);
            Assert.That(source.Reserved, Is.EqualTo(5));
            world.Damage(source.Id, source.Health);
            Assert.That(source.Stored, Is.Zero);
            Assert.That(source.Reserved, Is.Zero);
            Conserved(world);
            Until(world, () => world.Entities[site].Kind == InfraEntityKind.Wall);
            Assert.That(world.ConsumedMaterials, Is.EqualTo(10));
            Assert.That(world.TotalPhysicalMaterials, Is.Zero);
        }

        [Test]
        public void DepotDestructionKeepsCargoAndResumesWhenAReplacementAppears()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2), stock: 5);
            InfraEntity depot = world.AddEntity(InfraEntityKind.Depot, C(8), stock: 3);
            Send(world, worker, InfraCommandKind.Gather, source.Cell, source.Id);
            Until(world, () => worker.Carried == 5);
            world.Damage(depot.Id, depot.Health);
            Advance(world, 10);
            Assert.That(worker.Carried, Is.EqualTo(5));
            Assert.That(worker.TaskStatus, Is.EqualTo(InfraTaskStatus.Blocked));
            StringAssert.Contains("No owned depot", worker.Reason);
            Assert.That(depot.Reserved, Is.Zero);
            InfraEntity replacement = world.AddEntity(InfraEntityKind.Depot, C(8));
            Until(world, () => worker.TaskStatus == InfraTaskStatus.Completed);
            Assert.That(replacement.Stored, Is.EqualTo(5));
            Assert.That(world.Entities.Values.Where(e => e.Kind == InfraEntityKind.GroundPile).Sum(e => e.Stored),
                Is.EqualTo(3));
            Assert.That(world.InitialMaterials, Is.EqualTo(8));
        }

        [Test]
        public void TerrainRevisionBlocksDeliveryWithoutLosingCargoThenReplans()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2), stock: 5);
            InfraEntity depot = world.AddEntity(InfraEntityKind.Depot, C(8));
            Send(world, worker, InfraCommandKind.Gather, source.Cell, source.Id);
            Until(world, () => worker.Carried == 5);
            for (int y = 0; y < 3; y++)
                world.Map.SetTile(C(4, y), InfraTileKind.Water);
            Advance(world, 10);
            Assert.That(worker.TaskStatus, Is.EqualTo(InfraTaskStatus.Blocked));
            Assert.That(worker.Carried, Is.EqualTo(5));
            Assert.That(worker.Cell.x, Is.LessThan(4));
            Assert.That(depot.Stored, Is.Zero);
            Assert.That(depot.Reserved, Is.Zero);
            world.Map.SetTile(C(4, 1), InfraTileKind.Ground);
            Until(world, () => worker.TaskStatus == InfraTaskStatus.Completed);
            Assert.That(depot.Stored, Is.EqualTo(5));
        }

        [Test]
        public void DinosaurCannotAttackAcrossCliffOrDestroyAnUnhelpfulWall()
        {
            InfraWorld world = World(6, 1);
            InfraEntity dino = world.AddEntity(InfraEntityKind.Dinosaur, C(0, 0), owner: 1);
            InfraEntity wall = world.AddEntity(InfraEntityKind.Wall, C(1, 0));
            world.Damage(wall.Id, wall.Health - 1);
            world.Map.SetTile(C(2, 0), InfraTileKind.Cliff);
            InfraEntity target = world.AddEntity(InfraEntityKind.Worker, C(5, 0));
            Send(world, dino, InfraCommandKind.Attack, target.Cell, target.Id);
            Advance(world, 100);
            Assert.That(target.Health, Is.EqualTo(world.Rules.WorkerHealth));
            Assert.That(wall.Health, Is.EqualTo(1), "No useful route exists through this wall.");
            Assert.That(dino.TaskStatus, Is.EqualTo(InfraTaskStatus.Blocked));
            Assert.That(dino.Position, Is.EqualTo(world.Map.CellCenter(C(0, 0))));
        }

        [Test]
        public void DinosaurBreachesTheUsefulRouteInsteadOfAnUnrelatedWeakWallThenResumes()
        {
            InfraWorld world = World(9, 5);
            for (int x = 0; x < 9; x++)
                for (int y = 0; y < 5; y++)
                    world.Map.SetTile(C(x, y), InfraTileKind.Cliff);
            for (int x = 0; x < 9; x++)
                world.Map.SetTile(C(x, 2), InfraTileKind.Ground);
            world.Map.SetTile(C(1, 1), InfraTileKind.Ground);
            world.Map.SetTile(C(1, 0), InfraTileKind.Ground);
            InfraEntity dino = world.AddEntity(InfraEntityKind.Dinosaur, C(0, 2), owner: 1);
            InfraEntity target = world.AddEntity(InfraEntityKind.Worker, C(8, 2));
            InfraEntity useful = world.AddEntity(InfraEntityKind.Wall, C(4, 2));
            InfraEntity unrelated = world.AddEntity(InfraEntityKind.Wall, C(1, 0));
            world.Damage(unrelated.Id, unrelated.Health - 1);
            Send(world, dino, InfraCommandKind.Attack, target.Cell, target.Id);
            Until(world, () => useful.Health < world.Rules.WallHealth);
            Assert.That(dino.Position, Is.EqualTo(world.Map.CellCenter(C(3, 2))),
                "Breach damage must come from the reachable adjacent route cell.");
            Assert.That(target.Health, Is.EqualTo(world.Rules.WorkerHealth));
            Until(world, () => !target.IsAlive);
            Assert.That(useful.IsAlive, Is.False);
            Assert.That(unrelated.Health, Is.EqualTo(1));
            Assert.That(dino.TaskStatus, Is.EqualTo(InfraTaskStatus.Completed));
            Assert.That(dino.Cell.x, Is.GreaterThan(4), "Dinosaur did not resume through the breach.");
        }

        [Test]
        public void CampDemoGathersOutsideBuildsGateFromInsideAndDinosaurBreachesIntoCamp()
        {
            InfraWorld world = World(11, 5);
            for (int y = 0; y < 5; y++)
                if (y != 2)
                    world.Map.SetTile(C(5, y), InfraTileKind.Cliff);
            InfraEntity builder = world.AddEntity(InfraEntityKind.Worker, C(8, 2));
            InfraEntity survivor = world.AddEntity(InfraEntityKind.Worker, C(9, 3));
            InfraEntity depot = world.AddEntity(InfraEntityKind.Depot, C(9, 1));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(2, 1), stock: 20);
            Send(world, builder, InfraCommandKind.Gather, source.Cell, source.Id);
            Until(world, () => depot.Stored >= world.Rules.WallCost);
            Send(world, builder, InfraCommandKind.Stop, builder.Cell);
            Assert.That(builder.Cell.x, Is.GreaterThan(5), "Hauler should be inside after depositing.");
            Assert.That(world.CanBuild(0, builder.Id, C(5, 2), out string reason), Is.True, reason);
            InfraCommandResult build = Send(world, builder, InfraCommandKind.Build, C(5, 2));
            Assert.That(build.Accepted, Is.True, build.Reason);
            InfraEntity gate = world.Entities[build.TargetId];
            Assert.That(world.IsWalkable(gate.Cell), Is.False, "Blueprint must reserve the gate immediately.");
            Until(world, () => gate.Kind == InfraEntityKind.Wall);
            Assert.That(source.Stored, Is.EqualTo(10), "Closing gate must force construction to use the inside depot.");
            Assert.That(depot.Stored, Is.Zero);
            Assert.That(world.ConsumedMaterials, Is.EqualTo(10));
            Assert.That(builder.Cell.x, Is.GreaterThan(5));
            InfraEntity dinosaur = world.AddEntity(InfraEntityKind.Dinosaur, C(0, 2), owner: 1);
            Send(world, dinosaur, InfraCommandKind.Attack, survivor.Cell, survivor.Id);
            Until(world, () => survivor.Health < world.Rules.WorkerHealth);
            Assert.That(gate.IsAlive, Is.False);
            Assert.That(world.IsWalkable(gate.Cell), Is.True);
            Assert.That(dinosaur.Cell.x, Is.GreaterThan(5), "Dinosaur did not enter camp after breaching.");
            Conserved(world);
        }

        [Test]
        public void WeightedBreachChoosesTheCheaperUsefulWall()
        {
            InfraRules rules = FastRules();
            rules.AttackInterval = .2f;
            InfraWorld world = World(7, 5, rules);
            for (int x = 0; x < 7; x++)
            {
                world.Map.SetTile(C(x, 0), InfraTileKind.Water);
                world.Map.SetTile(C(x, 4), InfraTileKind.Water);
            }
            InfraEntity upper = world.AddEntity(InfraEntityKind.Wall, C(3, 1));
            InfraEntity middle = world.AddEntity(InfraEntityKind.Wall, C(3, 2));
            InfraEntity cheaper = world.AddEntity(InfraEntityKind.Wall, C(3, 3));
            world.Damage(cheaper.Id, cheaper.Health - 10);
            InfraEntity dino = world.AddEntity(InfraEntityKind.Dinosaur, C(0, 2), owner: 1);
            InfraEntity target = world.AddEntity(InfraEntityKind.Worker, C(6, 2));
            Send(world, dino, InfraCommandKind.Attack, target.Cell, target.Id);
            Until(world, () => target.Health < world.Rules.WorkerHealth);
            Assert.That(cheaper.IsAlive, Is.False);
            Assert.That(upper.Health, Is.EqualTo(world.Rules.WallHealth));
            Assert.That(middle.Health, Is.EqualTo(world.Rules.WallHealth));
        }

        [Test]
        public void DinosaurDoesNotTreatUnrelatedSourcesAsDestructibleNavigation()
        {
            InfraWorld world = World(7, 1);
            InfraEntity dino = world.AddEntity(InfraEntityKind.Dinosaur, C(0, 0), owner: 1);
            InfraEntity wall = world.AddEntity(InfraEntityKind.Wall, C(2, 0));
            InfraEntity source = world.AddEntity(InfraEntityKind.Source, C(3, 0), stock: 7);
            InfraEntity target = world.AddEntity(InfraEntityKind.Worker, C(6, 0));
            Send(world, dino, InfraCommandKind.Attack, target.Cell, target.Id);
            Advance(world, 50);
            Assert.That(dino.TaskStatus, Is.EqualTo(InfraTaskStatus.Blocked));
            Assert.That(wall.Health, Is.EqualTo(world.Rules.WallHealth));
            Assert.That(source.Health, Is.EqualTo(world.Rules.StructureHealth));
            Assert.That(source.Stored, Is.EqualTo(7));
        }

        [Test]
        public void ReplacingAttackClearsPendingBreachDamageAndTheOriginalGoal()
        {
            InfraRules rules = FastRules();
            rules.AttackInterval = .5f;
            InfraWorld world = World(7, 1, rules);
            InfraEntity dino = world.AddEntity(InfraEntityKind.Dinosaur, C(0, 0), owner: 1);
            InfraEntity wall = world.AddEntity(InfraEntityKind.Wall, C(3, 0));
            InfraEntity target = world.AddEntity(InfraEntityKind.Worker, C(6, 0));
            Send(world, dino, InfraCommandKind.Attack, target.Cell, target.Id);
            Until(world, () => dino.Action.StartsWith("Breaching", StringComparison.Ordinal));
            Assert.That(wall.Health, Is.EqualTo(rules.WallHealth));
            Send(world, dino, InfraCommandKind.Move, C(0, 0));
            Until(world, () => dino.TaskStatus == InfraTaskStatus.Completed);
            Advance(world, 50);
            Assert.That(dino.Position, Is.EqualTo(world.Map.CellCenter(C(0, 0))));
            Assert.That(wall.Health, Is.EqualTo(rules.WallHealth));
            Assert.That(target.Health, Is.EqualTo(rules.WorkerHealth));
        }

        [Test]
        public void EventLogIsBoundedButIdempotencySurvivesEventEviction()
        {
            InfraRules rules = FastRules();
            rules.EventLogCapacity = 3;
            InfraWorld world = World(rules: rules);
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            var command = new InfraCommand(100, 0, worker.Id, InfraCommandKind.Build, C(5));
            InfraCommandResult first = world.Submit(command);
            for (int i = 0; i < 20; i++)
                world.Submit(new InfraCommand(i, 4, worker.Id, InfraCommandKind.Stop, worker.Cell));
            Assert.That(world.Events.Count, Is.EqualTo(3));
            Assert.That(world.Events.All(e => e.Message.Contains("Rejected")), Is.True);
            Assert.That(world.Submit(command).TargetId, Is.EqualTo(first.TargetId));
            Assert.That(world.Entities.Values.Count(e => e.Kind == InfraEntityKind.Blueprint), Is.EqualTo(1));
        }

        [Test]
        public void TickIsBoundedAndRejectsInvalidTime()
        {
            InfraWorld world = World();
            InfraEntity worker = world.AddEntity(InfraEntityKind.Worker, C(0));
            Send(world, worker, InfraCommandKind.Move, C(8));
            Vector2 start = worker.Position;
            world.Tick(1000);
            Assert.That(world.Time, Is.EqualTo(world.Rules.MaxTickSeconds));
            Assert.That(Vector2.Distance(start, worker.Position),
                Is.LessThanOrEqualTo(world.Rules.MoveSpeed * world.Rules.MaxTickSeconds + .0001f));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.Tick(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => world.Tick(-1));
        }
    }
}
