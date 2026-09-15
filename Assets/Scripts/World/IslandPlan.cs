using System.Collections.Generic;
using UnityEngine;

namespace JurassicPark.World
{
    public struct FacilitySlot
    {
        public string name;
        public Vector3 position;
        public float rotation;
        public bool isDock;
    }

    public struct PropPlacement
    {
        public PropVariant variant;
        public Vector3 position;
        public float scale;
        public int tintIndex;
        public Biome biome;
    }

    /// <summary>Pure output of the generator: everything needed to build the island, and nothing scene-bound.</summary>
    public sealed class IslandPlan
    {
        public int seed;
        public int attempt;
        public float[,] heights;
        public List<FacilitySlot> facilities = new List<FacilitySlot>();
        public Vector3 baseCenter;
        public Vector3 playerSpawn;
        public List<PropPlacement> props = new List<PropPlacement>();
        public int[] propCountsByKind = new int[5];
    }
}
