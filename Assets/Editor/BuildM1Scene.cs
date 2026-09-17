using System.Collections.Generic;
using System.Text;
using JurassicPark.Net;
using JurassicPark.Presentation;
using JurassicPark.Simulation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace JurassicPark.Editor
{
    /// <summary>
    /// Generates the M1 slice: settings, catalog, a camp-and-valley map, a scenario and the scene that runs them.
    /// Scenes and data are generated, never hand-built: change this, rerun, commit the result. Every asset under Assets/Data/M1
    /// is rewritten on each run, so an Inspector edit there does not survive; the numbers below are the M1 stand-ins until the
    /// original map's object data is imported (issue #111) and replaces them.
    /// Run with: unity run . -- -executeMethod JurassicPark.Editor.BuildM1Scene.Run
    /// </summary>
    public static class BuildM1Scene
    {
        private const string DataFolder = "Assets/Data/M1";
        private const string MaterialFolder = "Assets/Materials";
        private const string ScenePath = "Assets/Scenes/M1.unity";
        private const float CellSize = 2f;

        [MenuItem("Jurassic Park/Build M1 Scene")]
        public static void Run()
        {
            // Open the new scene before loading or creating assets: closing the previous scene can unload assets held only on the stack.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EnsureFolder(DataFolder);
            EnsureFolder(MaterialFolder);

            TuneRenderPipelineForMacBook();
            var settings = Asset<SimulationSettingsAsset>($"{DataFolder}/Simulation.asset");
            // Reset to the class defaults, so this asset is generated like the other three instead of being the one hand-edited exception.
            var defaults = ScriptableObject.CreateInstance<SimulationSettingsAsset>();
            EditorUtility.CopySerialized(defaults, settings);
            settings.name = "Simulation";
            Object.DestroyImmediate(defaults);
            var catalog = Asset<EntityCatalogAsset>($"{DataFolder}/Catalog.asset");
            catalog.entries = Catalog();
            var mapAsset = Asset<MapDefinitionAsset>($"{DataFolder}/Map.asset");
            MapDefinition map = Map(out List<Cell> trees, out Cell depotAnchor, out List<Cell> redStarts, out List<Cell> blueStarts);
            IReadOnlyList<string> problems = map.Validate();
            if (problems.Count > 0) throw new System.InvalidOperationException("M1 map is invalid:\n- " + string.Join("\n- ", problems));
            mapAsset.SetFrom(map);
            var scenario = Asset<ScenarioAsset>($"{DataFolder}/Scenario.asset");
            scenario.seats = new[]
            {
                new ScenarioAsset.SeatEntry { id = 1, displayName = "Red", team = 1, controller = SeatController.Human },
                new ScenarioAsset.SeatEntry { id = 2, displayName = "Blue", team = 1, controller = SeatController.Computer, playable = true },
            };
            scenario.localSeat = 1;
            scenario.placements = Placements(trees, depotAnchor, redStarts, blueStarts);
            foreach (Object asset in new Object[] { settings, catalog, mapAsset, scenario }) EditorUtility.SetDirty(asset);

            Material entityMaterial = MaterialAsset($"{MaterialFolder}/Entity.mat", "Universal Render Pipeline/Lit", Color.white, true);
            Material ringMaterial = MaterialAsset($"{MaterialFolder}/SelectionRing.mat", "Universal Render Pipeline/Unlit", new Color(0.45f, 1f, 0.55f), false);
            Material terrainMaterial = MaterialAsset($"{MaterialFolder}/Terrain.mat", "JurassicPark/VertexColor", Color.white, false);

            var sessionObject = new GameObject("Session");
            var session = sessionObject.AddComponent<GameSession>();
            // The launcher decides between playing alone, hosting and joining, so the session waits for it.
            session.Configure(settings, mapAsset, catalog, scenario, beginOffline: false);

            var terrainObject = new GameObject("Terrain");
            terrainObject.GetOrAdd<MeshFilter>();
            terrainObject.GetOrAdd<MeshRenderer>().sharedMaterial = terrainMaterial;
            terrainObject.AddComponent<TerrainView>().Configure(session);

            var viewsObject = new GameObject("Views");
            var views = viewsObject.AddComponent<EntityViewRegistry>();
            views.Configure(session, entityMaterial, ringMaterial);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            // Narrow lens, far camera: at a 60 degree pitch a wide lens makes everything near the screen edge lean like a felled tree.
            camera.fieldOfView = 25f;
            // Saved into the scene, not only applied at Start, so the file on disk agrees with the art direction.
            camera.orthographic = true;
            camera.orthographicSize = 17f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 320f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.12f, 0.14f);
            // No MSAA and no HDR: the art direction forbids the first, and the second is bandwidth a MacBook Air does not need to spend.
            camera.allowMSAA = false;
            camera.allowHDR = false;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<RtsCamera>().Configure(session);

            var inputObject = new GameObject("Input");
            var selection = inputObject.AddComponent<SelectionController>();
            selection.Configure(session, views, camera);
            inputObject.AddComponent<SelectionOverlay>().Configure(session, selection);

            // Only the connection and named messages of Netcode are used: no NetworkObjects, no scene management, no player prefab.
            var networkObject = new GameObject("Network");
            var utp = networkObject.AddComponent<UnityTransport>();
            var manager = networkObject.AddComponent<NetworkManager>();
            manager.NetworkConfig = new NetworkConfig { NetworkTransport = utp, EnableSceneManagement = false, ConnectionApproval = false, TickRate = 10 };
            var net = networkObject.AddComponent<NetSession>();
            net.Configure(session, manager, utp);
            networkObject.AddComponent<MatchLauncher>().Configure(session, net);

            var lightObject = new GameObject("Key Light");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.78f);
            light.intensity = 1.2f;
            // No shadows yet: the terrain shader neither casts nor receives them, so a shadow map would be rendered for nothing.
            // Stand-ins are grounded by the selection ring and the checker; real sprites will bake a contact shadow.
            light.shadows = LightShadows.None;
            // The key always comes from screen upper-left so future baked sprite shading agrees with world shadows.
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[BuildM1Scene] {map.Width}x{map.Height} map, {scenario.placements.Length} placements, scene at {ScenePath}");
        }

        /// <summary>Turns off what the slice pays for and never shows: MSAA, HDR, the opaque and depth copies, shadow maps.</summary>
        private static void TuneRenderPipelineForMacBook()
        {
            if (!(GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset pipeline)) return;
            pipeline.msaaSampleCount = 1;
            pipeline.supportsHDR = false;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.shadowDistance = 0f;
            EditorUtility.SetDirty(pipeline);
        }

        private static T GetOrAdd<T>(this GameObject gameObject) where T : Component =>
            gameObject.TryGetComponent(out T existing) ? existing : gameObject.AddComponent<T>();

        private static EntityCatalogAsset.Entry[] Catalog() => new[]
        {
            new EntityCatalogAsset.Entry { id = "survivor", kind = EntityKind.Unit, moveSpeed = 4.5f, storageCapacity = 10, gatherSecondsPerUnit = 0.6f,
                shape = PlaceholderShape.Capsule, color = new Color(0.93f, 0.80f, 0.55f), size = new Vector3(0.7f, 0.85f, 0.7f) },
            new EntityCatalogAsset.Entry { id = "depot", kind = EntityKind.Building, storageCapacity = 200, isDepot = true, blocks = true, footprintWidth = 2, footprintHeight = 2,
                shape = PlaceholderShape.Box, color = new Color(0.62f, 0.44f, 0.29f), size = new Vector3(3.6f, 2.2f, 3.6f) },
            new EntityCatalogAsset.Entry { id = "tree", kind = EntityKind.ResourceNode, nodeResource = "wood", nodeAmount = 40, blocks = true, destructible = false,
                shape = PlaceholderShape.Cylinder, color = new Color(0.17f, 0.35f, 0.31f), size = new Vector3(1.3f, 1.7f, 1.3f) },
            new EntityCatalogAsset.Entry { id = "pile", kind = EntityKind.GroundPile,
                shape = PlaceholderShape.Sphere, color = new Color(0.81f, 0.66f, 0.47f), size = new Vector3(0.9f, 0.5f, 0.9f) },
        };

        /// <summary>
        /// 48 x 32 cells. A cliff-ringed camp in the west with a short east entrance and a far north gate (the fixture's
        /// relationships at playable size), open valley to the east with two groves, a no-build riverbank along the south.
        /// </summary>
        private static MapDefinition Map(out List<Cell> trees, out Cell depotAnchor, out List<Cell> redStarts, out List<Cell> blueStarts)
        {
            const int width = 48, height = 32;
            var grid = new char[height][];
            for (int y = 0; y < height; y++)
            {
                grid[y] = new string('.', width).ToCharArray();
                for (int x = 0; x < width; x++)
                    if (x == 0 || y == 0 || x == width - 1 || y == height - 1) grid[y][x] = '#';
            }
            // Camp ring: cells 6..21 by 9..23.
            for (int x = 6; x <= 21; x++) { grid[9][x] = '#'; grid[23][x] = '#'; }
            for (int y = 9; y <= 23; y++) { grid[y][6] = '#'; grid[y][21] = '#'; }
            grid[15][21] = '.'; grid[16][21] = '.';   // east entrance, two cells wide
            grid[23][13] = '.';                        // north gate, one cell
            // An outcrop that makes the way round to the north gate a real detour.
            for (int x = 22; x <= 27; x++) grid[22][x] = '#';
            for (int y = 17; y <= 22; y++) grid[y][27] = '#';
            // Riverbank: walkable, not buildable.
            for (int x = 1; x < width - 1; x++) { grid[1][x] = ','; grid[2][x] = ','; }

            trees = new List<Cell>();
            foreach ((int x, int y) in new[] { (30, 12), (31, 13), (32, 11), (33, 13), (34, 12), (31, 10), (38, 22), (39, 24), (40, 22), (41, 25), (37, 25), (26, 5), (28, 6), (24, 6) })
                trees.Add(new Cell(x, y));
            depotAnchor = new Cell(12, 15);
            redStarts = new List<Cell> { new Cell(15, 14), new Cell(16, 16), new Cell(15, 18) };
            blueStarts = new List<Cell> { new Cell(10, 12) };

            var flags = new CellFlags[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    flags[y * width + x] = grid[y][x] == '#' ? CellFlags.None : grid[y][x] == ',' ? CellFlags.Walkable : CellFlags.Walkable | CellFlags.Buildable;

            var camps = new[] { new CampDefinition("west-camp", "West Camp", new CellBounds(7, 10, 20, 22), new[] { new Cell(21, 15), new Cell(21, 16), new Cell(13, 23) }) };
            var regions = new[]
            {
                new RegionDefinition("spawn-east", RegionKind.DinosaurSpawn, new CellBounds(42, 4, 46, 28)),
                new RegionDefinition("evac-north", RegionKind.Evacuation, new CellBounds(8, 26, 18, 30)),
                new RegionDefinition("supply-valley", RegionKind.Supply, new CellBounds(28, 14, 36, 20)),
            };
            return new MapDefinition(width, height, CellSize, flags, camps, regions);
        }

        private static ScenarioAsset.Placement[] Placements(List<Cell> trees, Cell depot, List<Cell> red, List<Cell> blue)
        {
            var list = new List<ScenarioAsset.Placement> { new ScenarioAsset.Placement { definitionId = "depot", ownerSeat = 1, cellX = depot.X, cellY = depot.Y } };
            foreach (Cell cell in trees) list.Add(new ScenarioAsset.Placement { definitionId = "tree", ownerSeat = 0, cellX = cell.X, cellY = cell.Y });
            foreach (Cell cell in red) list.Add(new ScenarioAsset.Placement { definitionId = "survivor", ownerSeat = 1, cellX = cell.X, cellY = cell.Y });
            foreach (Cell cell in blue) list.Add(new ScenarioAsset.Placement { definitionId = "survivor", ownerSeat = 2, cellX = cell.X, cellY = cell.Y });
            return list.ToArray();
        }

        private static T Asset<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Material MaterialAsset(string path, string shaderName, Color color, bool instancing)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null) throw new System.InvalidOperationException($"Shader '{shaderName}' was not found.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.color = color;
            material.enableInstancing = instancing;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0, slash));
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
