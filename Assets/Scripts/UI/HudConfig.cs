using UnityEngine;

namespace JurassicPark.UI
{
    [CreateAssetMenu(menuName = "Jurassic Park/HUD Config", fileName = "Hud")]
    public sealed class HudConfig : ScriptableObject
    {
        [HideInInspector] public int designVersion;
        [Header("Licensed embedded typography")]
        public Font bodyFont;
        public Font emphasisFont;
        public Font headingFont;
        [Min(16)] public int bodyFontSize = 18;
        [Min(16)] public int smallFontSize = 16;
        [Min(18)] public int emphasisFontSize = 20;
        [Min(20)] public int headingFontSize = 30;
        [Min(24)] public int titleFontSize = 48;

        [Header("Island palette")]
        public Color panel = new Color32(19, 31, 28, 245);
        public Color veil = new Color32(8, 17, 14, 225);
        public Color text = new Color32(237, 232, 214, 255);
        public Color textShadow = new Color32(5, 12, 8, 210);
        public Color muted = new Color32(166, 184, 169, 255);
        public Color accent = new Color32(219, 177, 101, 255);
        public Color healthy = new Color32(133, 181, 143, 255);
        public Color danger = new Color32(235, 132, 112, 255);
        public Color track = new Color32(44, 62, 52, 255);
        public Color button = new Color32(43, 61, 51, 255);
        public Color buttonHover = new Color32(67, 89, 69, 255);
        public Color buttonPressed = new Color32(102, 91, 57, 255);
        public Color buttonDisabled = new Color32(34, 43, 37, 255);
        public Color selected = new Color32(113, 91, 47, 255);

        [Header("Native canvas layout at reference resolution")]
        public Vector2 referenceResolution = new Vector2(1280, 720);
        [Range(0, 1)] public float widthHeightMatch = 0.5f;
        public float margin = 24;
        public float gap = 8;
        public float buttonHeight = 44;
        public float healthWidth = 236;
        public float resourceWidth = 124;
        public float resourceHeight = 42;
        public float contextWidth = 286;
        public float contextHeight = 86;
        public float contextOffset = 24;
        public float paletteWidth = 800;
        public float paletteHeight = 176;
        public float buildCardWidth = 140;
        public float buildCardHeight = 68;
        public Vector2 modalSize = new Vector2(520, 500);
        public float menuWidth = 360;
        public float rowHeight = 44;
        public float progressHeight = 4;
        public float lineHeight = 24;
        public float headingHeight = 42;
        public float menuContentTop = 132;
        public float inventoryButtonWidth = 160;
        public float compactButtonWidth = 88;
        public float buildButtonWidth = 112;
        public float mapCloseButtonWidth = 180;
        public float healthHeight = 60;
        public float paletteBottom = 100;
        public float notificationBottom = 110;
        public float lobbyHeight = 600;
        public float mainMenuHeight = 400;
        public float controlsLineSpacing = 1.4f;
        [Range(0, 1)] public float lowHealthThreshold = 0.3f;
        [Min(0.05f)] public float refreshInterval = 0.1f;
        [Min(0.1f)] public float notificationDuration = 2.6f;
        [Min(0)] public float transitionDuration = 0.08f;

        [Header("Map")]
        public int mapResolution = 256;
        public float cornerMapSize = 128;
        public float largeMapSize = 560;
        public float mapMarkerSize = 8;
        public Color mapSea = new Color32(38, 64, 70, 255);
        public Color mapPlayer = new Color32(243, 215, 139, 255);
        public Color mapFacility = new Color32(204, 121, 96, 255);
        public Color mapDock = new Color32(126, 190, 202, 255);
        public Color[] mapLayers =
        {
            new Color32(153, 146, 112, 255), new Color32(71, 100, 64, 255),
            new Color32(83, 111, 70, 255), new Color32(63, 88, 64, 255),
            new Color32(106, 90, 67, 255), new Color32(106, 108, 102, 255)
        };

        public bool HasFonts => bodyFont != null && emphasisFont != null && headingFont != null;
    }
}
