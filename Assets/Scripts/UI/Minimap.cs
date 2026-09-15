using System.Collections.Generic;
using JurassicPark.Core;
using JurassicPark.Player;
using JurassicPark.World;
using UnityEngine;
using UnityEngine.UI;

namespace JurassicPark.UI
{
    /// <summary>
    /// Corner minimap painted once from the active Terrain (sea, sand, grasses, dirt, rock by the
    /// dominant splat layer) with live markers for the local player, other players and facilities.
    /// M toggles a large centered map.
    /// </summary>
    public sealed class Minimap : MonoBehaviour, IWorldInputBlocker
    {
        private HudConfig config;
        public bool Visible { get; set; } = true;

        public Texture2D Map { get; private set; }
        public bool Large { get; private set; }
        public bool BlocksWorldInput => isActiveAndEnabled && Visible && HasLocalPlayer
            && (Large || closedFrame == Time.frameCount);
        public Rect CornerScreenRect => ToScreenRect(MapRect(false));

        public static float ReferenceScale(int width, int height) =>
            Mathf.Sqrt(Mathf.Max(1, width) / 1280f * (Mathf.Max(1, height) / 720f));

        private float CanvasScale => config == null ? ReferenceScale(Screen.width, Screen.height)
            : Mathf.Pow(Mathf.Max(1, Screen.width) / Mathf.Max(1, config.referenceResolution.x), 1f - config.widthHeightMatch)
                * Mathf.Pow(Mathf.Max(1, Screen.height) / Mathf.Max(1, config.referenceResolution.y), config.widthHeightMatch);

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
        private Canvas canvas;
        private RectTransform canvasRect;
        private RawImage mapImage;
        private Image borderImage;
        private Text legend;
        private readonly List<Image> markers = new List<Image>();
        private readonly List<Text> labels = new List<Text>();
        private readonly List<Image> labelBackdrops = new List<Image>();
        private int markerCount, labelCount;
        private int closedFrame = -1;

        private void OnEnable()
        {
            WorldInputBlockers.Register(this);
            if (config != null) Configure(config);
        }

        public void Configure(HudConfig settings)
        {
            config = settings;
            ReleaseTextures();
            if (config == null) return;
            terrain = Terrain.activeTerrain;
            if (terrain != null) Paint();
            if (canvas == null) BuildCanvas();
        }

        public void Toggle()
        {
            if (Large) Close();
            else if (Visible && HasLocalPlayer && Map != null) Large = true;
        }

        public void Close()
        {
            if (!Large) return;
            Large = false;
            closedFrame = Time.frameCount;
        }

        private void Update()
        {
            if (!HasLocalPlayer) { Large = false; closedFrame = -1; return; }
            if (config == null) return;
            if (terrain == null || Map == null)
            {
                terrain = Terrain.activeTerrain;
                if (terrain != null) Paint();
            }
        }

        private void OnDisable()
        {
            WorldInputBlockers.Unregister(this);
            Large = false;
            closedFrame = -1;
            if (canvas != null) canvas.gameObject.SetActive(false);
            ReleaseTextures();
        }

        private void OnDestroy()
        {
            WorldInputBlockers.Unregister(this);
            ReleaseTextures();
        }

        private void ReleaseTextures()
        {
            if (Map != null)
            {
                if (Application.isPlaying) Destroy(Map);
                else DestroyImmediate(Map);
            }
            Map = null;
        }

        private Rect MapRect(bool large)
        {
            if (config == null) return Rect.zero;
            float scale = CanvasScale;
            float margin = config.margin;
            float size = large ? Mathf.Min(config.largeMapSize * scale, Screen.width - margin * 4 * scale, Screen.height - margin * 7 * scale)
                : config.cornerMapSize * scale;
            size = Mathf.Max(1f, size);
            return large
                ? new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f, size, size)
                : new Rect(Screen.width - size - margin * scale, (margin + config.buttonHeight + config.gap) * scale, size, size);
        }

        private static Rect ToScreenRect(Rect guiRect) =>
            new Rect(guiRect.x, Screen.height - guiRect.yMax, guiRect.width, guiRect.height);

        public bool BlocksPointer(Vector2 screenPosition)
        {
            if (!isActiveAndEnabled || !Visible || !HasLocalPlayer || Map == null) return false;
            if (BlocksWorldInput) return true;
            float scale = CanvasScale;
            Rect rect = MapRect(false);
            rect = new Rect(rect.x - config.progressHeight * scale, rect.y - config.progressHeight * scale,
                rect.width + config.progressHeight * 2 * scale, rect.height + config.progressHeight * 2 * scale);
            return ToScreenRect(rect).Contains(screenPosition);
        }

        /// <summary>Builds the map texture from terrain heights and alphamaps. Public so tests and tools can call it.</summary>
        public void Paint()
        {
            if (config == null)
            {
                SurvivalHud hud = GetComponent<SurvivalHud>();
                if (hud != null) config = hud.Config;
                if (config == null) return;
            }
            if (terrain == null) terrain = Terrain.activeTerrain;
            if (terrain == null) return;
            ReleaseTextures();
            TerrainData d = terrain.terrainData;
            int resolution = Mathf.Max(2, config.mapResolution);
            Color[] layerColors = config.mapLayers;
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
                    c = config.mapSea * Mathf.Lerp(0.55f, 1f, Mathf.Clamp01((h - (sea - 4f)) / 4f));
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

        private Vector2 ToMap(Vector3 world, Rect rect)
        {
            Vector3 local = world - terrain.transform.position;
            float u = Mathf.Clamp01(local.x / terrain.terrainData.size.x);
            float v = Mathf.Clamp01(local.z / terrain.terrainData.size.z);
            return new Vector2(rect.x + u * rect.width, rect.y + (1f - v) * rect.height);
        }

        private void BuildCanvas()
        {
            var root = new GameObject("Island chart", typeof(RectTransform), typeof(Canvas));
            root.transform.SetParent(transform, false);
            canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 101;
            canvasRect = root.GetComponent<RectTransform>();
            borderImage = MakeGraphic<Image>("Map border");
            mapImage = MakeGraphic<RawImage>("Terrain map");
            legend = MakeGraphic<Text>("Map legend");
        }

        private T MakeGraphic<T>(string name) where T : Graphic
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(canvasRect, false);
            T graphic = go.AddComponent<T>();
            graphic.raycastTarget = false;
            return graphic;
        }

        private static void Place(RectTransform target, Rect guiRect)
        {
            Rect screen = ToScreenRect(guiRect);
            target.anchorMin = target.anchorMax = target.pivot = Vector2.zero;
            target.anchoredPosition = screen.position;
            target.sizeDelta = screen.size;
        }

        private void LateUpdate()
        {
            if (canvas == null) return;
            bool visible = Visible && config != null && config.HasFonts && Map != null && terrain != null && HasLocalPlayer;
            canvas.gameObject.SetActive(visible);
            if (!visible) return;
            float scale = CanvasScale;
            Rect rect = MapRect(Large);
            float border = config.progressHeight * scale;
            Place(borderImage.rectTransform, new Rect(rect.x - border, rect.y - border, rect.width + border * 2, rect.height + border * 2));
            borderImage.color = config.panel;
            Place(mapImage.rectTransform, rect);
            mapImage.texture = Map;
            markerCount = labelCount = 0;
            float m = (Large ? config.mapMarkerSize : config.mapMarkerSize * .65f) * scale;
            foreach (FacilityMarker f in FacilityMarker.All)
            {
                Vector2 p = ToMap(f.transform.position, rect);
                Mark(new Rect(p.x - m * .5f, p.y - m * .5f, m, m), f.isDock ? config.mapDock : config.mapFacility);
                if (Large)
                {
                    Text label = GetLabel();
                    StyleLabel(label, scale);
                    label.text = WorldSelection.DisplayName(f);
                    float width = Mathf.Min(label.preferredWidth + config.gap * 2 * scale, rect.width);
                    float x = Mathf.Clamp(p.x + config.gap * scale, rect.x, rect.xMax - width);
                    float y = Mathf.Clamp(p.y - config.gap * scale, rect.y, rect.yMax - config.rowHeight * scale);
                    Rect bounds = new Rect(x, y, width, config.rowHeight * .75f * scale);
                    Place(labelBackdrops[labelCount - 1].rectTransform, bounds);
                    labelBackdrops[labelCount - 1].color = config.panel;
                    Place(label.rectTransform, bounds);
                }
            }

            foreach (PlayerController pc in PlayerController.All)
            {
                Vector2 p = ToMap(pc.transform.position, rect);
                float s = pc.enabled ? m + 2f * scale : m; // local player is the one with input enabled
                Mark(new Rect(p.x - s * .5f, p.y - s * .5f, s, s), config.mapPlayer);
                Vector2 f = FacingUtil.ToVector(pc.Facing);
                Vector2 tip = p + new Vector2(f.x, -f.y) * (s + 3f * scale);
                float tipSize = config.progressHeight * scale;
                Mark(new Rect(tip.x - tipSize * .5f, tip.y - tipSize * .5f, tipSize, tipSize), config.mapPlayer);
            }

            for (int i = markerCount; i < markers.Count; i++) markers[i].gameObject.SetActive(false);
            for (int i = labelCount; i < labels.Count; i++)
            {
                labels[i].gameObject.SetActive(false);
                labelBackdrops[i].gameObject.SetActive(false);
            }
            legend.gameObject.SetActive(Large);
            StyleLabel(legend, scale);
            legend.text = "Coral: facilities   ·   Blue: dock   ·   Gold: survivors";
            Place(legend.rectTransform, new Rect(rect.x, rect.yMax + config.gap * scale, rect.width, config.rowHeight * scale));
        }

        private void Mark(Rect bounds, Color color)
        {
            if (markerCount == markers.Count) markers.Add(MakeGraphic<Image>("Map marker"));
            Image marker = markers[markerCount++];
            marker.gameObject.SetActive(true);
            marker.color = color;
            Place(marker.rectTransform, bounds);
        }

        private Text GetLabel()
        {
            if (labelCount == labels.Count)
            {
                labelBackdrops.Add(MakeGraphic<Image>("Facility label backdrop"));
                labels.Add(MakeGraphic<Text>("Facility name"));
            }
            labelBackdrops[labelCount].gameObject.SetActive(true);
            Text label = labels[labelCount++];
            label.gameObject.SetActive(true);
            return label;
        }

        private void StyleLabel(Text label, float scale)
        {
            label.font = config.bodyFont;
            label.fontSize = Mathf.RoundToInt(config.smallFontSize * scale);
            label.color = config.text;
            label.alignment = TextAnchor.MiddleCenter;
            label.supportRichText = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
        }
    }
}
