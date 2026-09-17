using System;
using System.Collections.Generic;
using System.Linq;
using JurassicPark.Core;
using JurassicPark.Infrastructure;
using TMPro;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TextCore.LowLevel;
using Object = UnityEngine.Object;

namespace JurassicPark.EditorTools
{
    public static class BuildInfrastructure
    {
        public const string ScenePath = "Assets/Scenes/Infrastructure.unity";
        public const string ConfigPath = "Assets/Data/Infrastructure/Simulation.asset";
        public const string MapPath = "Assets/Data/Infrastructure/FixedMap.asset";
        private const string MeshPath = "Assets/Data/Generated/InfrastructureTerrain.asset";
        private const string FontPath = "Assets/Data/Generated/InfrastructureFont.asset";

        [MenuItem("Jurassic Park/Build Infrastructure Scene")]
        [CliCommand("build_infrastructure", "Generate the isolated fixed-map RTS infrastructure scene without changing Island")]
        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before rebuilding Infrastructure.");
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                UnityEngine.SceneManagement.Scene current = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (current.isDirty) throw new InvalidOperationException($"Save the modified scene before building: {current.path}");
            }
            if (Resources.Load<TMP_Settings>("TMP Settings") == null)
                throw new InvalidOperationException("Import TMP essentials non-interactively with TMP_PackageResourceImporter.ImportResources(true, false, false) first.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Folder("Assets/Data/Infrastructure");
            Folder("Assets/Data/Generated");
            Folder("Assets/Materials/Infrastructure");
            InfrastructureMapDefinition map = LoadOrCreate<InfrastructureMapDefinition>(MapPath);
            InfrastructureConfig config = LoadOrCreate<InfrastructureConfig>(ConfigPath);
            config.map = map;
            config.font = FontAsset();
            config.survivorSprites = Required<SpriteSheetSet>("Assets/Data/SurvivorSprites.asset");
            config.dinosaurSprites = Required<SpriteSheetSet>("Assets/Data/RaptorSprites.asset");
            config.spriteMaterial = Required<Material>("Assets/Materials/PlayerSprite.mat");
            config.trees = new[]
            {
                Required<Material>("Assets/Materials/Props/tree_strangler_fig_0.mat"),
                Required<Material>("Assets/Materials/Props/tree_thin_palm_0.mat"),
                Required<Material>("Assets/Materials/Props/tree_buttress_giant_0.mat")
            };
            config.structureMaterial = Material("Structure", Color.white, "Universal Render Pipeline/Lit");
            config.terrainMaterials = new Material[config.terrainColors.Length];
            for (int i = 0; i < config.terrainMaterials.Length; i++)
                config.terrainMaterials[i] = Material(((MapSurface)i).ToString(), config.terrainColors[i], "Universal Render Pipeline/Lit");
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            InfrastructureLayout layout = map.CreateLayout();
            BuildTerrain(layout, config);
            BuildScenery(layout, config);
            BuildCampLabels(layout, config);

            var simulation = new GameObject("InfrastructureSimulation");
            InfrastructureSession session = simulation.AddComponent<InfrastructureSession>();
            session.Configure(config);
            simulation.AddComponent<InfrastructureHud>();
            var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = config.initialZoom;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 400f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = config.terrainColors[(int)MapSurface.Water];
            camera.transform.rotation = Quaternion.Euler(config.cameraPitch, 0, 0);
            Vector2 focus = (layout.Map.CellCenter(map.arrival) + layout.Map.CellCenter(layout.MainCamp.center)) * .5f;
            camera.transform.position = new Vector3(focus.x, 0, focus.y) - camera.transform.forward * 160f;
            cameraObject.AddComponent<InfrastructureCamera>().Configure(session);
            var sunObject = new GameObject("Sun", typeof(Light));
            Light sun = sunObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, .94f, .8f);
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(55f, -25f, 0);
            RenderSettings.sun = sun;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.57f, .64f, .59f);
            RenderSettings.fog = false;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            return $"{ScenePath}; map={map.mapVersion}; {map.width}x{map.height} path cells; " +
                $"{map.width * map.cellSize}m x {map.height * map.cellSize}m; camps={map.camps.Length}; offline; press F6 in Play Mode.";
        }

        [CliCommand("infrastructure_scenario", "Start or inspect the live map/logistics/build/breach scenario in Play Mode")]
        public static object Scenario([CliArg("start", "Reset the slice and start its complete scenario")] bool start = false)
        {
            if (!EditorApplication.isPlaying)
                throw new InvalidOperationException("Open Infrastructure and enter Play Mode first.");
            InfrastructureSession session = Object.FindFirstObjectByType<InfrastructureSession>();
            if (session == null || session.World == null)
                throw new InvalidOperationException("The Infrastructure simulation is not ready.");
            if (start) session.BeginDemonstration();
            return new
            {
                phase = session.ScenarioPhase.ToString(),
                evidence = session.ScenarioEvidence,
                simulationTime = session.World.Time,
                spatialRevision = session.World.Revision,
                physical = session.World.TotalPhysicalMaterials,
                consumed = session.World.ConsumedMaterials,
                initial = session.World.InitialMaterials,
                views = session.VisibleEntityCount,
                entities = session.World.Entities.Values.Select(e => new
                {
                    e.Id, kind = e.Kind.ToString(), position = new { x = e.Position.x, z = e.Position.y },
                    e.Health, e.Stored, e.Reserved, e.Carried,
                    e.Delivered, task = e.TaskStatus.ToString(), e.Action, e.Reason, e.TargetId
                }).ToArray(),
                events = session.World.Events.Skip(Math.Max(0, session.World.Events.Count - 24)).ToArray()
            };
        }

        private static void BuildTerrain(InfrastructureLayout layout, InfrastructureConfig config)
        {
            var vertices = new List<Vector3>();
            var uvs = new List<Vector2>();
            var indices = new List<int>[config.terrainMaterials.Length];
            for (int i = 0; i < indices.Length; i++) indices[i] = new List<int>();
            InfraMap map = layout.Map;
            float half = map.CellSize * .5f;
            for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                var cell = new Vector2Int(x, y);
                Vector2 center = map.CellCenter(cell);
                float elevation = map.ElevationAt(cell);
                float left = center.x - half, right = center.x + half;
                float bottom = center.y - half, top = center.y + half;
                int surface = (int)layout.SurfaceAt(cell);
                Quad(new Vector3(left, elevation, bottom), new Vector3(left, elevation, top),
                    new Vector3(right, elevation, top), new Vector3(right, elevation, bottom), surface);
                if (surface != (int)MapSurface.Ridge) continue;
                if (Lower(cell + Vector2Int.down, elevation))
                    Quad(new Vector3(left, 0, bottom), new Vector3(left, elevation, bottom),
                        new Vector3(right, elevation, bottom), new Vector3(right, 0, bottom), surface);
                if (Lower(cell + Vector2Int.up, elevation))
                    Quad(new Vector3(right, 0, top), new Vector3(right, elevation, top),
                        new Vector3(left, elevation, top), new Vector3(left, 0, top), surface);
                if (Lower(cell + Vector2Int.left, elevation))
                    Quad(new Vector3(left, 0, top), new Vector3(left, elevation, top),
                        new Vector3(left, elevation, bottom), new Vector3(left, 0, bottom), surface);
                if (Lower(cell + Vector2Int.right, elevation))
                    Quad(new Vector3(right, 0, bottom), new Vector3(right, elevation, bottom),
                        new Vector3(right, elevation, top), new Vector3(right, 0, top), surface);
            }
            var generated = new Mesh { name = "InfrastructureTerrain", indexFormat = IndexFormat.UInt32 };
            generated.SetVertices(vertices);
            generated.SetUVs(0, uvs);
            generated.subMeshCount = indices.Length;
            for (int i = 0; i < indices.Length; i++) generated.SetTriangles(indices[i], i);
            generated.RecalculateNormals();
            generated.RecalculateBounds();
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh == null) { mesh = generated; AssetDatabase.CreateAsset(mesh, MeshPath); }
            else { EditorUtility.CopySerialized(generated, mesh); Object.DestroyImmediate(generated); EditorUtility.SetDirty(mesh); }
            var terrain = new GameObject("FixedTerrain", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            terrain.GetComponent<MeshFilter>().sharedMesh = mesh;
            terrain.GetComponent<MeshCollider>().sharedMesh = mesh;
            terrain.GetComponent<MeshRenderer>().sharedMaterials = config.terrainMaterials;
            terrain.isStatic = true;

            bool Lower(Vector2Int cell, float elevation) => !map.Contains(cell) || map.ElevationAt(cell) < elevation;
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int material)
            {
                int offset = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                uvs.Add(new Vector2(a.x, a.z) / 8); uvs.Add(new Vector2(b.x, b.z) / 8);
                uvs.Add(new Vector2(c.x, c.z) / 8); uvs.Add(new Vector2(d.x, d.z) / 8);
                indices[material].AddRange(new[] { offset, offset + 1, offset + 2, offset, offset + 2, offset + 3 });
            }
        }

        private static void BuildScenery(InfrastructureLayout layout, InfrastructureConfig config)
        {
            var root = new GameObject("FixedScenery_NoGameplay");
            Material grass = Required<Material>("Assets/Materials/Props/fern_tuft_0.mat");
            for (int y = 2; y < layout.Map.Height - 2; y += 2)
            for (int x = 2; x < layout.Map.Width - 2; x += 2)
            {
                var cell = new Vector2Int(x, y);
                MapSurface surface = layout.SurfaceAt(cell);
                int pattern = (x * 31 + y * 17) % 19;
                bool tree = surface == MapSurface.Ridge && pattern % 3 != 0;
                if (!tree && !(surface == MapSurface.Jungle && pattern == 0)) continue;
                Vector2 center = layout.Map.CellCenter(cell);
                float size = tree ? 4.5f + pattern * .06f : 1.8f;
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.name = tree ? "RidgeTree" : "GroundFern";
                quad.transform.SetParent(root.transform, false);
                quad.transform.position = new Vector3(center.x, layout.Map.ElevationAt(cell) + size * .45f, center.y);
                quad.transform.localScale = new Vector3(size, size, 1);
                quad.GetComponent<Renderer>().sharedMaterial = tree ? config.trees[pattern % config.trees.Length] : grass;
                Object.DestroyImmediate(quad.GetComponent<Collider>());
                quad.AddComponent<Billboard>();
            }
        }

        private static void BuildCampLabels(InfrastructureLayout layout, InfrastructureConfig config)
        {
            var root = new GameObject("CampMarkers");
            for (int i = 0; i < config.map.camps.Length; i++)
            {
                InfrastructureMapDefinition.Camp camp = config.map.camps[i];
                Vector2 center = layout.Map.CellCenter(camp.center);
                var marker = new GameObject($"Camp{i + 1:00}");
                marker.transform.SetParent(root.transform, false);
                marker.transform.position = new Vector3(center.x, .08f, center.y + 4f);
                marker.transform.rotation = Quaternion.Euler(90f, 0, 0);
                var text = marker.AddComponent<TextMeshPro>();
                text.font = config.font;
                text.text = $"{i + 1:00}  {camp.name.ToUpperInvariant()}";
                text.fontSize = 3f;
                text.enableAutoSizing = false;
                text.alignment = TextAlignmentOptions.Center;
                text.color = new Color(.85f, .85f, .66f);
                text.rectTransform.sizeDelta = new Vector2(17f, 3f);
            }
        }

        private static TMP_FontAsset FontAsset()
        {
            TMP_FontAsset existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (existing != null) return existing;
            Font source = Required<Font>("Assets/UI/Fonts/SourceSans3-Regular.ttf");
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDF16, 1024, 1024,
                AtlasPopulationMode.Dynamic, false);
            string characters = new string(Enumerable.Range(32, 95).Select(c => (char)c).ToArray()) + "\u2026";
            if (!font.TryAddCharacters(characters, out string missing))
                throw new InvalidOperationException($"Infrastructure font could not bake required glyphs: {missing}");
            font.name = "InfrastructureSourceSans";
            font.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(font, FontPath);
            foreach (Texture2D atlas in font.atlasTextures) AssetDatabase.AddObjectToAsset(atlas, font);
            AssetDatabase.AddObjectToAsset(font.material, font);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
            return font;
        }

        private static Material Material(string name, Color color, string shaderName)
        {
            string path = $"Assets/Materials/Infrastructure/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find(shaderName);
                if (shader == null) throw new InvalidOperationException($"Missing shader {shaderName}.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static T Required<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException($"Required asset missing: {path}");

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int split = path.LastIndexOf('/');
            string parent = path.Substring(0, split);
            Folder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
        }
    }
}
