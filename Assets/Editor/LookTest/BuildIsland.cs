using JurassicPark.Dinosaurs;
using JurassicPark.Net;
using JurassicPark.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
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
            // Closing the previous scene can unload assets held only by this method's local plan.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // The config is regenerated from the code defaults every build; a stale serialized copy once
            // kept the old sparse densities alive while the code said "crowded jungle".
            IslandConfig cfg = AssetDatabase.LoadAssetAtPath<IslandConfig>(ConfigPath);
            IslandConfig defaults = ScriptableObject.CreateInstance<IslandConfig>();
            if (cfg == null)
            {
                cfg = defaults;
                AssetDatabase.CreateAsset(cfg, ConfigPath);
            }
            else
            {
                EditorUtility.CopySerialized(defaults, cfg);
                Object.DestroyImmediate(defaults);
            }

            cfg.terrain = AssetDatabase.LoadAssetAtPath<TerrainConfig>(BuildTerrainAssets.ConfigPath);
            cfg.props = AssetDatabase.LoadAssetAtPath<PropLibrary>(BuildPropLibrary.LibraryPath);
            cfg.facilities = AssetDatabase.LoadAssetAtPath<FacilityLibrary>(BuildFacilityLibrary.LibraryPath);
            if (cfg.terrain == null || cfg.props == null || cfg.facilities == null)
                throw new System.InvalidOperationException("Build the terrain, prop and facility libraries before building the island.");
            cfg.campRockMaterial = BuildWorldStyleAssets.RidgeMaterial("CampRock", cfg.terrain.layers[TerrainNoise.Rock].diffuseTexture);
            cfg.campTopMaterial = BuildWorldStyleAssets.RidgeMaterial("CampGrass", cfg.terrain.layers[TerrainNoise.GrassA].diffuseTexture);
            EditorUtility.SetDirty(cfg);
            // Regenerating terrain assets can reimport dependencies; persist these references first.
            AssetDatabase.SaveAssets();

            IslandPlan plan = IslandGenerator.Plan(cfg, seed);

            Material seaMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Sea.mat");
            GameObject island = IslandBuilder.Build(plan, cfg, seaMat);

            Terrain terrain = island.GetComponentInChildren<Terrain>();
            NavMeshSurface nav = terrain.gameObject.AddComponent<NavMeshSurface>();
            nav.collectObjects = CollectObjects.All;
            nav.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;

            // Players are spawned by NetLobby (offline instantiate, or NetworkManager player prefab)
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildPlayerPrefab.PrefabPath);
            GameObject netGo = new GameObject("NetworkManager");
            NetworkManager nm = netGo.AddComponent<NetworkManager>();
            UnityTransport utp = netGo.AddComponent<UnityTransport>();
            nm.NetworkConfig = new NetworkConfig { PlayerPrefab = playerPrefab, ConnectionApproval = false, EnableSceneManagement = false };
            nm.NetworkConfig.NetworkTransport = utp;
            NetLobby lobby = netGo.AddComponent<NetLobby>();
            SerializedObject lso = new SerializedObject(lobby);
            lso.FindProperty("playerPrefab").objectReferenceValue = playerPrefab;
            lso.ApplyModifiedPropertiesWithoutUndo();
            GameObject campfirePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildCampfirePrefab.PrefabPath);
            if (campfirePrefab != null)
            {
                GameObject fire = (GameObject)PrefabUtility.InstantiatePrefab(campfirePrefab);
                fire.transform.position = plan.baseCenter;
            }

            // Dinosaurs come from the SpawnDirector at dusk; nothing is placed by hand

            // Sun + day-night
            GameObject sunGo = new GameObject("Sun");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            DayNightConfig dayNight = ScriptableObject.CreateInstance<DayNightConfig>();
            AssetDatabase.CreateAsset(dayNight, "Assets/Data/DayNight.asset");
            GameObject cycleGo = new GameObject("DayNightCycle");
            DayNightCycle cycle = cycleGo.AddComponent<DayNightCycle>();
            SerializedObject cycleSo = new SerializedObject(cycle);
            cycleSo.FindProperty("config").objectReferenceValue = dayNight;
            cycleSo.FindProperty("sun").objectReferenceValue = sun;
            cycleSo.FindProperty("startTime").floatValue = 0.2f;
            cycleSo.ApplyModifiedPropertiesWithoutUndo();
            cycleGo.AddComponent<NetworkObject>();
            cycleGo.AddComponent<NetDayNight>();

            GameObject directorGo = new GameObject("SpawnDirector");
            SpawnDirector director = directorGo.AddComponent<SpawnDirector>();
            SerializedObject dso = new SerializedObject(director);
            dso.FindProperty("table").objectReferenceValue = AssetDatabase.LoadAssetAtPath<SpawnTable>(BuildSpawnTable.TablePath);
            dso.FindProperty("cycle").objectReferenceValue = cycle;
            dso.FindProperty("seed").intValue = seed;
            dso.ApplyModifiedPropertiesWithoutUndo();

            // Camera
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 35f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 120f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            UniversalAdditionalCameraData camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.requiresDepthTexture = true;
            camData.antialiasing = AntialiasingMode.None;
            FollowCamera follow = camGo.AddComponent<FollowCamera>();
            CameraViewConfig view = BuildCameraViewAssets.Load();
            follow.Configure(view);
            camGo.AddComponent<SeeThrough>();
            follow.Target = null; // set by NetLobby when the local player spawns
            float b = cfg.terrain.size * 0.45f;
            follow.Bounds = new Rect(-b, -b, b * 2f, b * 2f);
            follow.FramePoint(plan.playerSpawn);

            VolumeProfile profile = BuildCameraViewAssets.CreateProfile(view);
            GameObject volGo = new GameObject("PostProcessVolume");
            Volume vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

            BuildFeedbackHud.Create();

            EditorSceneManager.SaveScene(scene, ScenePath);
            nav.BuildNavMesh();
            TerrainDataAssets.PersistNavMesh(nav, $"IslandNavMesh_seed{seed}");
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            return $"{ScenePath} seed={seed} attempt={plan.attempt} props={plan.props.Count} trees={plan.propCountsByKind[0]} rocks={plan.propCountsByKind[1]} grass={plan.propCountsByKind[2]} logs={plan.propCountsByKind[3]} bushes={plan.propCountsByKind[4]} facilities={plan.facilities.Count}";
        }
    }
}
