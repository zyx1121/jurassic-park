namespace JurassicPark.Simulation
{
    /// <summary>The time of day, in hours 0 to 24. It runs from the match rules, and freezes when the match says so.</summary>
    public sealed class Clock
    {
        private readonly MatchRules rules;

        public float TimeOfDay { get; private set; }
        public bool Frozen { get; private set; }
        public int Day { get; private set; }

        public Clock(MatchRules rules)
        {
            this.rules = rules;
            TimeOfDay = rules.StartTimeOfDay;
        }

        public bool IsNight => rules.NightStartsAt > rules.NightEndsAt
            ? TimeOfDay >= rules.NightStartsAt || TimeOfDay < rules.NightEndsAt
            : TimeOfDay >= rules.NightStartsAt && TimeOfDay < rules.NightEndsAt;

        internal void Advance(float seconds)
        {
            if (Frozen) return;
            TimeOfDay += 24f * seconds / rules.DayLengthSeconds;
            while (TimeOfDay >= 24f)
            {
                TimeOfDay -= 24f;
                Day++;
            }
        }

        internal void Freeze(float atHour)
        {
            TimeOfDay = atHour;
            Frozen = true;
        }
    }
}
