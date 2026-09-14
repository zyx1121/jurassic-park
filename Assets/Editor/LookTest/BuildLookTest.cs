using JurassicPark.Core;
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
        public static string Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Ground
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(6f, 1f, 6f); // 60 x 60 m
            ground.GetComponent<MeshRenderer>().sharedMaterial = PixelMaterial("Grass", "Assets/Textures/Terrain/grass.png", tiling: 30f);

            // Dirt patch under the camp
            GameObject patch = GameObject.CreatePrimitive(PrimitiveType.Plane);
            patch.name = "DirtPatch";
            patch.transform.position = new Vector3(0f, 0.01f, 0f);
            patch.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
            patch.GetComponent<MeshRenderer>().sharedMaterial = PixelMaterial("Dirt", "Assets/Textures/Terrain/dirt.png", tiling: 3f);

            // Rocks
            Material stone = PixelMaterial("Stone", "Assets/Textures/Terrain/stone.png", tiling: 1f);
            Rock(stone, new Vector3(-4f, 0f, 3f), 1.4f);
            Rock(stone, new Vector3(5f, 0f, -2f), 1.0f);
            Rock(stone, new Vector3(3f, 0f, 6f), 0.7f);

            // Trees: pixel-textured trunk and canopy
            Material bark = PixelMaterial("Bark", "Assets/Textures/Terrain/bark.png", tiling: 2f);
            Material leaves = PixelMaterial("Leaves", "Assets/Textures/Terrain/leaves.png", tiling: 2f);
            Vector3[] trees =
            {
                new Vector3(-8f, 0f, 8f), new Vector3(-3f, 0f, 11f), new Vector3(4f, 0f, 12f), new Vector3(9f, 0f, 9f),
                new Vector3(11f, 0f, 2f), new Vector3(-10f, 0f, 1f), new Vector3(-7f, 0f, -6f), new Vector3(8f, 0f, -7f),
                new Vector3(0f, 0f, 16f), new Vector3(-12f, 0f, 12f), new Vector3(13f, 0f, 14f),
            };
            for (int i = 0; i < trees.Length; i++)
            {
                Tree(bark, leaves, trees[i], 3.5f + (i % 3) * 0.8f);
            }

            // Campfire: emissive logs and a warm point light
            GameObject fire = new GameObject("Campfire");
            fire.transform.position = new Vector3(2f, 0f, -1f);
            GameObject logs = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            logs.name = "Logs";
            logs.transform.SetParent(fire.transform, false);
            logs.transform.localScale = new Vector3(0.8f, 0.15f, 0.8f);
            logs.transform.localPosition = new Vector3(0f, 0.15f, 0f);
            logs.GetComponent<MeshRenderer>().sharedMaterial = bark;
            GameObject flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flame.name = "Flame";
            flame.transform.SetParent(fire.transform, false);
            flame.transform.localScale = new Vector3(0.5f, 0.7f, 0.5f);
            flame.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            Object.DestroyImmediate(flame.GetComponent<Collider>());
            flame.GetComponent<MeshRenderer>().sharedMaterial = EmissiveMaterial("Flame", new Color(1f, 0.45f, 0.1f), 2.2f);
            GameObject lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(fire.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.2f, 0f);
            Light fireLight = lightGo.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.6f, 0.25f);
            fireLight.intensity = 30f;
            fireLight.range = 14f;
            fireLight.shadows = LightShadows.Soft;

            // Raptor sprite on a lit quad so it receives scene light and casts a shadow
            Raptor("Raptor", "Assets/Sprites/Dinosaurs/raptor_idle_left.png", new Vector3(-1.5f, 0f, 1.5f));
            Raptor("Raptor2", "Assets/Sprites/Dinosaurs/raptor_idle_front.png", new Vector3(4.5f, 0f, 5f));

            // Dusk sun
            GameObject sunGo = new GameObject("Sun");
            Light sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.55f, 0.5f, 0.75f);
            sun.intensity = 1.1f;
            sun.shadows = LightShadows.Soft;
            sunGo.transform.rotation = Quaternion.Euler(28f, -35f, 0f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.22f, 0.2f, 0.34f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.13f, 0.1f, 0.22f);
            RenderSettings.fogStartDistance = 14f;
            RenderSettings.fogEndDistance = 42f;

            // Camera: perspective, tilted 30 degrees, looking at the camp
            GameObject camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = 32f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 80f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.1f, 0.22f);
            camGo.transform.position = new Vector3(0f, 9f, -14f);
            camGo.transform.rotation = Quaternion.Euler(30f, 0f, 0f);
            UniversalAdditionalCameraData camData = camGo.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.None;

            // Post-processing volume
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, "Assets/Settings/LookTestProfile.asset");
            DepthOfField dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(16f);
            dof.aperture.Override(2.8f);
            dof.focalLength.Override(90f);
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.0f);
            bloom.intensity.Override(1.2f);
            bloom.scatter.Override(0.65f);
            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.5f);
            ColorAdjustments grade = profile.Add<ColorAdjustments>(true);
            grade.contrast.Override(18f);
            grade.saturation.Override(-8f);
            grade.colorFilter.Override(new Color(0.9f, 0.88f, 1f));
            EditorUtility.SetDirty(profile);
            GameObject volGo = new GameObject("PostProcessVolume");
            Volume vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true;
            vol.sharedProfile = profile;

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

        private static void Rock(Material stone, Vector3 pos, float size)
        {
            GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rock.name = "Rock";
            rock.transform.position = pos + new Vector3(0f, size * 0.35f, 0f);
            rock.transform.localScale = new Vector3(size * 1.6f, size * 0.8f, size * 1.2f);
            rock.transform.rotation = Quaternion.Euler(0f, pos.x * 37f, 0f);
            rock.GetComponent<MeshRenderer>().sharedMaterial = stone;
        }

        private static void Tree(Material bark, Material leaves, Vector3 pos, float height)
        {
            GameObject tree = new GameObject("Tree");
            tree.transform.position = pos;
            GameObject trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            trunk.transform.SetParent(tree.transform, false);
            trunk.transform.localScale = new Vector3(0.5f, height * 0.5f, 0.5f);
            trunk.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            trunk.GetComponent<MeshRenderer>().sharedMaterial = bark;
            GameObject canopy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            canopy.transform.SetParent(tree.transform, false);
            canopy.transform.localScale = new Vector3(3.2f, 2.4f, 3.2f);
            canopy.transform.localPosition = new Vector3(0f, height + 0.6f, 0f);
            canopy.GetComponent<MeshRenderer>().sharedMaterial = leaves;
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
