using JurassicPark.Core;
using JurassicPark.Player;
using JurassicPark.World;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JurassicPark.Building
{
    /// <summary>
    /// Mouse-driven building: B toggles the palette, R rotates, click places, and right click cancels.
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
        public IReadOnlyList<StructureDef> Definitions => library != null ? library.structures : System.Array.Empty<StructureDef>();
        public bool PreviewValid { get; private set; }
        public Vector3 PreviewCenter { get; private set; }
        public string PreviewReason { get; private set; } = "";

        private PlayerController player;
        private ResourceInventory inventory;
        private GameObject preview;
        private Transform structuresRoot;
        private readonly Collider[] overlap = new Collider[16];
        private readonly RaycastHit[] groundHits = new RaycastHit[64];
        private bool pointerHasGround;
        private int cancelledFrame = -1;
        private static bool CancelRequested =>
            (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            || (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame);

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
            bool wasBuilding = IsBuilding;
            IsBuilding = on && library != null && library.structures.Length > 0;
            if (player != null) player.InteractionSuppressed = IsBuilding;
            if (wasBuilding && !IsBuilding) cancelledFrame = Time.frameCount;
            if (!IsBuilding && preview != null)
            {
                Destroy(preview);
                preview = null;
            }
            if (IsBuilding && !wasBuilding) RebuildPreview();
        }

        public void Select(int index)
        {
            if (library == null || library.structures.Length == 0) return;
            SelectedIndex = ((index % library.structures.Length) + library.structures.Length) % library.structures.Length;
            RebuildPreview();
        }

        private void OnBuild()
        {
            SetBuilding(!IsBuilding);
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
            if (!player.isActiveAndEnabled) { SetBuilding(false); return; }
            if (CancelRequested && !WorldInputBlockers.BlocksWorldInput)
                SetBuilding(false);
            if (!IsBuilding || Selected == null) return;
            UpdatePreview();
        }

        /// <summary>Snaps the current pointer's ground hit; gamepad-only play keeps a facing-based preview.</summary>
        public Vector3 ComputeCenter()
        {
            pointerHasGround = false;
            if (Mouse.current != null)
            {
                Vector2 pointer = Mouse.current.position.ReadValue();
                Camera camera = Camera.main;
                if (camera == null || WorldInputBlockers.BlocksPointer(pointer)) return PreviewCenter;
                pointerHasGround = TryGetGround(camera.ScreenPointToRay(pointer), out Vector3 ground);
                return pointerHasGround ? SnapCenter(ground) : PreviewCenter;
            }
            Vector2 f = FacingUtil.ToVector(player.Facing);
            Vector3 ahead = transform.position + new Vector3(f.x, 0f, f.y) * library.placeDistance;
            pointerHasGround = true;
            return SnapCenter(ahead);
        }

        public Vector3 SnapCenter(Vector3 ground)
        {
            Vector3 center = BuildGrid.SnapFootprint(ground, Selected.footprint, RotationSteps, library.cellSize);
            return TerrainBuilder.OnGround(center);
        }

        public bool TryGetGround(Ray ray, out Vector3 ground)
        {
            ground = default;
            int count = Physics.RaycastNonAlloc(ray, groundHits, library.pointerRayDistance, blockingMask, QueryTriggerInteraction.Ignore);
            RaycastHit[] hits = groundHits;
            if (count == hits.Length)
            {
                hits = Physics.RaycastAll(ray, library.pointerRayDistance, blockingMask, QueryTriggerInteraction.Ignore);
                count = hits.Length;
            }
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider collider = hits[i].collider;
                if (collider.transform.IsChildOf(transform) || (preview != null && collider.transform.IsChildOf(preview.transform))) continue;
                if (!(collider is TerrainCollider)) continue;
                if (hits[i].distance >= nearest) continue;
                ground = hits[i].point;
                nearest = hits[i].distance;
            }
            return nearest < float.PositiveInfinity;
        }

        public bool Validate(Vector3 center, out string reason)
        {
            StructureDef def = Selected;
            if (def == null) { reason = "choose a structure"; return false; }
            Vector3 delta = center - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude > library.maxPlaceDistance * library.maxPlaceDistance)
            { reason = "move closer to build here"; return false; }
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
            Collider[] candidates = overlap;
            if (n == overlap.Length)
            {
                candidates = Physics.OverlapBox(center + Vector3.up * def.height * 0.5f, half, Quaternion.identity, blockingMask, QueryTriggerInteraction.Collide);
                n = candidates.Length;
            }
            for (int i = 0; i < n; i++)
            {
                Collider c = candidates[i];
                if (c.GetComponentInParent<Terrain>() != null) continue;
                if (c.GetComponentInParent<Pickup>() != null) continue; // pickups are collected, not obstacles
                if (c.isTrigger && c.GetComponentInParent<JurassicPark.Scene.Occluder>() != null) continue; // canopy volume for see-through, not a footprint
                if (preview != null && c.transform.IsChildOf(preview.transform)) continue;
                if (!c.isTrigger || c.GetComponentInParent<Structure>() != null || c.GetComponentInParent<PropInstance>() != null || c.GetComponentInParent<FacilityMarker>() != null || c.GetComponentInParent<PlayerController>() != null || c.GetComponent<UnityEngine.AI.NavMeshAgent>() != null)
                {
                    string name = c.GetComponentInParent<FacilityMarker>()?.facilityName
                        ?? c.GetComponentInParent<PropInstance>()?.variant?.name
                        ?? c.GetComponentInParent<Structure>()?.Def?.displayName
                        ?? c.gameObject.name;
                    reason = "blocked by " + name.Replace("_", " ");
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
            if (!IsBuilding || Selected == null || !player.isActiveAndEnabled ||
                WorldInputBlockers.BlocksWorldInput || cancelledFrame == Time.frameCount) return null;
            if (CancelRequested) { SetBuilding(false); return null; }
            Vector3 center = ComputeCenter();
            if (!pointerHasGround) { PreviewValid = false; PreviewReason = "point at nearby ground"; return null; }
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
            NoiseBus.Emit(center, 25f, gameObject);
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
            string reason = "point at nearby ground";
            PreviewValid = pointerHasGround && Validate(PreviewCenter, out reason);
            PreviewReason = reason;
            if (preview == null) return;
            preview.SetActive(pointerHasGround && !WorldInputBlockers.BlocksWorldInput);
            if (!preview.activeSelf) return;
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
