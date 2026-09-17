using JurassicPark.Core;
using JurassicPark.Scene;
using JurassicPark.World;
using Unity.AI.Navigation;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace JurassicPark.EditorTools
{
    /// <summary>
    /// Builds the HD-2D look test scene from scratch: pixel-textured ground and props,
    /// a billboarded raptor sprite, a dusk sun, a campfire point light, fog, and a URP
    /// volume with depth of field, bloom, vignette and color grading.
    /// Run with: unity command build_look_test
    /// </summary>
    public static class BuildLookTest
    {
        private const string ScenePath = "Assets/Scenes/LookTest.unity";

        [CliCommand("build_look_test", "Build the HD-2D look test scene and save it")]
        public static string Build([CliArg("seed", "Island seed")] int seed = 1)
        {
            UnityEngine.SceneManagement.Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Terrain from the seeded builder (issue 55); a flat plane before this
            TerrainConfig terrainConfig = AssetDatabase.LoadAssetAtPath<TerrainConfig>(BuildTerrainAssets.ConfigPath);
            Terrain terrain = TerrainBuilder.Build(terrainConfig, seed);
            NavMeshSurface navSurface = terrain.gameObject.AddComponent<NavMeshSurface>();
            navSurface.collectObjects = CollectObjects.All;
            navSurface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

            // Animated pixel sea, shared with the island scene.
            GameObject sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
            sea.name = "Sea";
            Object.DestroyImmediate(sea.GetComponent<Collider>());
            sea.transform.position = new Vector3(0f, terrainConfig.seaLevel, 0f);
            sea.transform.localScale = new Vector3(terrainConfig.size / 5f, 1f, terrainConfig.size / 5f);
            Material seaMat = AssetDatabase.LoadAssetAtPath<Material>(BuildSeaMaterial.MaterialPath);
            if (seaMat == null) seaMat = BuildSeaMaterial.Create();
            sea.GetComponent<MeshRenderer>().sharedMaterial = seaMat;

            // Props from the library: random variant, scale and tint per placement, seeded
            PropLibrary props = AssetDatabase.LoadAssetAtPath<PropLibrary>(BuildPropLibrary.LibraryPath);
            GameObject propRoot = new GameObject("Props");
            System.Random rng = new System.Random(seed * 7919 + 13);
            Vector3[] rockSpots = { new Vector3(-4f, 0f, 3f), new Vector3(5f, 0f, -2f), new Vector3(3f, 0f, 6f), new Vector3(-9f, 0f, -4f) };
            Vector3[] treeSpots =
            {
                new Vector3(-8f, 0f, 8f), new Vector3(-3f, 0f, 11f), new Vector3(4f, 0f, 12f), new Vector3(9f, 0f, 9f),
                new Vector3(11f, 0f, 2f), new Vector3(-10f, 0f, 1f), new Vector3(-7f, 0f, -6f), new Vector3(8f, 0f, -7f),
                new Vector3(0f, 0f, 16f), new Vector3(-12f, 0f, 12f), new Vector3(13f, 0f, 14f), new Vector3(-14f, 0f, 5f),
            };
            Vector3[] grassSpots = { new Vector3(-2f, 0f, 4f), new Vector3(6f, 0f, 3f), new Vector3(-6f, 0f, -1f), new Vector3(2f, 0f, 9f), new Vector3(-5f, 0f, 7f), new Vector3(9f, 0f, 5f) };
            if (props != null)
            {
                foreach (Vector3 p in rockSpots) PropPlacer.Place(props.Pick(PropKind.Rock, rng), props, TerrainBuilder.OnGround(p), rng, propRoot.transform);
                foreach (Vector3 p in treeSpots) PropPlacer.Place(props.Pick(PropKind.Tree, rng), props, TerrainBuilder.OnGround(p), rng, propRoot.transform);
                foreach (Vector3 p in grassSpots) PropPlacer.Place(props.Pick(rng.NextDouble() < 0.7 ? PropKind.Grass : PropKind.Bush, rng), props, TerrainBuilder.OnGround(p), rng, propRoot.transform);
                PropPlacer.Place(props.Pick(PropKind.Log, rng), props, TerrainBuilder.OnGround(new Vector3(-3f, 0f, -6f)), rng, propRoot.transform);
            }

            // Campfire prefab
            GameObject campfirePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildCampfirePrefab.PrefabPath);
            if (campfirePrefab != null)
            {
                GameObject fire = (GameObject)PrefabUtility.InstantiatePrefab(campfirePrefab);
                fire.transform.position = TerrainBuilder.OnGround(new Vector3(2f, 0f, -1f));
            }

            // Raptors from the prefab; the NavMesh is baked below once the ground exists
            GameObject raptorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildRaptorPrefab.PrefabPath);
            if (raptorPrefab != null)
            {
                GameObject r1 = (GameObject)PrefabUtility.InstantiatePrefab(raptorPrefab);
                r1.name = "Raptor";
                r1.transform.position = TerrainBuilder.OnGround(new Vector3(-5f, 0f, 5f));
                GameObject r2 = (GameObject)PrefabUtility.InstantiatePrefab(raptorPrefab);
                r2.name = "Raptor2";
                r2.transform.position = TerrainBuilder.OnGround(new Vector3(8f, 0f, 9f));
            }

            // Player
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildPlayerPrefab.PrefabPath);
            GameObject player = null;
            if (playerPrefab != null)
            {
                player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                player.transform.position = TerrainBuilder.OnGround(new Vector3(0f, 0f, -3f), 0.1f);
            }

            // Sun driven by the day-night cycle
            GameObject sunGo = new GameObject("Sun");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            // Always rebuilt from the code defaults so tuning lives in DayNightConfig.cs
            DayNightConfig dayNight = ScriptableObject.CreateInstance<DayNightConfig>();
            AssetDatabase.CreateAsset(dayNight, "Assets/Data/DayNight.asset");
            GameObject cycleGo = new GameObject("DayNightCycle");
            DayNightCycle cycle = cycleGo.AddComponent<DayNightCycle>();
            SerializedObject cycleSo = new SerializedObject(cycle);
            cycleSo.FindProperty("config").objectReferenceValue = dayNight;
            cycleSo.FindProperty("sun").objectReferenceValue = sun;
            cycleSo.FindProperty("startTime").floatValue = 0.62f; // dusk, so the campfire reads
            cycleSo.ApplyModifiedPropertiesWithoutUndo();

            // Shared tactical view keeps the entire defended clearing readable.
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.1f, 0.22f);
            FollowCamera follow = camGo.AddComponent<FollowCamera>();
            CameraViewConfig view = BuildCameraViewAssets.Load();
            follow.Configure(view);
            camGo.AddComponent<SeeThrough>();
            follow.Target = player != null ? player.transform : null;
            follow.Bounds = new Rect(-terrainConfig.size * 0.42f, -terrainConfig.size * 0.42f, terrainConfig.size * 0.84f, terrainConfig.size * 0.84f);
            follow.FramePoint(player != null ? player.transform.position : Vector3.zero);
            UniversalAdditionalCameraData camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.requiresDepthTexture = true;
            camData.antialiasing = AntialiasingMode.None;

            // Post-processing volume
            VolumeProfile profile = BuildCameraViewAssets.CreateProfile(view);
            GameObject volGo = new GameObject("PostProcessVolume");
            Volume vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            BuildFeedbackHud.Create();

            EditorSceneManager.SaveScene(scene, ScenePath);
            navSurface.BuildNavMesh();
            TerrainDataAssets.PersistNavMesh(navSurface, $"LookTestNavMesh_seed{seed}");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            return ScenePath;
        }

        private static Material PixelMaterial(string name, string texturePath, float tiling)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = name;
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", new Vector2(tiling, tiling));
            mat.SetFloat("_Smoothness", 0f);
            AssetDatabase.CreateAsset(mat, $"Assets/Materials/{name}.mat");
            return mat;
        }

        private static Material EmissiveMaterial(string name, Color color, float strength)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = name;
            mat.SetColor("_BaseColor", color);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", color * strength);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            AssetDatabase.CreateAsset(mat, $"Assets/Materials/{name}.mat");
            return mat;
        }

        private static void Raptor(string name, string spritePath, Vector3 pos)
        {
            Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(spritePath);
            float ppu = 64f;
            float w = tex.width / ppu;
            float h = tex.height / ppu;
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.position = pos + new Vector3(0f, h * 0.5f - 0.15f, 0f);
            quad.transform.localScale = new Vector3(w, h, 1f);
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = name + "Sprite";
            mat.SetTexture("_BaseMap", tex);
            mat.SetFloat("_Smoothness", 0f);
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.doubleSidedGI = true;
            AssetDatabase.CreateAsset(mat, $"Assets/Materials/{name}Sprite.mat");
            MeshRenderer mr = quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.TwoSided;
            quad.AddComponent<Billboard>();
        }
    }
}
