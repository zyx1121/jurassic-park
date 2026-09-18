using JurassicPark.Simulation;
using UnityEngine;

namespace JurassicPark.Presentation
{
    /// <summary>Every tunable number of the simulation and the frame loop, so none of them is a literal in code.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Simulation Settings")]
    public sealed class SimulationSettingsAsset : ScriptableObject
    {
        [Header("Clock")]
        [Min(1)] public int ticksPerSecond = 10;
        [Min(1)] public int maxStepsPerAdvance = 5;
        public ulong seed = 1;

        [Header("Commands")]
        [Min(1)] public int rememberedResultsPerSeat = 64;
        [Min(1)] public int maxPendingPerSeat = 32;
        [Min(1)] public int maxCommandIdGap = 64;

        [Header("Tasks")]
        [Min(1)] public int replanIntervalTicks = 10;
        [Min(0)] public int maxReplans = 4;
        [Min(0)] public int maxQueuedPerActor = 8;

        [Header("Instinct")]
        [Tooltip("Ticks between an idle computer-controlled unit's looks around for something to attack.")]
        [Min(1)] public int predatorScanIntervalTicks = 5;
        [Tooltip("Extra path cost of one destructible blocker cell for something that can breach. Straight steps cost 10.")]
        [Min(0)] public int breachCost = 74;

        [Header("Logistics")]
        [Min(1)] public int reservationLifetimeTicks = 50;
        public string groundPileDefinitionId = "pile";

        [Header("Frame loop (MacBook Air M2 target)")]
        [Tooltip("Frames per second to aim for. vSync is used when the display matches, otherwise this cap applies.")]
        [Min(15)] public int targetFrameRate = 60;
        [Tooltip("Frame rate when running without a window, so a batch-mode Editor does not heat a fanless machine.")]
        [Min(5)] public int batchModeFrameRate = 20;

        public SimConfig ToSimConfig() => new SimConfig(ticksPerSecond, maxStepsPerAdvance, seed);
        public CommandRouterConfig ToRouterConfig() => new CommandRouterConfig(rememberedResultsPerSeat, maxPendingPerSeat, maxCommandIdGap);
        public TaskConfig ToTaskConfig() => new TaskConfig(replanIntervalTicks, maxReplans, maxQueuedPerActor, new PathOptions(), new PathOptions(allowBreach: true, breachCost: breachCost));
        public LogisticsConfig ToLogisticsConfig() => new LogisticsConfig(reservationLifetimeTicks, groundPileDefinitionId);
    }
}
