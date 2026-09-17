namespace JurassicPark.Simulation
{
    /// <summary>Walk to a point. All of the walking lives in <see cref="Mover"/>; this task only turns its status into task states.</summary>
    public sealed class MoveTask : SimTask
    {
        private readonly Mover mover = new Mover();
        private int reportedReplans;

        public MoveTask(SimVector2 destination)
        {
            Destination = destination;
            mover.GoToPoint(destination);
        }

        public override string Kind => "move";

        public SimVector2 Destination { get; }

        protected internal override void Tick(TaskContext context, Entity actor)
        {
            MoverStatus status = mover.Tick(context, actor);
            if (mover.ReplanCount != reportedReplans)
            {
                // Publish the detour even when the new plan succeeded within the same tick.
                reportedReplans = mover.ReplanCount;
                Enter(context, TaskState.Planning, TaskReason.RouteBlocked);
            }
            switch (status)
            {
                case MoverStatus.Moving: Enter(context, TaskState.Running); break;
                case MoverStatus.Planning: Enter(context, TaskState.Planning, mover.Reason); break;
                case MoverStatus.Arrived:
                    // Even a move that finishes within its first tick ran; consumers rely on Planning, Running, Completed.
                    Enter(context, TaskState.Running);
                    Enter(context, TaskState.Completed, TaskReason.Arrived);
                    break;
                case MoverStatus.Failed: Enter(context, TaskState.Failed, mover.Reason); break;
            }
        }
    }
}
