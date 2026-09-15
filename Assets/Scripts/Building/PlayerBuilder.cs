using JurassicPark.Core;
using JurassicPark.Player;
using JurassicPark.World;
using UnityEngine;

namespace JurassicPark.Building
{
    /// <summary>
    /// Build mode for one player: Build toggles the mode and cycles structures, Rotate turns the
    /// preview in 90-degree steps, Attack confirms. The preview sits a fixed distance in front of
    /// the player on the grid and turns red when the spot is blocked, too steep, in the water or
    /// unaffordable.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    [RequireComponent(typeof(ResourceInventory))]
    public sealed class PlayerBuilder : MonoBehaviour
    {
        [SerializeField] private StructureLibrary library;
        [SerializeField] private LayerMask blockingMask = ~0;

        public bool IsBuilding { get; private set; }
        public int SelectedIndex { get; private set; }
        public int RotationSteps { get; private set; }
        public StructureDef Selected => library != null && library.structures.Length > 0 ? library.structures[SelectedIndex % library.structures.Length] : null;
        public bool PreviewValid { get; private set; }
        public Vector3 PreviewCenter { get; private set; }
        public string PreviewReason { get; private set; } = "";

        private PlayerController player;
        private ResourceInventory inventory;
        private GameObject preview;
        private Transform structuresRoot;
        private readonly Collider[] overlap = new Collider[16];

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            inventory = GetComponent<ResourceInventory>();
        }

        private void OnEnable()
        {
            player.BuildPressed += OnBuild;
            player.RotatePressed += OnRotate;
            player.AttackPressed += OnConfirm;
        }

        private void OnDisable()
        {
            player.BuildPressed -= OnBuild;
            player.RotatePressed -= OnRotate;
            player.AttackPressed -= OnConfirm;
            SetBuilding(false);
        }

        public void SetBuilding(bool on)
        {
            IsBuilding = on && library != null && library.structures.Length > 0;
            if (!IsBuilding && preview != null)
            {
                Destroy(preview);
                preview = null;
            }
        }

        public void Select(int index)
        {
            if (library == null || library.structures.Length == 0) return;
            SelectedIndex = ((index % library.structures.Length) + library.structures.Length) % library.structures.Length;
            RebuildPreview();
        }

        private void OnBuild()
        {
            if (!IsBuilding)
            {
                SetBuilding(true);
                RebuildPreview();
            }
            else
            {
                Select(SelectedIndex + 1);
            }
        }

        private void OnRotate()
        {
            if (!IsBuilding) return;
            RotationSteps = (RotationSteps + 1) % 4;
            RebuildPreview();
        }

        private void OnConfirm()
        {
            if (IsBuilding) TryPlace();
        }

        private void Update()
        {
            if (!IsBuilding || Selected == null) return;
            UpdatePreview();
        }

        /// <summary>Where the selected structure would go right now, snapped to the grid in front of the player.</summary>
        public Vector3 ComputeCenter()
        {
            Vector2 f = FacingUtil.ToVector(player.Facing);
            Vector3 ahead = transform.position + new Vector3(f.x, 0f, f.y) * library.placeDistance;
            Vector3 snapped = BuildGrid.Snap(ahead, library.cellSize);
            Vector3 center = BuildGrid.FootprintCenter(snapped, Selected.footprint, RotationSteps, library.cellSize);
            return TerrainBuilder.OnGround(center);
        }

        public bool Validate(Vector3 center, out string reason)
        {
            StructureDef def = Selected;
            Vector2 size = BuildGrid.RotatedFootprint(def.footprint, RotationSteps, library.cellSize);
            Terrain t = Terrain.activeTerrain;
            if (t != null)
            {
                // Water and slope: sample the four corners
                float minH = float.MaxValue, maxH = float.MinValue;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 c = center + new Vector3((i % 2 == 0 ? -1 : 1) * size.x * 0.5f, 0f, (i < 2 ? -1 : 1) * size.y * 0.5f);
                    float h = TerrainBuilder.SampleHeight(c);
                    minH = Mathf.Min(minH, h); maxH = Mathf.Max(maxH, h);
                }

                if (maxH - minH > library.maxHeightDelta) { reason = "too steep"; return false; }
                float sea = FindSeaLevel();
                if (minH < sea + 0.05f) { reason = "in the water"; return false; }
            }

            Vector3 half = new Vector3(size.x * 0.5f - 0.02f, def.height * 0.5f, size.y * 0.5f - 0.02f);
            int n = Physics.OverlapBoxNonAlloc(center + Vector3.up * def.height * 0.5f, half, overlap, Quaternion.identity, blockingMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Collider c = overlap[i];
                if (c.GetComponentInParent<Terrain>() != null) continue;
                if (c.GetComponentInParent<Pickup>() != null) continue; // pickups are collected, not obstacles
                if (preview != null && c.transform.IsChildOf(preview.transform)) continue;
                if (c.GetComponentInParent<Structure>() != null || c.GetComponentInParent<PropInstance>() != null || c.GetComponentInParent<PlayerController>() != null || c.GetComponent<UnityEngine.AI.NavMeshAgent>() != null)
                {
                    reason = "blocked by " + c.transform.root.name;
                    return false;
                }
            }

            if (!BuildGrid.CanAfford(inventory, def)) { reason = "not enough resources"; return false; }
            reason = "";
            return true;
        }

        private static float FindSeaLevel()
        {
            GameObject sea = GameObject.Find("Sea");
            return sea != null ? sea.transform.position.y : float.NegativeInfinity;
        }

        public Structure TryPlace()
        {
            if (!IsBuilding || Selected == null) return null;
            Vector3 center = ComputeCenter();
            if (!Validate(center, out string reason))
            {
                PreviewReason = reason;
                return null;
            }

            if (!BuildGrid.Pay(inventory, Selected)) return null;
            if (structuresRoot == null)
            {
                GameObject root = GameObject.Find("Structures") ?? new GameObject("Structures");
                structuresRoot = root.transform;
            }

            Structure s = StructureFactory.Place(library, Selected, center, RotationSteps, structuresRoot);
            RebuildPreview();
            return s;
        }

        private void RebuildPreview()
        {
            if (preview != null) Destroy(preview);
            preview = null;
            if (!IsBuilding || Selected == null) return;
            preview = new GameObject("BuildPreview");
            Structure ghost = StructureFactory.Place(library, Selected, transform.position, RotationSteps, preview.transform);
            foreach (Collider c in ghost.GetComponentsInChildren<Collider>()) c.enabled = false;
            var obstacle = ghost.GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if (obstacle != null) obstacle.enabled = false;
            foreach (Light l in ghost.GetComponentsInChildren<Light>()) l.enabled = false;
            foreach (MonoBehaviour mb in ghost.GetComponentsInChildren<MonoBehaviour>()) mb.enabled = false;
            UpdatePreview();
        }

        private void UpdatePreview()
        {
            PreviewCenter = ComputeCenter();
            PreviewValid = Validate(PreviewCenter, out string reason);
            PreviewReason = reason;
            if (preview == null) return;
            Transform ghost = preview.transform.GetChild(0);
            ghost.SetPositionAndRotation(PreviewCenter, Quaternion.Euler(0f, RotationSteps * 90f, 0f));
            Material m = PreviewValid ? library.previewValid : library.previewInvalid;
            if (m == null) return;
            foreach (Renderer r in preview.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial = m;
            }
        }
    }
}
