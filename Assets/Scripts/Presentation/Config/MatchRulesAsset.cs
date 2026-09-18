using System;
using System.Collections.Generic;
using JurassicPark.Simulation;
using UnityEngine;

namespace JurassicPark.Presentation
{
    /// <summary>The original map's match rules as an asset, generated from docs/original/match_flow.json by the scene builder. Rawcodes map to our definition ids here.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Match Rules")]
    public sealed class MatchRulesAsset : ScriptableObject
    {
        [Serializable] public sealed class Mode { public string id = string.Empty; public string label = string.Empty; [Min(1f)] public float survivalSeconds = 1800f; }
        [Serializable] public sealed class UnitCount { public string definitionId = string.Empty; [Min(1)] public int count = 1; }
        [Serializable] public sealed class Alternative { public UnitCount[] units = Array.Empty<UnitCount>(); }
        [Serializable]
        public sealed class Timer
        {
            public string id = string.Empty;
            [Min(0f)] public float enabledAtSeconds;
            [Tooltip("Difficulty levels it runs for; empty for all.")] public int[] difficultyGate = Array.Empty<int>();
            [Min(0.1f)] public float periodMinSeconds = 60f;
            [Min(0.1f)] public float periodMaxSeconds = 60f;
            public Alternative[] alternatives = Array.Empty<Alternative>();
        }
        [Serializable] public sealed class RawcodeMapping { public string rawcode = string.Empty; public string definitionId = string.Empty; }

        [Min(0f)] public float selectionWindowSeconds = 20f;
        public Mode[] modes = Array.Empty<Mode>();
        [Min(0)] public int defaultModeIndex;
        [Min(1)] public int difficultyCount = 6;
        [Min(1)] public int defaultDifficulty = 2;
        [Min(0f)] public float helicopterWindowSeconds = 300f;
        [Range(0f, 24f)] public float startTimeOfDay = 17.5f;
        [Range(0f, 24f)] public float freezeTimeOfDayAtEvacuation = 3f;
        [Min(1f)] public float dayLengthSeconds = 480f;
        [Range(0f, 24f)] public float nightStartsAt = 18f;
        [Range(0f, 24f)] public float nightEndsAt = 6f;
        public string helicopterDefinitionId = "helicopter";
        [Min(1)] public int dinosaurSeat = 8;
        public Timer[] timers = Array.Empty<Timer>();
        [Tooltip("Kept for reference: which original unit each timer's definition stands in for.")] public RawcodeMapping[] rawcodes = Array.Empty<RawcodeMapping>();

        public MatchRules ToRules()
        {
            var modeList = new List<MatchMode>();
            for (int i = 0; i < modes.Length; i++) modeList.Add(new MatchMode(modes[i].id, modes[i].label, modes[i].survivalSeconds));
            var timerList = new List<SpawnTimerRule>();
            for (int i = 0; i < timers.Length; i++)
            {
                Timer t = timers[i];
                var alternatives = new List<IReadOnlyList<KeyValuePair<string, int>>>();
                for (int a = 0; a < t.alternatives.Length; a++)
                {
                    var group = new List<KeyValuePair<string, int>>();
                    for (int u = 0; u < t.alternatives[a].units.Length; u++) group.Add(new KeyValuePair<string, int>(t.alternatives[a].units[u].definitionId, t.alternatives[a].units[u].count));
                    alternatives.Add(group);
                }
                timerList.Add(new SpawnTimerRule(t.id, t.enabledAtSeconds, t.difficultyGate.Length == 0 ? null : t.difficultyGate, t.periodMinSeconds, t.periodMaxSeconds, new SpawnBatch(alternatives)));
            }
            return new MatchRules(selectionWindowSeconds, modeList, defaultModeIndex, difficultyCount, defaultDifficulty, helicopterWindowSeconds,
                startTimeOfDay, freezeTimeOfDayAtEvacuation, dayLengthSeconds, nightStartsAt, nightEndsAt, timerList, helicopterDefinitionId, new SeatId(dinosaurSeat));
        }
    }
}
