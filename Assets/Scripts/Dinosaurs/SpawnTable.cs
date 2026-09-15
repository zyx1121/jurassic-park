using System;
using UnityEngine;

namespace JurassicPark.Dinosaurs
{
    [Serializable]
    public struct SpawnEntry
    {
        public string species;
        public GameObject prefab;
        [Tooltip("First night this species appears (1 = the first night).")]
        [Min(1)] public int fromNight;
        [Tooltip("Members per pack; 1 spawns loners.")]
        [Min(1)] public int packSize;
        [Tooltip("Packs spawned on the first eligible night.")]
        [Min(0)] public int basePacks;
        [Tooltip("Extra packs per night after the first eligible night.")]
        [Min(0f)] public float packsPerNight;
        [Tooltip("Never more than this many alive at once.")]
        [Min(1)] public int cap;
        public DinosaurStats stats;
    }

    /// <summary>What spawns on which night. The director reads it every dusk.</summary>
    [CreateAssetMenu(menuName = "Jurassic Park/Spawn Table", fileName = "SpawnTable")]
    public sealed class SpawnTable : ScriptableObject
    {
        public SpawnEntry[] entries = new SpawnEntry[0];

        [Header("Placement")]
        [Tooltip("Spawn at least this far from every player.")]
        [Min(5f)] public float minDistanceFromPlayers = 40f;
        [Tooltip("And no farther than this from the nearest player, so they arrive within the night.")]
        [Min(10f)] public float maxDistanceFromPlayers = 70f;
        [Tooltip("Also outside every camera's view frustum.")]
        public bool requireOffscreen = true;
        [Min(1)] public int placementAttempts = 40;
        [Tooltip("Dinosaurs spawned by the director leave at dawn.")]
        public bool despawnAtDawn = true;

        /// <summary>Packs of an entry to spawn on a given night, before the cap. Pure.</summary>
        public static int PacksForNight(SpawnEntry e, int night)
        {
            if (night < e.fromNight) return 0;
            return e.basePacks + Mathf.FloorToInt(e.packsPerNight * (night - e.fromNight));
        }

        /// <summary>Individuals allowed given the cap and how many are already alive. Pure.</summary>
        public static int AllowedToSpawn(SpawnEntry e, int night, int alive)
        {
            int wanted = PacksForNight(e, night) * e.packSize;
            return Mathf.Max(0, Mathf.Min(wanted, e.cap - alive));
        }
    }
}
