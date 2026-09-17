using System.Collections.Generic;
using System.Linq;
using JurassicPark.Simulation;
using NUnit.Framework;

namespace JurassicPark.Tests.EditMode
{
    public sealed class TaskPipelineTests
    {
        private static readonly SeatId Red = new SeatId(1);
        private const float Speed = 4f; // metres per second: two cells a second on the 2 m fixture grid

        private World world;
        private GridMap map;
        private TaskSystem tasks;
        private CommandRouter router;
        private CommandSender sender;
        private readonly List<SimEvent> log = new List<SimEvent>();

        [SetUp]
        public void SetUp() => Build(FixtureMaps.CampValleyGrid(), maxReplans: 3, new PathOptions());

        private void Build(GridMap grid, int maxReplans, PathOptions pathOptions)
        {
            log.Clear();
            world = new World(new SimConfig(10, 8, 1));
            map = grid;
            var catalog = new DefinitionCatalog();
            catalog.Add(new EntityDefinition("survivor", Speed));
            catalog.Add(new EntityDefinition("depot", 0f));
            var config = new TaskConfig(replanIntervalTicks: 5, maxReplans, maxQueuedPerActor: 2, pathOptions);
            tasks = new TaskSystem(new TaskContext(world, map, catalog, config));
            var seats = new SeatRegistry(world);
            seats.Add(Red, "Red", 1, SeatController.Human);
            router = new CommandRouter(seats, new CommandRouterConfig(8, 16, 8));
            router.Register(CommandKind.Move, new MoveCommandHandler(tasks));
            router.Register(CommandKind.Stop, new StopCommandHandler(tasks));
            world.AddSystem(router);
            world.AddSystem(tasks);
            sender = new CommandSender(router, Red, 1);
        }

        private Entity Survivor(Cell cell)
        {
            Entity entity = world.Spawn(EntityKind.Unit, "survivor", Red, map.CenterOf(cell));
            world.Commit();
            return entity;
        }

        private void Run(int ticks)
        {
            for (int i = 0; i < ticks; i++)
            {
                world.Step();
                log.AddRange(world.DrainEvents());
            }
        }

        private IEnumerable<TaskStateChanged> TaskLog(Entity actor) => log.OfType<TaskStateChanged>().Where(e => e.Actor == actor.Id);

        private void Wall(params Cell[] cells)
        {
            Entity wall = world.Spawn(EntityKind.Building, "wall", Red, map.CenterOf(cells[0]));
            Assert.That(map.TryOccupy(cells, wall.Id, destructible: true), Is.True);
        }

        [Test]
        public void AMoveOrderWalksTheUnitToTheExactPointAtItsSpeed()
        {
            Entity unit = Survivor(new Cell(1, 1));
            SimVector2 target = map.CenterOf(new Cell(6, 1)) + new SimVector2(0.5f, -0.25f);
            sender.Send(CommandKind.Move, new[] { unit.Id }, target);

            Run(1);
            Assert.That(tasks.CurrentOf(unit.Id), Is.InstanceOf<MoveTask>(), "accepted and started in the same tick");
            Assert.That(SimVector2.Distance(unit.Position, map.CenterOf(new Cell(1, 1))), Is.EqualTo(Speed * 0.1f).Within(1e-4f));

            Run(40);
            Assert.That(unit.Position, Is.EqualTo(target));
            Assert.That(tasks.CurrentOf(unit.Id), Is.Null);
            Assert.That(TaskLog(unit).Select(e => e.State), Is.EqualTo(new[] { TaskState.Planning, TaskState.Running, TaskState.Completed }));
            Assert.That(TaskLog(unit).Last().Reason, Is.EqualTo(TaskReason.Arrived));
        }

        [Test]
        public void ArrivalTimeMatchesDistanceOverSpeed()
        {
            Entity unit = Survivor(new Cell(1, 1));
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(new Cell(11, 1)));

            // 10 cells of 2 m at 4 m/s is 5 s, which is 50 ticks at 10 Hz.
            Run(49);
            Assert.That(tasks.CurrentOf(unit.Id), Is.Not.Null);
            Run(2);
            Assert.That(tasks.CurrentOf(unit.Id), Is.Null);
        }

        [Test]
        public void TheRouteGoesThroughTheEntranceNotThroughTheCliff()
        {
            Entity unit = Survivor(new Cell(14, 5));
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(FixtureMaps.CampGround));

            var visited = new HashSet<Cell>();
            for (int i = 0; i < 200 && (i == 0 || tasks.CurrentOf(unit.Id) != null); i++)
            {
                Run(1);
                Cell at = map.CellAt(unit.Position);
                Assert.That(map.IsStaticWalkable(at), Is.True, $"walked onto {at}");
                visited.Add(at);
            }

            Assert.That(visited, Does.Contain(FixtureMaps.MainEntrance));
            Assert.That(map.CellAt(unit.Position), Is.EqualTo(FixtureMaps.CampGround));
        }

        [Test]
        public void ANewOrderReplacesTheOldOneWhichNeverResumes()
        {
            Entity unit = Survivor(new Cell(1, 1));
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(new Cell(13, 1)));
            Run(5);
            TaskId first = tasks.CurrentOf(unit.Id).Id;
            SimVector2 second = map.CenterOf(new Cell(1, 9));
            sender.Send(CommandKind.Move, new[] { unit.Id }, second);
            Run(100);

            Assert.That(unit.Position, Is.EqualTo(second));
            TaskStateChanged[] firstLog = log.OfType<TaskStateChanged>().Where(e => e.Task == first).ToArray();
            Assert.That(firstLog.Last().State, Is.EqualTo(TaskState.Cancelled));
            Assert.That(firstLog.Last().Reason, Is.EqualTo(TaskReason.ReplacedByNewCommand));
        }

        [Test]
        public void StopEndsTheTaskAndTheUnitStaysWhereItIs()
        {
            Entity unit = Survivor(new Cell(1, 1));
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(new Cell(13, 1)));
            Run(5);
            sender.Send(CommandKind.Stop, new[] { unit.Id });
            Run(1);
            SimVector2 stoppedAt = unit.Position;
            Run(20);

            Assert.That(unit.Position, Is.EqualTo(stoppedAt));
            Assert.That(tasks.CurrentOf(unit.Id), Is.Null);
            Assert.That(TaskLog(unit).Last().Reason, Is.EqualTo(TaskReason.Stopped));
        }

        [Test]
        public void StoppingAnIdleUnitIsAcceptedAndDoesNothing()
        {
            Entity unit = Survivor(new Cell(1, 1));
            sender.Send(CommandKind.Stop, new[] { unit.Id });
            Run(1);

            Assert.That(log.OfType<CommandResolved>().Single().Accepted, Is.True);
            Assert.That(TaskLog(unit), Is.Empty);
        }

        [Test]
        public void QueuedOrdersRunOneAfterAnother()
        {
            Entity unit = Survivor(new Cell(1, 1));
            SimVector2 a = map.CenterOf(new Cell(3, 1)), c = map.CenterOf(new Cell(1, 1));
            sender.Send(CommandKind.Move, new[] { unit.Id }, a);
            sender.Send(CommandKind.Move, new[] { unit.Id }, c, mode: CommandMode.Queue);
            Run(1);
            Assert.That(tasks.QueuedCountOf(unit.Id), Is.EqualTo(1));
            Run(40);

            Assert.That(unit.Position, Is.EqualTo(c));
            Assert.That(TaskLog(unit).Count(e => e.State == TaskState.Completed), Is.EqualTo(2));
        }

        [Test]
        public void TheQueueIsBoundedAndAReplaceOrderClearsIt()
        {
            Entity unit = Survivor(new Cell(1, 1));
            SimVector2 far = map.CenterOf(new Cell(13, 1));
            sender.Send(CommandKind.Move, new[] { unit.Id }, far);
            for (int i = 0; i < 4; i++) sender.Send(CommandKind.Move, new[] { unit.Id }, far, mode: CommandMode.Queue);
            Run(1);
            Assert.That(tasks.QueuedCountOf(unit.Id), Is.EqualTo(2));
            Assert.That(log.OfType<CommandResolved>().Select(r => r.Rejection), Is.EqualTo(new[]
            {
                CommandRejection.None, CommandRejection.None, CommandRejection.None, CommandRejection.QueueFull, CommandRejection.QueueFull,
            }), "an order the task system refused must not be answered Accepted");

            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(new Cell(2, 1)));
            Run(1);
            Assert.That(tasks.QueuedCountOf(unit.Id), Is.EqualTo(0));
        }

        [Test]
        public void AnOrderOntoACliffGoesAsCloseAsTheGroundAllows()
        {
            Entity unit = Survivor(new Cell(1, 1));
            var cliff = new Cell(3, 5);
            Assert.That(map.IsStaticWalkable(cliff), Is.False);
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(cliff));
            Run(100);

            Cell at = map.CellAt(unit.Position);
            Assert.That(TaskLog(unit).Last().State, Is.EqualTo(TaskState.Completed));
            Assert.That(System.Math.Max(System.Math.Abs(at.X - cliff.X), System.Math.Abs(at.Y - cliff.Y)), Is.EqualTo(1));
            Assert.That(map.IsWalkable(at), Is.True);
        }

        [Test]
        public void AnOrderOffTheMapIsRejectedAsInvalidTarget()
        {
            Entity unit = Survivor(new Cell(1, 1));
            sender.Send(CommandKind.Move, new[] { unit.Id }, new SimVector2(-50f, 3f));
            Run(1);

            Assert.That(log.OfType<CommandResolved>().Single().Rejection, Is.EqualTo(CommandRejection.InvalidTarget));
            Assert.That(tasks.CurrentOf(unit.Id), Is.Null);
        }

        [Test]
        public void ABuildingCaughtInASelectionDoesNotVoidTheOrderButABuildingAloneIsRefused()
        {
            Entity unit = Survivor(new Cell(1, 1));
            Entity depot = world.Spawn(EntityKind.Building, "depot", Red, map.CenterOf(new Cell(2, 9)));
            world.Commit();

            sender.Send(CommandKind.Move, new[] { unit.Id, depot.Id }, map.CenterOf(new Cell(4, 1)));
            sender.Send(CommandKind.Move, new[] { depot.Id }, map.CenterOf(new Cell(4, 1)));
            Run(1);

            Assert.That(log.OfType<CommandResolved>().Select(r => r.Rejection), Is.EqualTo(new[] { CommandRejection.None, CommandRejection.ActorsLackAbility }));
            Assert.That(tasks.CurrentOf(unit.Id), Is.Not.Null);
            Assert.That(tasks.CurrentOf(depot.Id), Is.Null);
        }

        [Test]
        public void AWallAcrossTheRouteMakesTheUnitReplanThroughTheFarGate()
        {
            Entity unit = Survivor(new Cell(14, 5));
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(FixtureMaps.CampGround));
            Run(2);
            Wall(FixtureMaps.MainEntrance);

            var visited = new HashSet<Cell>();
            for (int i = 0; i < 400 && tasks.CurrentOf(unit.Id) != null; i++)
            {
                Run(1);
                Cell at = map.CellAt(unit.Position);
                Assert.That(at, Is.Not.EqualTo(FixtureMaps.MainEntrance), "never walks through the new wall");
                visited.Add(at);
            }

            Assert.That(visited, Does.Contain(FixtureMaps.DetourGate));
            Assert.That(map.CellAt(unit.Position), Is.EqualTo(FixtureMaps.CampGround));
            Assert.That(TaskLog(unit).Any(e => e.State == TaskState.Planning && e.Reason == TaskReason.RouteBlocked), Is.True);
        }

        [Test]
        public void AMapChangeThatDoesNotTouchTheRouteCausesNoReplan()
        {
            Entity unit = Survivor(new Cell(1, 1));
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(new Cell(13, 1)));
            Run(2);
            Wall(new Cell(1, 10));
            Run(80);

            Assert.That(TaskLog(unit).Select(e => e.State), Is.EqualTo(new[] { TaskState.Planning, TaskState.Running, TaskState.Completed }));
        }

        [Test]
        public void WithEveryWayInWalledOffTheMoveFailsWithNoRouteInsteadOfWaitingForever()
        {
            Entity unit = Survivor(new Cell(14, 5));
            Wall(FixtureMaps.MainEntrance);
            Wall(FixtureMaps.DetourGate);
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(FixtureMaps.CampGround));
            Run(3);

            Assert.That(TaskLog(unit).Last().State, Is.EqualTo(TaskState.Failed));
            Assert.That(TaskLog(unit).Last().Reason, Is.EqualTo(TaskReason.NoRoute));
            Assert.That(tasks.CurrentOf(unit.Id), Is.Null);
        }

        [Test]
        public void AnActorRemovedByALaterSystemStillGetsItsTasksCancelled()
        {
            Entity unit = Survivor(new Cell(1, 1));
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(new Cell(13, 1)));
            Run(3);
            world.Despawn(unit.Id, "eaten");
            world.Commit(); // gone from Entities before the task system sees another tick
            Run(1);

            Assert.That(TaskLog(unit).Last().State, Is.EqualTo(TaskState.Cancelled));
            Assert.That(TaskLog(unit).Last().Reason, Is.EqualTo(TaskReason.ActorRemoved));
            Assert.That(tasks.CurrentOf(unit.Id), Is.Null);
        }

        private sealed class HoldingTask : SimTask
        {
            public int Released;
            public bool FailNow, CompleteNow;
            public override string Kind => "hold";
            protected override void Tick(TaskContext context, Entity actor)
            {
                if (State == TaskState.Planning) Enter(context, TaskState.Running);
                if (FailNow) Enter(context, TaskState.Failed, TaskReason.NoRoute);
                if (CompleteNow) Enter(context, TaskState.Completed, TaskReason.Arrived);
            }
            protected override void Release(TaskContext context) => Released++;
        }

        [Test]
        public void ReleaseRunsExactlyOnceOnEveryKindOfEnding()
        {
            Entity a = Survivor(new Cell(1, 1)), b = Survivor(new Cell(2, 1)), c = Survivor(new Cell(3, 1));
            var replaced = new HoldingTask(); var failed = new HoldingTask(); var queued = new HoldingTask(); var orphaned = new HoldingTask();
            tasks.Assign(a.Id, replaced, CommandMode.Replace);
            tasks.Assign(b.Id, failed, CommandMode.Replace);
            tasks.Assign(b.Id, queued, CommandMode.Queue);
            tasks.Assign(c.Id, orphaned, CommandMode.Replace);
            Run(1);

            tasks.Assign(a.Id, new HoldingTask(), CommandMode.Replace);
            failed.FailNow = true;
            world.Despawn(c.Id, "eaten");
            Run(2);

            Entity d = Survivor(new Cell(4, 1)), e = Survivor(new Cell(5, 1));
            var stopped = new HoldingTask(); var completed = new HoldingTask();
            tasks.Assign(d.Id, stopped, CommandMode.Replace);
            tasks.Assign(e.Id, completed, CommandMode.Replace);
            Run(1);
            tasks.Stop(d.Id, TaskReason.Stopped);
            tasks.Stop(d.Id, TaskReason.Stopped);
            completed.CompleteNow = true;
            Run(2);

            Assert.That(new[] { replaced.Released, failed.Released, queued.Released, orphaned.Released, stopped.Released, completed.Released }, Is.All.EqualTo(1));
            Assert.That(completed.State, Is.EqualTo(TaskState.Completed));
            Assert.That(tasks.TrackedActorCount, Is.EqualTo(1), "only the actor still holding a task is tracked");
            Assert.That(queued.State, Is.EqualTo(TaskState.Cancelled));
            Assert.That(queued.Reason, Is.EqualTo(TaskReason.PreviousTaskDidNotComplete));
        }

        [Test]
        public void TwoRunsFromTheSameSetupProduceIdenticalPositionsAndTaskEvents()
        {
            List<string> Trace()
            {
                SetUp();
                Entity one = Survivor(new Cell(14, 5)), two = Survivor(new Cell(1, 10));
                sender.Send(CommandKind.Move, new[] { one.Id, two.Id }, map.CenterOf(FixtureMaps.CampGround));
                var trace = new List<string>();
                for (int i = 0; i < 120; i++)
                {
                    world.Step();
                    trace.Add($"{one.Position.X:R},{one.Position.Y:R};{two.Position.X:R},{two.Position.Y:R}");
                    foreach (TaskStateChanged e in world.DrainEvents().OfType<TaskStateChanged>())
                        trace.Add($"{e.Tick}:{e.Task}:{e.Actor}:{e.State}:{e.Reason}");
                }
                return trace;
            }

            Assert.That(Trace(), Is.EqualTo(Trace()));
        }

        /// <summary>Walks the straight line between two tick positions in 1 cm steps and fails on the first step that is inside a blocked cell.</summary>
        private void AssertNeverInsideABlocker(SimVector2 from, SimVector2 to)
        {
            float length = SimVector2.Distance(from, to);
            int samples = System.Math.Max(1, (int)(length / 0.01f));
            for (int i = 0; i <= samples; i++)
            {
                SimVector2 p = from + (to - from) * (i / (float)samples);
                Assert.That(map.IsWalkable(map.CellAt(p)), Is.True, $"inside blocked cell {map.CellAt(p)} at {p}");
            }
        }

        [Test]
        public void AWallBesideTheRouteThatTheFinalOffCentreLegWouldCrossIsNeverWalkedThrough()
        {
            Build(FixtureMaps.OpenGrid(12, 12), maxReplans: 3, new PathOptions());
            Entity unit = Survivor(new Cell(1, 1));
            var destination = new SimVector2(10.05f, 11.95f); // off-centre inside cell (5,5), towards cell (4,5)
            sender.Send(CommandKind.Move, new[] { unit.Id }, destination);
            Run(2);
            Wall(new Cell(4, 5));

            for (int i = 0; i < 200 && tasks.CurrentOf(unit.Id) != null; i++)
            {
                SimVector2 before = unit.Position;
                Run(1);
                AssertNeverInsideABlocker(before, unit.Position);
            }

            Assert.That(unit.Position, Is.EqualTo(destination));
        }

        [Test]
        public void AWallBuiltInTheSideCellOfAnOffCentreDiagonalLegIsNeverWalkedThrough()
        {
            Build(FixtureMaps.OpenGrid(12, 12), maxReplans: 3, new PathOptions());
            // Standing at the west edge of cell (1,1) and ordered diagonally: the straight first leg to the centre of (2,2)
            // leaves (1,1) through its top edge at x = 3.82, so it crosses the side cell (1,2) about six ticks from now.
            Entity unit = world.Spawn(EntityKind.Unit, "survivor", Red, new SimVector2(2.05f, 2.5f));
            world.Commit();
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(new Cell(6, 6)));
            Run(1);
            Assert.That(map.CellAt(unit.Position), Is.EqualTo(new Cell(1, 1)));
            Wall(new Cell(1, 2));

            for (int i = 0; i < 200 && tasks.CurrentOf(unit.Id) != null; i++)
            {
                SimVector2 before = unit.Position;
                Run(1);
                AssertNeverInsideABlocker(before, unit.Position);
            }

            Assert.That(map.CellAt(unit.Position), Is.EqualTo(new Cell(6, 6)));
        }

        [Test]
        public void AnEntityThatCannotMoveFailsItsMoveTaskWithThatReason()
        {
            Entity depot = world.Spawn(EntityKind.Building, "depot", Red, map.CenterOf(new Cell(2, 9)));
            world.Commit();
            tasks.Assign(depot.Id, new MoveTask(map.CenterOf(new Cell(4, 9))), CommandMode.Replace);
            Run(1);

            Assert.That(TaskLog(depot).Last().State, Is.EqualTo(TaskState.Failed));
            Assert.That(TaskLog(depot).Last().Reason, Is.EqualTo(TaskReason.ActorCannotMove));
        }

        [Test]
        public void ATaskAimedOffTheMapFailsWithThatReason()
        {
            Entity unit = Survivor(new Cell(1, 1));
            tasks.Assign(unit.Id, new MoveTask(new SimVector2(-40f, 2f)), CommandMode.Replace);
            Run(1);

            Assert.That(TaskLog(unit).Last().Reason, Is.EqualTo(TaskReason.TargetOutOfBounds));
        }

        [Test]
        public void ASearchThatKeepsRunningOutOfBudgetRetriesAtALimitedRateThenFails()
        {
            Build(FixtureMaps.CampValleyGrid(), maxReplans: 2, new PathOptions(maxExpandedNodes: 3));
            Entity unit = Survivor(new Cell(14, 5));
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(FixtureMaps.CampGround));

            Run(4);
            Assert.That(tasks.CurrentOf(unit.Id).State, Is.EqualTo(TaskState.Planning), "over budget is unknown, not unreachable");
            Assert.That(TaskLog(unit).Count(e => e.Reason == TaskReason.PathSearchBudgetExceeded), Is.EqualTo(1), "one attempt so far: retries wait for the interval");

            Run(20);
            Assert.That(TaskLog(unit).Last().State, Is.EqualTo(TaskState.Failed));
            Assert.That(TaskLog(unit).Last().Reason, Is.EqualTo(TaskReason.PathSearchBudgetExceeded));
            Assert.That(tasks.CurrentOf(unit.Id), Is.Null);
        }

        [Test]
        public void WithNoReplansAllowedACutRouteFailsAsRouteBlocked()
        {
            Build(FixtureMaps.CampValleyGrid(), maxReplans: 0, new PathOptions());
            Entity unit = Survivor(new Cell(14, 5));
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(FixtureMaps.CampGround));
            Run(2);
            Wall(FixtureMaps.MainEntrance);
            Run(2);

            Assert.That(TaskLog(unit).Last().State, Is.EqualTo(TaskState.Failed));
            Assert.That(TaskLog(unit).Last().Reason, Is.EqualTo(TaskReason.RouteBlocked));
        }

        [Test]
        public void ProgressResetsTheReplanCountSoAGateToggledManyTimesDoesNotFailALongWalk()
        {
            Build(FixtureMaps.OpenGrid(40, 5), maxReplans: 1, new PathOptions());
            Entity unit = Survivor(new Cell(1, 2));
            sender.Send(CommandKind.Move, new[] { unit.Id }, map.CenterOf(new Cell(38, 2)));
            Run(1);

            // Six separate walls appear in front of the walker, each cutting the current route, each well after the last was passed.
            for (int n = 0; n < 6; n++)
            {
                Cell ahead = map.CellAt(unit.Position);
                Wall(new Cell(ahead.X + 3, 2));
                Run(25);
            }
            Run(200);

            Assert.That(TaskLog(unit).Last().State, Is.EqualTo(TaskState.Completed));
            Assert.That(TaskLog(unit).Count(e => e.Reason == TaskReason.RouteBlocked), Is.EqualTo(6));
        }

        [Test]
        public void AQueuedGroupOrderIsAcceptedWhenOneMoverHasRoomAndSkipsTheOneWhoseQueueIsFull()
        {
            Entity full = Survivor(new Cell(1, 1)), free = Survivor(new Cell(2, 1));
            SimVector2 far = map.CenterOf(new Cell(13, 1));
            sender.Send(CommandKind.Move, new[] { full.Id, free.Id }, far);
            sender.Send(CommandKind.Move, new[] { full.Id }, far, mode: CommandMode.Queue);
            sender.Send(CommandKind.Move, new[] { full.Id }, far, mode: CommandMode.Queue);
            Run(1);
            log.Clear();

            sender.Send(CommandKind.Move, new[] { full.Id, free.Id }, far, mode: CommandMode.Queue);
            Run(1);

            Assert.That(log.OfType<CommandResolved>().Single().Accepted, Is.True);
            Assert.That(tasks.QueuedCountOf(full.Id), Is.EqualTo(2));
            Assert.That(tasks.QueuedCountOf(free.Id), Is.EqualTo(1));
        }
    }
}
