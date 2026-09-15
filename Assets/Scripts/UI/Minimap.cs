using JurassicPark.Core;
using JurassicPark.Player;
using JurassicPark.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace JurassicPark.UI
{
    /// <summary>
    /// Corner minimap painted once from the active Terrain (sea, sand, grasses, dirt, rock by the
    /// dominant splat layer) with live markers for the local player, other players and facilities.
    /// M toggles a large centered map.
    /// </summary>
    public sealed class Minimap : MonoBehaviour, IWorldInputBlocker
    {
        [SerializeField] private int resolution = 256;
        [SerializeField] private int cornerSize = 200;
        [SerializeField] private int largeSize = 640;
        [SerializeField] private Color seaColor = new Color(0.16f, 0.32f, 0.5f);
        [SerializeField] private Color[] layerColors =
        {
            new Color(0.85f, 0.78f, 0.55f), // sand
            new Color(0.3f, 0.55f, 0.28f),  // grass a
            new Color(0.42f, 0.66f, 0.32f), // grass b
            new Color(0.24f, 0.48f, 0.3f),  // grass c
            new Color(0.42f, 0.3f, 0.18f),  // dirt
            new Color(0.5f, 0.48f, 0.55f),  // rock
        };

        public Texture2D Map { get; private set; }
        public bool Large { get; private set; }
        public bool BlocksWorldInput => isActiveAndEnabled && HasLocalPlayer
            && (Large || closedFrame == Time.frameCount);
        public Rect CornerScreenRect => ToScreenRect(MapRect(false));

        public static float ReferenceScale(int width, int height) =>
            Mathf.Sqrt(Mathf.Max(1, width) / 1280f * (Mathf.Max(1, height) / 720f));

        private static bool HasLocalPlayer
        {
            get
            {
                foreach (PlayerController player in PlayerController.All)
                    if (player != null && player.isActiveAndEnabled) return true;
                return false;
            }
        }

        private Terrain terrain;
        private Texture2D dot;
        private Texture2D facilityIcon;
        private Texture2D dockIcon;
        private GUIStyle labelStyle;
        private GUIStyle legendStyle;
        private int closedFrame = -1;

        private void OnEnable()
        {
            WorldInputBlockers.Register(this);
            terrain = Terrain.activeTerrain;
            if (terrain != null) Paint();
            dot = Solid(new Color(1f, 0.95f, 0.6f));
            facilityIcon = Solid(new Color(0.95f, 0.35f, 0.3f));
            dockIcon = Solid(new Color(0.3f, 0.7f, 1f));
        }

        private void Update()
        {
            if (!HasLocalPlayer) { Large = false; closedFrame = -1; return; }
            if (terrain == null || Map == null)
            {
                terrain = Terrain.activeTerrain;
                if (terrain != null) Paint();
            }
            if (Keyboard.current == null) return;
            bool toggle = Keyboard.current.mKey.wasPressedThisFrame;
            bool close = Large && Keyboard.current.escapeKey.wasPressedThisFrame;
            if (Large && (toggle || close))
            {
                Large = false;
                // Esc belongs to the map even if a build-mode Update runs after this one.
                closedFrame = Time.frameCount;
            }
            else if (toggle && Map != null)
            {
                Large = true;
            }
        }

        private void OnDisable()
        {
            WorldInputBlockers.Unregister(this);
            Large = false;
            closedFrame = -1;
            ReleaseTextures();
        }

        private void OnDestroy()
        {
            WorldInputBlockers.Unregister(this);
            ReleaseTextures();
        }

        private void ReleaseTextures()
        {
            if (Map != null) Destroy(Map);
            if (dot != null) Destroy(dot);
            if (facilityIcon != null) Destroy(facilityIcon);
            if (dockIcon != null) Destroy(dockIcon);
            Map = dot = facilityIcon = dockIcon = null;
            labelStyle = legendStyle = null;
        }

        private Rect MapRect(bool large)
        {
            float scale = ReferenceScale(Screen.width, Screen.height);
            float size = large ? Mathf.Min(largeSize * scale, Screen.width - 64f * scale, Screen.height - 112f * scale)
                : cornerSize * scale;
            size = Mathf.Max(1f, size);
            return large
                ? new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f - 12f * scale, size, size)
                : new Rect(Screen.width - size - 16f * scale, 16f * scale, size, size);
        }

        private static Rect ToScreenRect(Rect guiRect) =>
            new Rect(guiRect.x, Screen.height - guiRect.yMax, guiRect.width, guiRect.height);

        public bool BlocksPointer(Vector2 screenPosition)
        {
            if (!isActiveAndEnabled || !HasLocalPlayer || Map == null) return false;
            if (BlocksWorldInput) return true;
            float scale = ReferenceScale(Screen.width, Screen.height);
            Rect rect = MapRect(false);
            rect = new Rect(rect.x - 4f * scale, rect.y - 4f * scale, rect.width + 8f * scale, rect.height + 36f * scale);
            return ToScreenRect(rect).Contains(screenPosition);
        }

        /// <summary>Builds the map texture from terrain heights and alphamaps. Public so tests and tools can call it.</summary>
        public void Paint()
        {
            if (terrain == null) terrain = Terrain.activeTerrain;
            if (terrain == null) return;
            if (Map != null) Destroy(Map);
            TerrainData d = terrain.terrainData;
            Map = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            float sea = SeaLevel();
            float[,,] alpha = d.GetAlphamaps(0, 0, d.alphamapWidth, d.alphamapHeight);
            var pixels = new Color32[resolution * resolution];
            for (int y = 0; y < resolution; y++)
            for (int x = 0; x < resolution; x++)
            {
                float u = x / (float)(resolution - 1);
                float v = y / (float)(resolution - 1);
                float h = d.GetInterpolatedHeight(u, v) + terrain.transform.position.y;
                Color c;
                if (h <= sea)
                {
                    c = seaColor * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((h - (sea - 4f)) / 4f));
                }
                else
                {
                    int ax = Mathf.Clamp(Mathf.RoundToInt(u * (d.alphamapWidth - 1)), 0, d.alphamapWidth - 1);
                    int ay = Mathf.Clamp(Mathf.RoundToInt(v * (d.alphamapHeight - 1)), 0, d.alphamapHeight - 1);
                    int best = 0;
                    float bw = -1f;
                    for (int l = 0; l < d.alphamapLayers && l < layerColors.Length; l++)
                    {
                        if (alpha[ay, ax, l] > bw) { bw = alpha[ay, ax, l]; best = l; }
                    }

                    // Shade by height so hills read on the map
                    float shade = Mathf.Lerp(0.8f, 1.15f, Mathf.InverseLerp(sea, d.size.y, h));
                    c = layerColors[best] * shade;
                }

                c.a = 1f;
                pixels[y * resolution + x] = c;
            }

            Map.SetPixels32(pixels);
            Map.Apply(false, false);
        }

        private static float SeaLevel()
        {
            GameObject sea = GameObject.Find("Sea");
            return sea != null ? sea.transform.position.y : 0f;
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private Vector2 ToMap(Vector3 world, Rect rect)
        {
            Vector3 local = world - terrain.transform.position;
            float u = Mathf.Clamp01(local.x / terrain.terrainData.size.x);
            float v = Mathf.Clamp01(local.z / terrain.terrainData.size.z);
            return new Vector2(rect.x + u * rect.width, rect.y + (1f - v) * rect.height);
        }

        private void OnGUI()
        {
            if (Map == null || terrain == null || !HasLocalPlayer) return;
            float scale = ReferenceScale(Screen.width, Screen.height);
            Rect rect = MapRect(Large);
            Color oldColor = GUI.color;
            if (Large)
            {
                GUI.color = new Color(0.04f, 0.07f, 0.07f, 0.94f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
                GUI.color = oldColor;
            }
            GUI.DrawTexture(new Rect(rect.x - 4f * scale, rect.y - 4f * scale, rect.width + 8f * scale, rect.height + 36f * scale), Texture2D.blackTexture);
            GUI.DrawTexture(rect, Map);

            labelStyle ??= new GUIStyle(GUI.skin.label) { normal = { textColor = new Color32(239, 231, 209, 255) } };
            legendStyle ??= new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleCenter };
            labelStyle.fontSize = Mathf.RoundToInt(14f * scale);
            legendStyle.fontSize = Mathf.RoundToInt(16f * scale);
            float m = (Large ? 8f : 5f) * scale;
            foreach (FacilityMarker f in FacilityMarker.All)
            {
                Vector2 p = ToMap(f.transform.position, rect);
                GUI.DrawTexture(new Rect(p.x - m * 0.5f, p.y - m * 0.5f, m, m), f.isDock ? dockIcon : facilityIcon);
                if (Large)
                {
                    float width = 160f * scale;
                    float x = Mathf.Clamp(p.x + 8f * scale, rect.x, rect.xMax - width);
                    float y = Mathf.Clamp(p.y - 10f * scale, rect.y, rect.yMax - 24f * scale);
                    GUI.Label(new Rect(x, y, width, 24f * scale), f.facilityName, labelStyle);
                }
            }

            foreach (PlayerController pc in PlayerController.All)
            {
                Vector2 p = ToMap(pc.transform.position, rect);
                float s = pc.enabled ? m + 2f * scale : m; // local player is the one with input enabled
                GUI.DrawTexture(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), dot);
                Vector2 f = FacingUtil.ToVector(pc.Facing);
                Vector2 tip = p + new Vector2(f.x, -f.y) * (s + 3f * scale);
                GUI.DrawTexture(new Rect(tip.x - 1.5f * scale, tip.y - 1.5f * scale, 3f * scale, 3f * scale), dot);
            }

            GUI.Label(new Rect(rect.x, rect.yMax + 2f * scale, rect.width, 28f * scale),
                Large ? "Red: facilities   ·   Blue: dock   ·   M / Esc: close" : "Island map  ·  M to expand", legendStyle);
        }
    }
}
