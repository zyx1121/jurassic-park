using JurassicPark.Scene;
using JurassicPark.World;
using Unity.AI.Navigation;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace JurassicPark.EditorTools
{
    /// <summary>Generates Assets/Scenes/Island.unity from a seed: terrain, facilities, props, player, camera, day-night, NavMesh.</summary>
    public static class BuildIsland
    {
        public const string ConfigPath = "Assets/Data/Island.asset";
        private const string ScenePath = "Assets/Scenes/Island.unity";

        [CliCommand("build_island", "Generate the Island scene from a seed")]
        public static string Build([CliArg("seed", "Island seed")] int seed = 1)
        {
            IslandConfig cfg = AssetDatabase.LoadAssetAtPath<IslandConfig>(ConfigPath);
            if (cfg == null)
            {
                cfg = ScriptableObject.CreateInstance<IslandConfig>();
                AssetDatabase.CreateAsset(cfg, ConfigPath);
            }

            cfg.terrain = AssetDatabase.LoadAssetAtPath<TerrainConfig>(BuildTerrainAssets.ConfigPath);
            cfg.props = AssetDatabase.LoadAssetAtPath<PropLibrary>(BuildPropLibrary.LibraryPath);
            EditorUtility.SetDirty(cfg);

            IslandPlan plan = IslandGenerator.Plan(cfg, seed);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Material propMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PropSprite.mat");
            Material seaMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Sea.mat");
            GameObject island = IslandBuilder.Build(plan, cfg, propMat, seaMat);

            Terrain terrain = island.GetComponentInChildren<Terrain>();
            NavMeshSurface nav = terrain.gameObject.AddComponent<NavMeshSurface>();
            nav.collectObjects = CollectObjects.All;
            nav.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

            // Player, campfire, raptors near the base
            GameObject player = null;
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildPlayerPrefab.PrefabPath);
            if (playerPrefab != null)
            {
                player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
                player.transform.position = plan.playerSpawn + Vector3.up * 0.1f;
            }
            GameObject campfirePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildCampfirePrefab.PrefabPath);
            if (campfirePrefab != null)
            {
                GameObject fire = (GameObject)PrefabUtility.InstantiatePrefab(campfirePrefab);
                fire.transform.position = plan.baseCenter;
            }
            GameObject raptorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildRaptorPrefab.PrefabPath);
            if (raptorPrefab != null)
            {
                var rng = new System.Random(seed + 99);
                for (int i = 0; i < 3; i++)
                {
                    float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                    Vector3 p = plan.baseCenter + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (cfg.baseClearingRadius + 6f + i * 3f);
                    p.y = IslandGenerator.HeightAt(plan.heights, p, cfg.terrain) * cfg.terrain.maxHeight;
                    GameObject r = (GameObject)PrefabUtility.InstantiatePrefab(raptorPrefab);
                    r.name = "Raptor" + i;
                    r.transform.position = p;
                }
            }

            // Sun + day-night
            GameObject sunGo = new GameObject("Sun");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            DayNightConfig dayNight = AssetDatabase.LoadAssetAtPath<DayNightConfig>("Assets/Data/DayNight.asset");
            GameObject cycleGo = new GameObject("DayNightCycle");
            DayNightCycle cycle = cycleGo.AddComponent<DayNightCycle>();
            SerializedObject cycleSo = new SerializedObject(cycle);
            cycleSo.FindProperty("config").objectReferenceValue = dayNight;
            cycleSo.FindProperty("sun").objectReferenceValue = sun;
            cycleSo.FindProperty("startTime").floatValue = 0.2f;
            cycleSo.ApplyModifiedPropertiesWithoutUndo();

            // Camera
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            UniversalAdditionalCameraData camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;
            FollowCamera follow = camGo.AddComponent<FollowCamera>();
            follow.Target = player != null ? player.transform : null;
            float b = cfg.terrain.size * 0.45f;
            follow.Bounds = new Rect(-b, -b, b * 2f, b * 2f);
            camGo.transform.position = plan.playerSpawn + new Vector3(0f, 9f, -14f);
            camGo.transform.rotation = Quaternion.Euler(30f, 0f, 0f);

            VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/LookTestProfile.asset");
            GameObject volGo = new GameObject("PostProcessVolume");
            Volume vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            EditorSceneManager.SaveScene(scene, ScenePath);
            nav.BuildNavMesh();
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            return $"{ScenePath} seed={seed} attempt={plan.attempt} props={plan.props.Count} trees={plan.propCountsByKind[0]} rocks={plan.propCountsByKind[1]} grass={plan.propCountsByKind[2]} logs={plan.propCountsByKind[3]} bushes={plan.propCountsByKind[4]} facilities={plan.facilities.Count}";
        }
    }
}
