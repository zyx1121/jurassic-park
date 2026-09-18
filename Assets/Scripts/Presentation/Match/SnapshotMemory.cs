using System.Collections.Generic;
using JurassicPark.Simulation;

namespace JurassicPark.Presentation
{
    /// <summary>
    /// What each team remembers of things it has seen and can no longer see: buildings, trees and piles keep the look they had
    /// when last in sight, as in the original, until the team looks at that cell again. Units are never remembered. Host only.
    /// </summary>
    public sealed class SnapshotMemory
    {
        private readonly Dictionary<int, Dictionary<EntityId, EntitySnapshot>> byTeam = new Dictionary<int, Dictionary<EntityId, EntitySnapshot>>();
        internal readonly HashSet<EntityId> SeenThisCapture = new HashSet<EntityId>();
        internal readonly List<EntityId> Forget = new List<EntityId>();

        internal Dictionary<EntityId, EntitySnapshot> Of(int team)
        {
            if (!byTeam.TryGetValue(team, out Dictionary<EntityId, EntitySnapshot> remembered))
                byTeam[team] = remembered = new Dictionary<EntityId, EntitySnapshot>();
            return remembered;
        }

        public bool Remembers(int team, EntityId id) => byTeam.TryGetValue(team, out Dictionary<EntityId, EntitySnapshot> remembered) && remembered.ContainsKey(id);

        public int RememberedCountOf(int team) => byTeam.TryGetValue(team, out Dictionary<EntityId, EntitySnapshot> remembered) ? remembered.Count : 0;
    }
}
