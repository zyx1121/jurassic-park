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
    public sealed class Minimap : MonoBehaviour
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

        private Terrain terrain;
        private Texture2D dot;
        private Texture2D facilityIcon;
        private Texture2D dockIcon;
        private GUIStyle labelStyle;

        private void Start()
        {
            terrain = Terrain.activeTerrain;
            if (terrain != null) Paint();
            dot = Solid(new Color(1f, 0.95f, 0.6f));
            facilityIcon = Solid(new Color(0.95f, 0.35f, 0.3f));
            dockIcon = Solid(new Color(0.3f, 0.7f, 1f));
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame) Large = !Large;
        }

        /// <summary>Builds the map texture from terrain heights and alphamaps. Public so tests and tools can call it.</summary>
        public void Paint()
        {
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
            if (Map == null || terrain == null) return;
            int size = Large ? largeSize : cornerSize;
            Rect rect = Large
                ? new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f, size, size)
                : new Rect(Screen.width - size - 12, 12, size, size);
            GUI.DrawTexture(new Rect(rect.x - 3, rect.y - 3, rect.width + 6, rect.height + 6), Texture2D.blackTexture);
            GUI.DrawTexture(rect, Map);

            float m = Large ? 8f : 5f;
            foreach (FacilityMarker f in FacilityMarker.All)
            {
                Vector2 p = ToMap(f.transform.position, rect);
                GUI.DrawTexture(new Rect(p.x - m * 0.5f, p.y - m * 0.5f, m, m), f.isDock ? dockIcon : facilityIcon);
                if (Large)
                {
                    labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = Color.white } };
                    GUI.Label(new Rect(p.x + 6, p.y - 8, 140, 18), f.facilityName, labelStyle);
                }
            }

            foreach (PlayerController pc in PlayerController.All)
            {
                Vector2 p = ToMap(pc.transform.position, rect);
                float s = pc.enabled ? m + 2f : m; // local player is the one with input enabled
                GUI.DrawTexture(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), dot);
                Vector2 f = FacingUtil.ToVector(pc.Facing);
                Vector2 tip = p + new Vector2(f.x, -f.y) * (s + 3f);
                GUI.DrawTexture(new Rect(tip.x - 1.5f, tip.y - 1.5f, 3f, 3f), dot);
            }

            if (!Large)
            {
                labelStyle ??= new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = Color.white } };
                GUI.Label(new Rect(rect.x, rect.yMax + 2, size, 18), "M: map", labelStyle);
            }
        }
    }
}
