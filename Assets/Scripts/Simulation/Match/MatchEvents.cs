namespace JurassicPark.Simulation
{
    public enum MatchPhase { Setup = 1, Survival = 2, Evacuation = 3, Ended = 4 }

    public enum SeatOutcome { Undecided = 0, Won = 1, Lost = 2 }

    public sealed class MatchPhaseChanged : SimEvent
    {
        public MatchPhase Phase { get; }
        public MatchPhaseChanged(MatchPhase phase) => Phase = phase;
    }

    public sealed class MatchOptionsChosen : SimEvent
    {
        public int ModeIndex { get; }
        public int Difficulty { get; }
        public SeatId By { get; }
        public MatchOptionsChosen(int modeIndex, int difficulty, SeatId by) { ModeIndex = modeIndex; Difficulty = difficulty; By = by; }
    }

    public sealed class DinosaursSpawned : SimEvent
    {
        public string TimerId { get; }
        public int Count { get; }
        public DinosaursSpawned(string timerId, int count) { TimerId = timerId; Count = count; }
    }

    public sealed class HelicopterLanded : SimEvent
    {
        public EntityId Helicopter { get; }
        public HelicopterLanded(EntityId helicopter) => Helicopter = helicopter;
    }

    public sealed class Boarded : SimEvent
    {
        public EntityId Survivor { get; }
        public SeatId Seat { get; }
        public Boarded(EntityId survivor, SeatId seat) { Survivor = survivor; Seat = seat; }
    }

    public sealed class SeatOutcomeDecided : SimEvent
    {
        public SeatId Seat { get; }
        public SeatOutcome Outcome { get; }
        public SeatOutcomeDecided(SeatId seat, SeatOutcome outcome) { Seat = seat; Outcome = outcome; }
    }
}
