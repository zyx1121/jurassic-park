using JurassicPark.Simulation;

namespace JurassicPark.Presentation
{
    /// <summary>What the screen needs to know about the match itself: phase, clocks, and how the local seat is doing.</summary>
    public struct MatchSnapshot
    {
        public MatchPhase Phase;
        public float SecondsLeft;
        public float TimeOfDay;
        public byte ModeIndex;
        public byte Difficulty;
        public ushort BoardedByLocal;
        public SeatOutcome LocalOutcome;
        public EntityId Helicopter;
    }
}
