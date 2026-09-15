using System.Collections.Generic;
using JurassicPark.Scene;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicPark.Dinosaurs
{
    /// <summary>
    /// Spawns dinosaur packs at dusk from the SpawnTable, out of sight of every player, capped per
    /// species, and sends director-spawned dinosaurs away at dawn. Host only in co-op.
    /// </summary>
    public sealed class SpawnDirector : MonoBehaviour
    {
        [SerializeField] private SpawnTable table;
        [SerializeField] private DayNightCycle cycle;
        [SerializeField] private string playerTag = "Player";
        [SerializeField] private int seed = 1;

        public int NightsSpawned { get; private set; }
        public IReadOnlyList<DinosaurBrain> Spawned => spawned;

        private readonly List<DinosaurBrain> spawned = new List<DinosaurBrain>();
        private readonly Dictionary<string, List<DinosaurBrain>> alive = new Dictionary<string, List<DinosaurBrain>>();
        private System.Random rng;
        private Transform root;

        private void Awake()
        {
            rng = new System.Random(seed);
            root = new GameObject("Dinosaurs (spawned)").transform;
        }

        private void OnEnable()
        {
            if (cycle == null) cycle = FindFirstObjectByType<DayNightCycle>();
            if (cycle != null) cycle.PhaseChanged += OnPhase;
        }

        private void OnDisable()
        {
            if (cycle != null) cycle.PhaseChanged -= OnPhase;
        }

        private void OnPhase(DayPhase phase)
        {
            if (!JurassicPark.Core.Authority.IsAuthority) return;
            if (phase == DayPhase.Dusk) SpawnForNight(cycle.DayNumber);
            if (phase == DayPhase.Dawn && table != null && table.despawnAtDawn) Despawn();
        }

        public int AliveOf(string species)
        {
            if (!alive.TryGetValue(species, out var list)) return 0;
            list.RemoveAll(b => b == null || b.State == DinosaurState.Dead);
            return list.Count;
        }

        /// <summary>Spawns everything the table asks for on this night. Returns how many dinosaurs were placed.</summary>
        public int SpawnForNight(int night)
        {
            if (table == null) return 0;
            NightsSpawned++;
            int total = 0;
            Vector3[] players = PlayerPositions();
            Camera[] cams = Camera.allCameras;
            foreach (SpawnEntry e in table.entries)
            {
                if (e.prefab == null) continue;
                int allowed = SpawnTable.AllowedToSpawn(e, night, AliveOf(e.species));
                while (allowed > 0)
                {
                    int members = Mathf.Min(e.packSize, allowed);
                    if (!TryFindSpawnPoint(players, cams, out Vector3 point)) break;
                    Pack pack = members > 1 ? new GameObject($"{e.species} pack").AddComponent<Pack>() : null;
                    if (pack != null) pack.transform.SetParent(root, false);
                    for (int i = 0; i < members; i++)
                    {
                        Vector3 p = point + new Vector3(Mathf.Cos(i * 2.1f), 0f, Mathf.Sin(i * 2.1f)) * 1.5f;
                        if (NavMesh.SamplePosition(p, out NavMeshHit hit, 4f, NavMesh.AllAreas)) p = hit.position;
                        GameObject go = Instantiate(e.prefab, p, Quaternion.identity, pack != null ? pack.transform : root);
                        go.name = $"{e.species}{total}";
                        DinosaurBrain brain = go.GetComponent<DinosaurBrain>();
                        if (brain != null)
                        {
                            pack?.Add(brain);
                            spawned.Add(brain);
                            if (!alive.TryGetValue(e.species, out var list)) alive[e.species] = list = new List<DinosaurBrain>();
                            list.Add(brain);
                        }

                        total++;
                    }

                    allowed -= members;
                }
            }

            return total;
        }

        public void Despawn()
        {
            foreach (DinosaurBrain b in spawned)
            {
                if (b != null && b.State != DinosaurState.Dead) Destroy(b.transform.parent != null && b.transform.parent.GetComponent<Pack>() != null ? b.transform.parent.gameObject : b.gameObject);
            }

            spawned.Clear();
            alive.Clear();
        }

        private Vector3[] PlayerPositions()
        {
            GameObject[] gos = GameObject.FindGameObjectsWithTag(playerTag);
            var ps = new Vector3[gos.Length];
            for (int i = 0; i < gos.Length; i++) ps[i] = gos[i].transform.position;
            return ps;
        }

        /// <summary>Pure placement check: far enough from every player, close enough to the nearest, and off every camera's screen.</summary>
        public static bool IsValidSpawnPoint(Vector3 p, Vector3[] players, Plane[][] frustums, float minDist, float maxDist)
        {
            if (players.Length == 0) return true;
            float nearest = float.MaxValue;
            foreach (Vector3 pl in players)
            {
                float d = Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(pl.x, 0f, pl.z));
                if (d < minDist) return false;
                nearest = Mathf.Min(nearest, d);
            }

            if (nearest > maxDist) return false;
            if (frustums != null)
            {
                Bounds b = new Bounds(p + Vector3.up, new Vector3(2f, 2f, 2f));
                foreach (Plane[] f in frustums)
                {
                    if (f != null && GeometryUtility.TestPlanesAABB(f, b)) return false;
                }
            }

            return true;
        }

        private bool TryFindSpawnPoint(Vector3[] players, Camera[] cams, out Vector3 point)
        {
            Plane[][] frustums = null;
            if (table.requireOffscreen && cams != null)
            {
                frustums = new Plane[cams.Length][];
                for (int i = 0; i < cams.Length; i++) frustums[i] = GeometryUtility.CalculateFrustumPlanes(cams[i]);
            }

            Vector3 anchor = players.Length > 0 ? players[rng.Next(players.Length)] : Vector3.zero;
            for (int a = 0; a < table.placementAttempts; a++)
            {
                float ang = (float)rng.NextDouble() * Mathf.PI * 2f;
                float r = Mathf.Lerp(table.minDistanceFromPlayers, table.maxDistanceFromPlayers, (float)rng.NextDouble());
                Vector3 c = anchor + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * r;
                if (!NavMesh.SamplePosition(c, out NavMeshHit hit, 6f, NavMesh.AllAreas)) continue;
                if (!IsValidSpawnPoint(hit.position, players, frustums, table.minDistanceFromPlayers, table.maxDistanceFromPlayers)) continue;
                point = hit.position;
                return true;
            }

            point = default;
            return false;
        }
    }
}
