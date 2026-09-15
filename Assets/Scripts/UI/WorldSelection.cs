using System.Globalization;
using System.Collections.Generic;
using JurassicPark.Building;
using JurassicPark.Combat;
using JurassicPark.Core;
using JurassicPark.Player;
using JurassicPark.Scene;
using JurassicPark.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JurassicPark.UI
{
    [DefaultExecutionOrder(-100)]
    public sealed class WorldSelection : MonoBehaviour
    {
        [SerializeField] private SelectionConfig config;

        public PlayerController LocalPlayer { get; private set; }
        public Component Target { get; private set; }
        public Component HoveredTarget { get; private set; }
        public string TargetName { get; private set; } = "";
        public string TargetDetails => Describe(Target);
        public float TargetDistance => LocalPlayer != null ? LocalPlayer.InteractionDistance(Target) : float.PositiveInfinity;
        public bool CanInteract => LocalPlayer != null && LocalPlayer.CanInteractWith(Target);
        public string ActionText => InteractionHint();

        private Camera cam;
        private PlayerBuilder builder;
        private Renderer hoveredRenderer;
        private Renderer targetRenderer;
        private Component namedTarget;
        private readonly RaycastHit[] hits = new RaycastHit[128];
        private readonly List<Renderer> structureRenderers = new List<Renderer>();

        public void Configure(SelectionConfig value) => config = value;

        private void Start()
        {
            if (config == null)
            {
                Debug.LogError("WorldSelection requires a SelectionConfig. Run build_selection_assets and rebuild the scene.", this);
                enabled = false;
                return;
            }
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        private void Update()
        {
            if (config == null) return;
            if (cam == null) cam = Camera.main;
            BindLocalPlayer();
            if (LocalPlayer == null || Mouse.current == null)
            {
                HoveredTarget = null;
                RefreshTarget();
                return;
            }
            if (WorldInputBlockers.BlocksWorldInput || (builder != null && builder.IsBuilding))
            {
                HoveredTarget = null;
                LocalPlayer.InteractionTarget = null;
                Target = null;
                TargetName = "";
                namedTarget = null;
                return;
            }

            Vector2 pointer = Mouse.current.position.ReadValue();
            bool overUi = WorldInputBlockers.BlocksPointer(pointer);
            HoveredTarget = !overUi && cam != null
                ? PickTarget(cam.ScreenPointToRay(pointer), out hoveredRenderer)
                : null;
            if (!(HoveredTarget is IInteractable)) HoveredTarget = null;
            RefreshTarget();
        }

        private void BindLocalPlayer()
        {
            PlayerController local = null;
            FollowCamera follow = cam != null ? cam.GetComponent<FollowCamera>() : null;
            if (follow != null && follow.Target != null)
                local = follow.Target.GetComponent<PlayerController>();
            if (local == null || !local.isActiveAndEnabled)
            {
                local = null;
                foreach (PlayerController player in PlayerController.All)
                {
                    if (player != null && player.isActiveAndEnabled) { local = player; break; }
                }
            }
            if (LocalPlayer == local) return;
            if (LocalPlayer != null) LocalPlayer.InteractionTarget = null;
            LocalPlayer = local;
            builder = local != null ? local.GetComponent<PlayerBuilder>() : null;
            HoveredTarget = null;
            RefreshTarget();
        }

        private void RefreshTarget()
        {
            Target = HoveredTarget;
            targetRenderer = hoveredRenderer;
            if (LocalPlayer != null)
            {
                LocalPlayer.InteractionTarget = Target;
                if (Target == null)
                {
                    Target = LocalPlayer.FindNearestInteractable();
                    targetRenderer = Target != null ? Target.GetComponentInChildren<Renderer>() : null;
                }
            }
            if (namedTarget != Target || Target == null)
            {
                namedTarget = Target;
                TargetName = DisplayName(Target);
            }
        }

        public Component PickTarget(Ray ray, out Renderer renderer)
        {
            renderer = null;
            if (config == null) return null;
            int count = Physics.RaycastNonAlloc(ray, hits, config.maxPickDistance, config.selectionMask, QueryTriggerInteraction.Collide);
            RaycastHit[] candidates = hits;
            if (count == hits.Length)
            {
                candidates = Physics.RaycastAll(ray, config.maxPickDistance, config.selectionMask, QueryTriggerInteraction.Collide);
                count = candidates.Length;
            }
            float nearest = float.PositiveInfinity;
            float obstruction = float.PositiveInfinity;
            Component best = null;
            Component fadedBest = null;
            Renderer fadedRenderer = null;
            float fadedDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = candidates[i];
                if (LocalPlayer != null && hit.collider.transform.IsChildOf(LocalPlayer.transform)) continue;
                Component target = ResolveSelectable(hit.collider);
                if (target == null)
                {
                    if (!hit.collider.isTrigger) obstruction = Mathf.Min(obstruction, hit.distance);
                    continue;
                }

                Renderer visual = hit.collider.GetComponentInChildren<Renderer>();
                if (visual == null) visual = target.GetComponentInChildren<Renderer>();
                float distance = hit.distance;
                MeshFilter mesh = visual != null ? visual.GetComponent<MeshFilter>() : null;
                if (mesh != null && mesh.sharedMesh != null && mesh.sharedMesh.bounds.size.z < 0.001f)
                {
                    // Pick the visible billboard rectangle, not its oversized see-through capsule.
                    Plane plane = new Plane(visual.transform.forward, visual.transform.position);
                    if (!plane.Raycast(ray, out distance)) continue;
                    Vector3 point = visual.transform.InverseTransformPoint(ray.GetPoint(distance));
                    Bounds bounds = mesh.sharedMesh.bounds;
                    if (point.x < bounds.min.x || point.x > bounds.max.x || point.y < bounds.min.y || point.y > bounds.max.y) continue;
                }
                if (distance > config.maxPickDistance) continue;
                Occluder occluder = target.GetComponent<Occluder>();
                // Visible resources win over the canopy that was faded specifically to reveal them.
                if (occluder != null && occluder.Fade > 0f && occluder.Fade >= config.fadedPickThreshold)
                {
                    if (distance < fadedDistance)
                    {
                        fadedDistance = distance;
                        fadedBest = target;
                        fadedRenderer = visual;
                    }
                    continue;
                }
                if (distance >= nearest) continue;
                nearest = distance;
                best = target;
                renderer = visual;
            }
            if (best != null && nearest <= obstruction) return best;
            if (fadedBest != null && fadedDistance <= obstruction)
            {
                renderer = fadedRenderer;
                return fadedBest;
            }
            renderer = null;
            return null;
        }

        public static Component ResolveSelectable(Collider collider)
        {
            if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return null;
            if (collider.GetComponentInParent<IInteractable>() is Component interactable) return interactable;
            PropInstance prop = collider.GetComponentInParent<PropInstance>();
            if (prop != null) return prop;
            FacilityMarker facility = collider.GetComponentInParent<FacilityMarker>();
            if (facility != null) return facility;
            return collider.GetComponentInParent<Health>();
        }

        public bool TryGetScreenRect(Camera camera, out Rect pixelRect)
        {
            pixelRect = default;
            if (Target == null || targetRenderer == null || !targetRenderer.enabled || camera == null) return false;
            Bounds b = targetRenderer.bounds;
            if (Target is Structure)
            {
                Target.GetComponentsInChildren(false, structureRenderers);
                foreach (Renderer visual in structureRenderers)
                    if (visual.enabled) b.Encapsulate(visual.bounds);
            }
            Vector2 min = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            Vector2 max = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            bool visible = false;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 p = camera.WorldToScreenPoint(corner);
                if (p.z <= camera.nearClipPlane) continue;
                visible = true;
                min = Vector2.Min(min, new Vector2(p.x, p.y));
                max = Vector2.Max(max, new Vector2(p.x, p.y));
            }
            if (!visible) return false;
            pixelRect = Rect.MinMaxRect(Mathf.Clamp(min.x, 0, Screen.width), Mathf.Clamp(min.y, 0, Screen.height),
                Mathf.Clamp(max.x, 0, Screen.width), Mathf.Clamp(max.y, 0, Screen.height));
            return pixelRect.width > 1f && pixelRect.height > 1f;
        }

        public static string DisplayName(Component target)
        {
            if (target == null) return "";
            if (target is Structure structure && structure.Def != null) return structure.Def.displayName;
            if (target is Pickup pickup) return pickup.Kind == ResourceKind.BoatPart ? "Boat part" : pickup.Kind.ToString();
            if (target is FacilityMarker facility)
            {
                switch (facility.facilityName)
                {
                    case "CrashSite": return "Crash site";
                    case "VisitorCenter": return "Visitor center";
                    case "PowerStation": return "Power station";
                    default: return facility.facilityName;
                }
            }
            PropInstance prop = target.GetComponent<PropInstance>();
            string name = prop != null && prop.variant != null ? prop.variant.name : target.gameObject.name;
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace("(Clone)", "").Replace("_", " ").Trim());
        }

        private static string Describe(Component target)
        {
            if (target is ResourceNode node && node.Stock != null)
                return node.Stock.Depleted ? "Depleted" : $"{node.Stock.Remaining} {node.Kind} remaining  |  Hit {node.Stock.Hits}/{node.Stock.HitsPerUnit}";
            if (target is Pickup pickup)
                return pickup.Kind == ResourceKind.BoatPart ? "Unique recovery item" : $"{pickup.Amount} {pickup.Kind}";
            if (target is Structure structure && structure.Health != null)
                return $"{Mathf.CeilToInt(structure.Health.Current)} / {Mathf.CeilToInt(structure.Health.Max)} durability";
            if (target is Health health)
                return health.IsAlive ? $"{Mathf.CeilToInt(health.Current)} / {Mathf.CeilToInt(health.Max)} health" : "Defeated";
            if (target is FacilityMarker marker) return marker.isDock ? "Escape dock" : "Ruined facility";
            if (target is PropInstance) return "Scenery - cannot gather";
            return "";
        }

        private string InteractionHint()
        {
            if (Target == null) return "Point at an object, or move closer to interact";
            if (!(Target is IInteractable action)) return "Inspect only";
            if (LocalPlayer == null) return "";
            if (TargetDistance > LocalPlayer.InteractionRange) return "Move closer";
            if (Target is ResourceNode node && node.Stock != null && node.Stock.Depleted) return "Depleted";
            ResourceInventory inventory = LocalPlayer.GetComponent<ResourceInventory>();
            if (Target is Pickup part && part.Kind == ResourceKind.BoatPart && inventory != null && inventory.HasBoatPart(part.BoatPartId))
                return "Already carrying this part";
            ResourceKind kind = Target is ResourceNode resource ? resource.Kind : Target is Pickup pickup ? pickup.Kind : ResourceKind.None;
            if (kind != ResourceKind.None && inventory != null && inventory.Space(kind) == 0) return "Inventory full";
            if (Target is Structure structure && structure.Def != null && !structure.Def.isGate)
            {
                if (structure.Health != null && structure.Health.Current >= structure.Health.Max) return "Undamaged";
                if (inventory != null && !BuildGrid.CanAfford(inventory, structure.Def, structure.Def.repairCostFraction)) return "Not enough resources to repair";
            }
            if (!LocalPlayer.HasClearInteractionPath(Target)) return "Path blocked";
            return action.CanInteract(LocalPlayer.gameObject) ? action.Prompt : "Unavailable";
        }

        private void OnDisable()
        {
            if (LocalPlayer != null) LocalPlayer.InteractionTarget = null;
            Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }
    }
}
