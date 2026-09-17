using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>
    /// Owns every actor's current task and the short queue behind it, and steps them once per tick in the order actors first
    /// received work, so the result never depends on hash order. Register it after the command router: a command accepted this tick starts working this tick.
    /// </summary>
    public sealed class TaskSystem : ISimSystem
    {
        private sealed class ActorTasks
        {
            public SimTask Current;
            public readonly Queue<SimTask> Queue = new Queue<SimTask>();
        }

        private readonly TaskContext context;
        private readonly Dictionary<EntityId, ActorTasks> byActor = new Dictionary<EntityId, ActorTasks>();
        private readonly List<EntityId> actorOrder = new List<EntityId>();
        private long nextTaskId = 1;

        public TaskSystem(TaskContext context)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public TaskContext Context => context;

        /// <summary>The task the actor is working on, or null.</summary>
        public SimTask CurrentOf(EntityId actor) => byActor.TryGetValue(actor, out ActorTasks tasks) ? tasks.Current : null;

        /// <summary>Actors that currently have work. Idle actors are not tracked.</summary>
        public int TrackedActorCount => actorOrder.Count;

        public int QueuedCountOf(EntityId actor) => byActor.TryGetValue(actor, out ActorTasks tasks) ? tasks.Queue.Count : 0;

        /// <summary>True when Assign with this mode would take a task for the actor right now. Handlers use it in Validate so a refused order is never answered Accepted.</summary>
        public bool CanAccept(EntityId actor, CommandMode mode)
        {
            if (!context.World.IsAlive(actor)) return false;
            if (mode != CommandMode.Queue) return true;
            return !byActor.TryGetValue(actor, out ActorTasks tasks) || tasks.Current == null || tasks.Queue.Count < context.Config.MaxQueuedPerActor;
        }

        /// <summary>
        /// Gives the actor a task. Replace ends the current task and drops the queue first, so old work can never quietly resume.
        /// Queue appends, and returns false when the actor's queue is full.
        /// </summary>
        public bool Assign(EntityId actor, SimTask task, CommandMode mode)
        {
            if (task == null) throw new ArgumentNullException(nameof(task));
            if (!task.Id.IsNone) throw new ArgumentException("A task instance can be assigned once.", nameof(task));
            if (!context.World.IsAlive(actor)) return false;

            if (!byActor.TryGetValue(actor, out ActorTasks tasks))
            {
                tasks = new ActorTasks();
                byActor.Add(actor, tasks);
                actorOrder.Add(actor);
            }
            if (mode == CommandMode.Queue && tasks.Current != null)
            {
                if (tasks.Queue.Count >= context.Config.MaxQueuedPerActor) return false;
                Adopt(task, actor);
                tasks.Queue.Enqueue(task);
                context.World.Raise(new TaskStateChanged(task.Id, actor, task.Kind, TaskState.Queued, TaskReason.None));
                return true;
            }

            EndAll(tasks, TaskState.Cancelled, TaskReason.ReplacedByNewCommand);
            Adopt(task, actor);
            tasks.Current = task;
            task.Enter(context, TaskState.Planning);
            return true;
        }

        /// <summary>Ends the actor's current task and clears its queue. Returns false when there was nothing to stop.</summary>
        public bool Stop(EntityId actor, TaskReason reason)
        {
            if (!byActor.TryGetValue(actor, out ActorTasks tasks) || (tasks.Current == null && tasks.Queue.Count == 0)) return false;
            EndAll(tasks, TaskState.Cancelled, reason);
            return true;
        }

        public void Tick(World world)
        {
            // Walk our own list, not world.Entities: an actor removed by a later system last tick is already gone from Entities,
            // and its tasks must still be cancelled and their reservations released.
            int write = 0;
            for (int read = 0; read < actorOrder.Count; read++)
            {
                EntityId actorId = actorOrder[read];
                ActorTasks tasks = byActor[actorId];
                if (!world.TryGet(actorId, out Entity actor) || !actor.IsAlive)
                {
                    EndAll(tasks, TaskState.Cancelled, TaskReason.ActorRemoved);
                    byActor.Remove(actorId);
                    continue;
                }
                SimTask current = tasks.Current;
                if (current != null)
                {
                    current.Tick(context, actor);
                    // The task may have reassigned or stopped its own actor from inside Tick; then the system already ended it.
                    if (tasks.Current == current && current.IsFinished)
                    {
                        current.Release(context);
                        // A task that did not complete voids what was queued behind it: the queue assumed it would succeed.
                        if (current.State != TaskState.Completed) DropQueue(tasks, TaskReason.PreviousTaskDidNotComplete);
                        tasks.Current = tasks.Queue.Count > 0 ? tasks.Queue.Dequeue() : null;
                        tasks.Current?.Enter(context, TaskState.Planning);
                    }
                }
                // Idle actors leave the books, so the per-tick walk stays proportional to units that are actually working.
                if (tasks.Current == null && tasks.Queue.Count == 0) byActor.Remove(actorId);
                else actorOrder[write++] = actorId;
            }
            actorOrder.RemoveRange(write, actorOrder.Count - write);
        }

        private void Adopt(SimTask task, EntityId actor)
        {
            task.Id = new TaskId(nextTaskId++);
            task.Actor = actor;
        }

        private void EndAll(ActorTasks tasks, TaskState state, TaskReason reason)
        {
            if (tasks.Current != null)
            {
                tasks.Current.Enter(context, state, reason);
                tasks.Current.Release(context);
                tasks.Current = null;
            }
            DropQueue(tasks, reason);
        }

        private void DropQueue(ActorTasks tasks, TaskReason reason)
        {
            while (tasks.Queue.Count > 0)
            {
                SimTask queued = tasks.Queue.Dequeue();
                queued.Enter(context, TaskState.Cancelled, reason);
                queued.Release(context);
            }
        }
    }
}
