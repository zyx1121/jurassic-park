namespace JurassicPark.Simulation
{
    /// <summary>Walk beside the helicopter and climb in. Only during the evacuation; the match decides whether the door is open.</summary>
    public sealed class BoardTask : SimTask
    {
        private readonly MatchFlow match;
        private readonly Mover mover = new Mover();
        private bool started;

        public BoardTask(MatchFlow match) => this.match = match;

        public override string Kind => "board";

        protected internal override void Tick(TaskContext context, Entity actor)
        {
            if (match.Phase != MatchPhase.Evacuation || !context.World.TryGet(match.Helicopter, out Entity helicopter) || !helicopter.IsAlive)
            {
                Enter(context, TaskState.Failed, TaskReason.TargetGone);
                return;
            }
            if (!started)
            {
                started = true;
                mover.GoBeside(LogisticsQueries.FootprintOf(context, helicopter));
                Enter(context, TaskState.Running);
            }
            switch (mover.Tick(context, actor))
            {
                case MoverStatus.Arrived:
                    if (match.TryBoard(actor)) Enter(context, TaskState.Completed, TaskReason.Arrived);
                    else Enter(context, TaskState.Failed, TaskReason.TargetGone);
                    break;
                case MoverStatus.Failed:
                    Enter(context, TaskState.Failed, mover.Reason);
                    break;
            }
        }
    }
}
