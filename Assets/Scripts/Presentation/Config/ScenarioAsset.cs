using System;
using JurassicPark.Simulation;
using UnityEngine;

namespace JurassicPark.Presentation
{
    /// <summary>Who plays and what stands where when a match starts.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Scenario")]
    public sealed class ScenarioAsset : ScriptableObject
    {
        [Serializable]
        public sealed class SeatEntry
        {
            [Min(1)] public int id = 1;
            public string displayName = string.Empty;
            public int team = 1;
            public SeatController controller = SeatController.Human;
        }

        [Serializable]
        public sealed class Placement
        {
            public string definitionId = string.Empty;
            [Tooltip("0 for unowned things such as trees.")]
            [Min(0)] public int ownerSeat;
            public int cellX;
            public int cellY;
        }

        public SeatEntry[] seats = Array.Empty<SeatEntry>();
        [Tooltip("The seat this machine's player controls.")]
        [Min(1)] public int localSeat = 1;
        public Placement[] placements = Array.Empty<Placement>();
    }
}
