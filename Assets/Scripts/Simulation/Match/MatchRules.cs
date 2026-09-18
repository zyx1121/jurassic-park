using System;
using System.Collections.Generic;

namespace JurassicPark.Simulation
{
    /// <summary>One thing a spawn timer may produce: some units of some kinds, chosen among alternatives.</summary>
    public sealed class SpawnBatch
    {
        /// <summary>Alternatives, one rolled per firing by weight. Each alternative is a list of (definition, count).</summary>
        public IReadOnlyList<IReadOnlyList<KeyValuePair<string, int>>> Alternatives { get; }

        /// <summary>Relative weight of each alternative. All ones when the original rolls evenly.</summary>
        public IReadOnlyList<int> Weights { get; }

        public SpawnBatch(IReadOnlyList<IReadOnlyList<KeyValuePair<string, int>>> alternatives, IReadOnlyList<int> weights = null)
        {
            Alternatives = alternatives ?? throw new ArgumentNullException(nameof(alternatives));
            if (weights != null && weights.Count != alternatives.Count) throw new ArgumentException("One weight per alternative.", nameof(weights));
            var w = new int[alternatives.Count];
            for (int i = 0; i < w.Length; i++) w[i] = weights == null ? 1 : Math.Max(0, weights[i]);
            Weights = w;
        }

        /// <summary>Picks an alternative by weight with a roll in [0, total weight).</summary>
        public int Pick(int roll)
        {
            for (int i = 0; i < Weights.Count; i++)
            {
                roll -= Weights[i];
                if (roll < 0) return i;
            }
            return Weights.Count - 1;
        }

        public int TotalWeight
        {
            get
            {
                int total = 0;
                for (int i = 0; i < Weights.Count; i++) total += Weights[i];
                return Math.Max(1, total);
            }
        }
    }

    /// <summary>One of the original map's periodic spawn timers, in our definition ids.</summary>
    public sealed class SpawnTimerRule
    {
        public string Id { get; }
        /// <summary>Seconds after the difficulty is picked before the timer starts. Zero for the ones the pick starts at once.</summary>
        public float EnabledAtSeconds { get; }
        /// <summary>Difficulty levels the timer runs for; null for all.</summary>
        public IReadOnlyList<int> DifficultyGate { get; }
        public float PeriodMinSeconds { get; }
        public float PeriodMaxSeconds { get; }
        public SpawnBatch Batch { get; }

        public SpawnTimerRule(string id, float enabledAtSeconds, IReadOnlyList<int> difficultyGate, float periodMinSeconds, float periodMaxSeconds, SpawnBatch batch)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A timer needs an id.", nameof(id));
            if (!(periodMinSeconds > 0f) || periodMaxSeconds < periodMinSeconds) throw new ArgumentOutOfRangeException(nameof(periodMinSeconds));
            Id = id;
            EnabledAtSeconds = Math.Max(0f, enabledAtSeconds);
            DifficultyGate = difficultyGate;
            PeriodMinSeconds = periodMinSeconds;
            PeriodMaxSeconds = periodMaxSeconds;
            Batch = batch ?? throw new ArgumentNullException(nameof(batch));
        }

        public bool RunsAt(int difficulty)
        {
            if (DifficultyGate == null) return true;
            for (int i = 0; i < DifficultyGate.Count; i++)
                if (DifficultyGate[i] == difficulty) return true;
            return false;
        }
    }

    public sealed class MatchMode
    {
        public string Id { get; }
        public string Label { get; }
        public float SurvivalSeconds { get; }
        public MatchMode(string id, string label, float survivalSeconds) { Id = id; Label = label; SurvivalSeconds = survivalSeconds; }
    }

    /// <summary>The rules of a match, all numbers from the original map's data and none from code.</summary>
    public sealed class MatchRules
    {
        public float SelectionWindowSeconds { get; }
        public IReadOnlyList<MatchMode> Modes { get; }
        public int DefaultModeIndex { get; }
        public int DifficultyCount { get; }
        public int DefaultDifficulty { get; }
        public float HelicopterWindowSeconds { get; }
        public float StartTimeOfDay { get; }
        public float FreezeTimeOfDayAtEvacuation { get; }
        public float DayLengthSeconds { get; }
        public float NightStartsAt { get; }
        public float NightEndsAt { get; }
        public IReadOnlyList<SpawnTimerRule> SpawnTimers { get; }
        public string HelicopterDefinitionId { get; }
        /// <summary>Seat that owns everything the timers spawn.</summary>
        public SeatId DinosaurSeat { get; }

        public MatchRules(float selectionWindowSeconds, IReadOnlyList<MatchMode> modes, int defaultModeIndex, int difficultyCount, int defaultDifficulty,
            float helicopterWindowSeconds, float startTimeOfDay, float freezeTimeOfDayAtEvacuation, float dayLengthSeconds, float nightStartsAt, float nightEndsAt,
            IReadOnlyList<SpawnTimerRule> spawnTimers, string helicopterDefinitionId, SeatId dinosaurSeat)
        {
            if (modes == null || modes.Count == 0) throw new ArgumentException("At least one mode.", nameof(modes));
            if (defaultModeIndex < 0 || defaultModeIndex >= modes.Count) throw new ArgumentOutOfRangeException(nameof(defaultModeIndex));
            if (difficultyCount < 1 || defaultDifficulty < 1 || defaultDifficulty > difficultyCount) throw new ArgumentOutOfRangeException(nameof(difficultyCount));
            if (!(dayLengthSeconds > 0f)) throw new ArgumentOutOfRangeException(nameof(dayLengthSeconds));
            if (string.IsNullOrEmpty(helicopterDefinitionId)) throw new ArgumentException("A helicopter definition is needed.", nameof(helicopterDefinitionId));
            SelectionWindowSeconds = selectionWindowSeconds;
            Modes = modes;
            DefaultModeIndex = defaultModeIndex;
            DifficultyCount = difficultyCount;
            DefaultDifficulty = defaultDifficulty;
            HelicopterWindowSeconds = helicopterWindowSeconds;
            StartTimeOfDay = startTimeOfDay;
            FreezeTimeOfDayAtEvacuation = freezeTimeOfDayAtEvacuation;
            DayLengthSeconds = dayLengthSeconds;
            NightStartsAt = nightStartsAt;
            NightEndsAt = nightEndsAt;
            SpawnTimers = spawnTimers ?? Array.Empty<SpawnTimerRule>();
            HelicopterDefinitionId = helicopterDefinitionId;
            DinosaurSeat = dinosaurSeat;
        }
    }
}
