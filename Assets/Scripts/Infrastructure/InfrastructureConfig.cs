using JurassicPark.Core;
using TMPro;
using UnityEngine;

namespace JurassicPark.Infrastructure
{
    [CreateAssetMenu(menuName = "Jurassic Park/Infrastructure/Simulation Settings")]
    public sealed class InfrastructureConfig : ScriptableObject
    {
        public InfrastructureMapDefinition map;
        public InfraRules rules = new InfraRules();
        [Min(.01f)] public float tickSeconds = .05f;
        [Min(1)] public int sourceStock = 60;
        public int eventSeed = 1;
        [Min(1)] public int supplyDropStock = 10;
        [Min(1f)] public float scenarioTimeout = 150f;
        [Header("Fixed orientation camera")]
        [Range(30f, 80f)] public float cameraPitch = 60f;
        [Min(1f)] public float cameraPanSpeed = 35f;
        [Min(1f)] public float cameraZoomSpeed = 12f;
        public Vector2 cameraZoomRange = new Vector2(12f, 90f);
        public float initialZoom = 23f;
        [Header("Presentation only")]
        public TMP_FontAsset font;
        public SpriteSheetSet survivorSprites;
        public SpriteSheetSet dinosaurSprites;
        public Material spriteMaterial;
        public Material structureMaterial;
        public Material[] trees;
        public Material[] terrainMaterials;
        public Color[] terrainColors =
        {
            new Color(.27f, .35f, .19f), new Color(.53f, .45f, .29f),
            new Color(.43f, .48f, .27f), new Color(.33f, .32f, .25f),
            new Color(.12f, .28f, .33f)
        };
        public Color workerColor = new Color(.4f, .85f, .95f);
        public Color dinosaurColor = new Color(.95f, .38f, .22f);
    }
}
