using System.Collections.Generic;
using System;
using System.Linq;
using System.Text;
using JurassicPark.Net;
using JurassicPark.Presentation;
using JurassicPark.Simulation;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Newtonsoft.Json.Linq;
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
            UnityEngine.Object.DestroyImmediate(defaults);
            var catalog = Asset<EntityCatalogAsset>($"{DataFolder}/Catalog.asset");
            catalog.entries = Catalog();
            var mapAsset = Asset<MapDefinitionAsset>($"{DataFolder}/Map.asset");
            MapDefinition map = Map(out List<Cell> trees, out Cell depotAnchor, out List<Cell> redStarts, out List<Cell> blueStarts);
            IReadOnlyList<string> problems = map.Validate();
            if (problems.Count > 0) throw new System.InvalidOperationException("M1 map is invalid:\n- " + string.Join("\n- ", problems));
            mapAsset.SetFrom(map);
            var matchRules = Asset<MatchRulesAsset>($"{DataFolder}/MatchRules.asset");
            FillMatchRules(matchRules, "docs/original/match_flow.json");
            var scenario = Asset<ScenarioAsset>($"{DataFolder}/Scenario.asset");
            scenario.seats = new[]
            {
                new ScenarioAsset.SeatEntry { id = 1, displayName = "Red", team = 1, controller = SeatController.Human },
                new ScenarioAsset.SeatEntry { id = 2, displayName = "Blue", team = 1, controller = SeatController.Computer, playable = true },
                new ScenarioAsset.SeatEntry { id = 8, displayName = "Dinosaurs", team = 2, controller = SeatController.Computer, playable = false },
            };
            scenario.localSeat = 1;
            scenario.placements = Placements(trees, depotAnchor, redStarts, blueStarts, new[] { new Cell(44, 26), new Cell(45, 6) });
            foreach (UnityEngine.Object asset in new UnityEngine.Object[] { settings, catalog, mapAsset, scenario, matchRules }) EditorUtility.SetDirty(asset);

            Material entityMaterial = MaterialAsset($"{MaterialFolder}/Entity.mat", "Universal Render Pipeline/Lit", Color.white, true);
            Material ringMaterial = MaterialAsset($"{MaterialFolder}/SelectionRing.mat", "Universal Render Pipeline/Unlit", new Color(0.45f, 1f, 0.55f), false);
            Material terrainMaterial = MaterialAsset($"{MaterialFolder}/Terrain.mat", "JurassicPark/VertexColor", Color.white, false);

            var sessionObject = new GameObject("Session");
            var session = sessionObject.AddComponent<GameSession>();
            // The launcher decides between playing alone, hosting and joining, so the session waits for it.
            session.Configure(settings, mapAsset, catalog, scenario, beginOffline: false, matchRules);

            var terrainObject = new GameObject("Terrain");
            terrainObject.GetOrAdd<MeshFilter>();
            terrainObject.GetOrAdd<MeshRenderer>().sharedMaterial = terrainMaterial;
            terrainObject.AddComponent<TerrainView>().Configure(session);

            var viewsObject = new GameObject("Views");
            var views = viewsObject.AddComponent<EntityViewRegistry>();
            views.Configure(session, entityMaterial, ringMaterial);

            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            var camera = cameraObject.AddComponent<Camera>();
            // The original's 70 degree horizontal field of view is about 43 degrees vertical at 16:9.
            camera.fieldOfView = 43f;
            camera.orthographic = false;
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
            // Netcode marks its manager DontDestroyOnLoad and offers no switch, so nothing else lives on that GameObject,
            // and NetSession destroys it when this scene goes.
            var matchNetObject = new GameObject("Match Net");
            var net = matchNetObject.AddComponent<NetSession>();
            net.Configure(session, manager, utp);
            matchNetObject.AddComponent<MatchLauncher>().Configure(session, net);

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

        /// <summary>
        /// Which of our definitions stands in for each original rawcode until #114 imports the real roster. "Wv" is the
        /// difficulty-dependent raptor tier and maps to the raptor as well.
        /// </summary>
        private static readonly Dictionary<string, string> RawcodeStandIns = new Dictionary<string, string>
        {
            ["Wv"] = "raptor", ["o004"] = "raptor", ["o00N"] = "raptor", ["o00O"] = "raptor", ["o00P"] = "raptor", ["o00Q"] = "raptor", ["o008"] = "raptor",
            ["o002"] = "dilophosaurus", ["o00Y"] = "dilophosaurus",
            ["o006"] = "stegosaurus", ["o00B"] = "triceratops", ["o00C"] = "triceratops",
            ["o000"] = "spinosaurus", ["o00J"] = "spinosaurus",
            ["o001"] = "trex", ["o007"] = "trex", ["o00F"] = "trex", ["o00L"] = "trex", ["o013"] = "trex", ["o00Z"] = "trex",
            ["o005"] = "pteranodon", ["o00G"] = "pteranodon", ["o00W"] = "pteranodon",
            ["e001"] = "insects", ["e002"] = "insects",
        };

        /// <summary>Reads the original's match flow (docs/original/match_flow.json, extracted from the map script) into the asset.</summary>
        private static void FillMatchRules(MatchRulesAsset asset, string jsonPath)
        {
            JObject root = JObject.Parse(System.IO.File.ReadAllText(jsonPath));
            JObject match = (JObject)root["match"];
            asset.selectionWindowSeconds = (float)match["selectionWindowSeconds"];
            var modes = new List<MatchRulesAsset.Mode>();
            foreach (JObject mode in match["modes"])
                modes.Add(new MatchRulesAsset.Mode { id = (string)mode["id"], label = (string)mode["label"], survivalSeconds = (float)mode["survivalSeconds"] });
            asset.modes = modes.ToArray();
            asset.defaultModeIndex = modes.FindIndex(m => m.id == (string)match["defaultMode"]);
            asset.helicopterWindowSeconds = (float)match["helicopterWindowSeconds"];
            asset.startTimeOfDay = (float)match["startTimeOfDay"];
            asset.freezeTimeOfDayAtEvacuation = (float)match["freezeTimeOfDayAtEvacuation"];
            asset.dayLengthSeconds = (float)match["dayLengthSeconds"];
            asset.difficultyCount = ((JArray)root["difficulties"]).Count;
            asset.defaultDifficulty = 2;
            var timers = new List<MatchRulesAsset.Timer>();
            var rawcodes = new HashSet<string>();
            foreach (JObject t in root["spawnTimers"])
            {
                JObject period = (JObject)t["periodSeconds"];
                var timer = new MatchRulesAsset.Timer
                {
                    id = (string)t["id"],
                    enabledAtSeconds = (float)t["enabledAtSeconds"],
                    difficultyGate = t["difficultyGate"] is JArray gate ? gate.Select(g => (int)g).ToArray() : Array.Empty<int>(),
                    periodMinSeconds = period["fixed"] != null ? (float)period["fixed"] : (float)period["min"],
                    periodMaxSeconds = period["fixed"] != null ? (float)period["fixed"] : (float)period["max"],
                };
                // "a,b" is one group of several kinds; "a|b" is a roll between alternatives; counts follow the same shape.
                // "alternativeWeights" gives the roll's odds and "always" a group spawned on every firing besides the rolled one.
                string[] codeAlternatives = ((string)t["unitRawcode"]).Split('|');
                string[] countAlternatives = ((string)t["count"]).Split('|');
                int[] weights = t["alternativeWeights"] is JArray wa ? wa.Select(w => (int)w).ToArray() : null;
                List<MatchRulesAsset.UnitCount> always = ParseGroup((string)t["always"], (string)t["alwaysCount"], rawcodes);
                var alternatives = new List<MatchRulesAsset.Alternative>();
                for (int a = 0; a < codeAlternatives.Length; a++)
                {
                    List<MatchRulesAsset.UnitCount> units = ParseGroup(codeAlternatives[a], countAlternatives[Math.Min(a, countAlternatives.Length - 1)], rawcodes);
                    units.AddRange(always);
                    alternatives.Add(new MatchRulesAsset.Alternative { units = units.ToArray(), weight = weights != null && a < weights.Length ? weights[a] : 1 });
                }
                timer.alternatives = alternatives.ToArray();
                timers.Add(timer);
            }
            asset.timers = timers.ToArray();
            asset.rawcodes = rawcodes.OrderBy(c => c).Select(c => new MatchRulesAsset.RawcodeMapping { rawcode = c, definitionId = RawcodeStandIns.TryGetValue(c, out string def) ? def : "raptor" }).ToArray();
        }

        private static List<MatchRulesAsset.UnitCount> ParseGroup(string codesText, string countsText, HashSet<string> rawcodes)
        {
            var units = new List<MatchRulesAsset.UnitCount>();
            if (string.IsNullOrEmpty(codesText)) return units;
            string[] codes = codesText.Split(',');
            string[] counts = (countsText ?? "1").Split(',');
            for (int u = 0; u < codes.Length; u++)
            {
                string code = codes[u].Trim();
                rawcodes.Add(code);
                units.Add(new MatchRulesAsset.UnitCount { definitionId = RawcodeStandIns.TryGetValue(code, out string def) ? def : "raptor", count = int.Parse(counts[Math.Min(u, counts.Length - 1)].Trim()) });
            }
            return units;
        }

        private static T GetOrAdd<T>(this GameObject gameObject) where T : Component =>
            gameObject.TryGetComponent(out T existing) ? existing : gameObject.AddComponent<T>();

        private static EntityCatalogAsset.Entry[] Catalog() => new[]
        {
            new EntityCatalogAsset.Entry { id = "survivor", kind = EntityKind.Unit, moveSpeed = 4.5f, storageCapacity = 10, gatherSecondsPerUnit = 0.6f, maxHealth = 60,
                shape = PlaceholderShape.Capsule, color = new Color(0.93f, 0.80f, 0.55f), size = new Vector3(0.7f, 0.85f, 0.7f) },
            new EntityCatalogAsset.Entry { id = "depot", kind = EntityKind.Building, storageCapacity = 200, isDepot = true, blocks = true, footprintWidth = 2, footprintHeight = 2, maxHealth = 400,
                shape = PlaceholderShape.Box, color = new Color(0.62f, 0.44f, 0.29f), size = new Vector3(3.6f, 2.2f, 3.6f) },
            new EntityCatalogAsset.Entry { id = "wall", kind = EntityKind.Building, blocks = true, destructible = true, maxHealth = 120, buildWorkSeconds = 6f,
                buildCost = new[] { new EntityCatalogAsset.CostEntry { resource = "wood", amount = 6 } },
                shape = PlaceholderShape.Box, color = new Color(0.55f, 0.47f, 0.33f), size = new Vector3(1.9f, 1.6f, 1.9f) },
            new EntityCatalogAsset.Entry { id = "gate", kind = EntityKind.Building, blocks = true, destructible = true, maxHealth = 100, buildWorkSeconds = 8f, isGate = true,
                buildCost = new[] { new EntityCatalogAsset.CostEntry { resource = "wood", amount = 8 } },
                shape = PlaceholderShape.Box, color = new Color(0.72f, 0.58f, 0.36f), size = new Vector3(1.9f, 1.8f, 1.9f) },
            new EntityCatalogAsset.Entry { id = "tree", kind = EntityKind.ResourceNode, nodeResource = "wood", nodeAmount = 40, blocks = true, destructible = false,
                shape = PlaceholderShape.Cylinder, color = new Color(0.17f, 0.35f, 0.31f), size = new Vector3(1.3f, 1.7f, 1.3f) },
            new EntityCatalogAsset.Entry { id = "raptor", kind = EntityKind.Unit, moveSpeed = 6f, maxHealth = 120, attackDamage = 12, attackSeconds = 0.8f, perceptionRadius = 28f, canBreach = true,
                shape = PlaceholderShape.Capsule, color = new Color(0.45f, 0.62f, 0.30f), size = new Vector3(1.1f, 0.7f, 1.1f) },
            new EntityCatalogAsset.Entry { id = "dilophosaurus", kind = EntityKind.Unit, moveSpeed = 5f, maxHealth = 90, attackDamage = 9, attackSeconds = 0.9f, perceptionRadius = 24f, canBreach = true,
                shape = PlaceholderShape.Capsule, color = new Color(0.55f, 0.55f, 0.25f), size = new Vector3(1f, 0.65f, 1f) },
            new EntityCatalogAsset.Entry { id = "stegosaurus", kind = EntityKind.Unit, moveSpeed = 3f, maxHealth = 260, attackDamage = 18, attackSeconds = 1.6f, perceptionRadius = 16f, canBreach = true,
                shape = PlaceholderShape.Capsule, color = new Color(0.40f, 0.35f, 0.22f), size = new Vector3(1.6f, 0.9f, 1.6f) },
            new EntityCatalogAsset.Entry { id = "triceratops", kind = EntityKind.Unit, moveSpeed = 3.5f, maxHealth = 320, attackDamage = 22, attackSeconds = 1.5f, perceptionRadius = 18f, canBreach = true,
                shape = PlaceholderShape.Capsule, color = new Color(0.50f, 0.42f, 0.30f), size = new Vector3(1.7f, 0.95f, 1.7f) },
            new EntityCatalogAsset.Entry { id = "spinosaurus", kind = EntityKind.Unit, moveSpeed = 4.5f, maxHealth = 420, attackDamage = 30, attackSeconds = 1.2f, perceptionRadius = 30f, canBreach = true,
                shape = PlaceholderShape.Capsule, color = new Color(0.35f, 0.45f, 0.40f), size = new Vector3(1.8f, 1.2f, 1.8f) },
            new EntityCatalogAsset.Entry { id = "trex", kind = EntityKind.Unit, moveSpeed = 5f, maxHealth = 600, attackDamage = 45, attackSeconds = 1.4f, perceptionRadius = 34f, canBreach = true,
                shape = PlaceholderShape.Capsule, color = new Color(0.45f, 0.30f, 0.22f), size = new Vector3(2.2f, 1.5f, 2.2f) },
            new EntityCatalogAsset.Entry { id = "pteranodon", kind = EntityKind.Unit, moveSpeed = 7f, maxHealth = 70, attackDamage = 8, attackSeconds = 0.7f, perceptionRadius = 30f, canBreach = false,
                shape = PlaceholderShape.Sphere, color = new Color(0.55f, 0.50f, 0.60f), size = new Vector3(1.4f, 0.6f, 1.4f) },
            new EntityCatalogAsset.Entry { id = "insects", kind = EntityKind.Unit, moveSpeed = 6f, maxHealth = 12, attackDamage = 2, attackSeconds = 0.5f, perceptionRadius = 14f, canBreach = false,
                shape = PlaceholderShape.Sphere, color = new Color(0.25f, 0.25f, 0.25f), size = new Vector3(0.5f, 0.3f, 0.5f) },
            new EntityCatalogAsset.Entry { id = "helicopter", kind = EntityKind.Building, footprintWidth = 2, footprintHeight = 2,
                shape = PlaceholderShape.Box, color = new Color(0.85f, 0.85f, 0.90f), size = new Vector3(4.5f, 2.4f, 4.5f) },
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

        private static ScenarioAsset.Placement[] Placements(List<Cell> trees, Cell depot, List<Cell> red, List<Cell> blue, IReadOnlyList<Cell> raptors)
        {
            var list = new List<ScenarioAsset.Placement>
            {
                new ScenarioAsset.Placement { definitionId = "depot", ownerSeat = 1, cellX = depot.X, cellY = depot.Y,
                    stock = new[] { new EntityCatalogAsset.CostEntry { resource = "wood", amount = 60 } } },
            };
            foreach (Cell cell in trees) list.Add(new ScenarioAsset.Placement { definitionId = "tree", ownerSeat = 0, cellX = cell.X, cellY = cell.Y });
            foreach (Cell cell in red) list.Add(new ScenarioAsset.Placement { definitionId = "survivor", ownerSeat = 1, cellX = cell.X, cellY = cell.Y });
            foreach (Cell cell in blue) list.Add(new ScenarioAsset.Placement { definitionId = "survivor", ownerSeat = 2, cellX = cell.X, cellY = cell.Y });
            foreach (Cell cell in raptors) list.Add(new ScenarioAsset.Placement { definitionId = "raptor", ownerSeat = 8, cellX = cell.X, cellY = cell.Y });
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
