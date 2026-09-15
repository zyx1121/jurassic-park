using JurassicPark.Scene;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    /// <summary>Builds Assets/Prefabs/Structures/Campfire.prefab: logs, emissive core, flame and ember particles, flickering point light.</summary>
    public static class BuildCampfirePrefab
    {
        public const string PrefabPath = "Assets/Prefabs/Structures/Campfire.prefab";

        [CliCommand("build_campfire_prefab", "Create the Campfire prefab with light and particles")]
        public static string Build()
        {
            GameObject root = new GameObject("Campfire");

            Material bark = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Bark.mat");
            Material stone = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Stone.mat");
            for (int i = 0; i < 3; i++)
            {
                GameObject log = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                log.name = "Log" + i;
                log.transform.SetParent(root.transform, false);
                log.transform.localScale = new Vector3(0.14f, 0.45f, 0.14f);
                log.transform.localPosition = new Vector3(0f, 0.12f, 0f);
                log.transform.localRotation = Quaternion.Euler(80f, i * 60f, 0f);
                log.GetComponent<MeshRenderer>().sharedMaterial = bark;
                Object.DestroyImmediate(log.GetComponent<Collider>());
            }

            for (int i = 0; i < 6; i++)
            {
                GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Cube);
                rock.name = "Stone" + i;
                rock.transform.SetParent(root.transform, false);
                float a = i * Mathf.PI * 2f / 6f;
                rock.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.62f, 0.1f, Mathf.Sin(a) * 0.62f);
                rock.transform.localScale = new Vector3(0.3f, 0.2f, 0.22f);
                rock.transform.localRotation = Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f);
                rock.GetComponent<MeshRenderer>().sharedMaterial = stone;
                Object.DestroyImmediate(rock.GetComponent<Collider>());
            }

            CapsuleCollider trigger = root.AddComponent<CapsuleCollider>();
            trigger.radius = 0.7f;
            trigger.height = 1.2f;
            trigger.center = new Vector3(0f, 0.5f, 0f);

            GameObject core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            core.name = "Embers";
            core.transform.SetParent(root.transform, false);
            core.transform.localScale = new Vector3(0.35f, 0.18f, 0.35f);
            core.transform.localPosition = new Vector3(0f, 0.16f, 0f);
            Object.DestroyImmediate(core.GetComponent<Collider>());
            Material ember = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Embers" };
            ember.SetColor("_BaseColor", new Color(1f, 0.35f, 0.08f));
            ember.EnableKeyword("_EMISSION");
            ember.SetColor("_EmissionColor", new Color(1f, 0.35f, 0.08f) * 5f);
            AssetDatabase.CreateAsset(ember, "Assets/Materials/Embers.mat");
            core.GetComponent<MeshRenderer>().sharedMaterial = ember;

            Material particleMat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")) { name = "FireParticle" };
            particleMat.SetFloat("_Surface", 1f);
            particleMat.SetFloat("_Blend", 1f); // additive
            particleMat.SetFloat("_ZWrite", 0f);
            particleMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            particleMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            particleMat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            particleMat.renderQueue = 3000;
            particleMat.SetColor("_BaseColor", Color.white);
            AssetDatabase.CreateAsset(particleMat, "Assets/Materials/FireParticle.mat");

            Flames(root.transform, particleMat);
            Embers(root.transform, particleMat);

            GameObject lightGo = new GameObject("FireLight");
            lightGo.transform.SetParent(root.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            Light fireLight = lightGo.AddComponent<Light>();
            fireLight.type = LightType.Point;
            fireLight.color = new Color(1f, 0.6f, 0.25f);
            fireLight.intensity = 34f;
            fireLight.range = 16f;
            fireLight.shadows = LightShadows.Soft;
            lightGo.AddComponent<CampfireLight>();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            AssetDatabase.SaveAssets();
            return AssetDatabase.GetAssetPath(prefab);
        }

        private static void Flames(Transform parent, Material mat)
        {
            GameObject go = new GameObject("Flames");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.2f, 0f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.6f, 0.15f, 0.9f), new Color(1f, 0.25f, 0.05f, 0.8f));
            main.maxParticles = 60;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 28f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.18f;
            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.1f)));
            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.85f, 0.4f), 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 0.5f), new GradientColorKey(new Color(0.3f, 0.05f, 0.02f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = g;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 1.2f;
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
        }

        private static void Embers(Transform parent, Material mat)
        {
            GameObject go = new GameObject("EmberSparks");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.3f, 0f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.09f);
            main.startColor = new Color(1f, 0.6f, 0.2f, 1f);
            main.maxParticles = 40;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;
            var emission = ps.emission;
            emission.rateOverTime = 10f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.2f;
            var noise = ps.noise;
            noise.enabled = true;
            noise.strength = 0.5f;
            noise.frequency = 0.8f;
            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            Gradient g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(new Color(1f, 0.8f, 0.3f), 0f), new GradientColorKey(new Color(1f, 0.3f, 0.05f), 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = g;
            ParticleSystemRenderer r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
        }
    }
}
