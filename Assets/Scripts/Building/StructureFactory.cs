using JurassicPark.Combat;
using JurassicPark.Scene;
using UnityEngine;
using UnityEngine.AI;

namespace JurassicPark.Building
{
    /// <summary>Builds structure GameObjects from definitions: simple pixel-textured geometry, colliders, NavMesh carving, health.</summary>
    public static class StructureFactory
    {
        public static Structure Place(StructureLibrary lib, StructureDef def, Vector3 groundCenter, int rotationSteps, Transform parent = null)
        {
            GameObject root;
            if (def.prefab != null)
            {
                root = Object.Instantiate(def.prefab, groundCenter, Quaternion.Euler(0f, rotationSteps * 90f, 0f), parent);
                root.name = def.displayName;
            }
            else
            {
                root = new GameObject(def.displayName);
                root.transform.SetParent(parent, false);
                root.transform.SetPositionAndRotation(groundCenter, Quaternion.Euler(0f, rotationSteps * 90f, 0f));
                BuildGeometry(lib, def, root.transform);
            }

            Vector2 size = new Vector2(def.footprint.x * lib.cellSize, def.footprint.y * lib.cellSize);
            if (def.solid)
            {
                BoxCollider col = root.GetComponent<BoxCollider>();
                if (col == null) col = root.AddComponent<BoxCollider>();
                col.size = new Vector3(size.x, def.height, size.y);
                col.center = new Vector3(0f, def.height * 0.5f, 0f);
                NavMeshObstacle obstacle = root.GetComponent<NavMeshObstacle>();
                if (obstacle == null) obstacle = root.AddComponent<NavMeshObstacle>();
                obstacle.shape = NavMeshObstacleShape.Box;
                obstacle.size = col.size;
                obstacle.center = col.center;
                obstacle.carving = true;
                obstacle.carveOnlyStationary = true;
            }
            else
            {
                SphereCollider trigger = root.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.radius = Mathf.Max(size.x, size.y) * 0.6f + 0.3f;
                trigger.center = new Vector3(0f, def.height * 0.5f, 0f);
            }

            Health health = root.GetComponent<Health>();
            if (health == null) health = root.AddComponent<Health>();
            health.Configure(def.health);
            if (root.GetComponent<HitFlash>() == null) root.AddComponent<HitFlash>();

            Structure structure = root.GetComponent<Structure>();
            if (structure == null) structure = root.AddComponent<Structure>();
            structure.Configure(def, groundCenter, rotationSteps);
            return structure;
        }

        private static void BuildGeometry(StructureLibrary lib, StructureDef def, Transform root)
        {
            float w = def.footprint.x * lib.cellSize;
            float d = def.footprint.y * lib.cellSize;
            switch (def.kind)
            {
                case StructureKind.Fence:
                    Post(lib.wood, root, new Vector3(-w * 0.5f + 0.12f, 0f, 0f), def.height);
                    Post(lib.wood, root, new Vector3(w * 0.5f - 0.12f, 0f, 0f), def.height);
                    Rail(lib.wood, root, def.height * 0.35f, w);
                    Rail(lib.wood, root, def.height * 0.8f, w);
                    break;
                case StructureKind.Wall:
                    Block(lib.stone, root, new Vector3(w, def.height, d), new Vector3(0f, def.height * 0.5f, 0f));
                    break;
                case StructureKind.Gate:
                    Post(lib.wood, root, new Vector3(-w * 0.5f + 0.12f, 0f, 0f), def.height + 0.3f);
                    Post(lib.wood, root, new Vector3(w * 0.5f - 0.12f, 0f, 0f), def.height + 0.3f);
                    GameObject leaf = new GameObject("Leaf");
                    leaf.transform.SetParent(root, false);
                    leaf.transform.localPosition = new Vector3(-w * 0.5f + 0.24f, 0f, 0f);
                    Rail(lib.wood, leaf.transform, def.height * 0.35f, w - 0.5f, offsetX: (w - 0.5f) * 0.5f);
                    Rail(lib.wood, leaf.transform, def.height * 0.8f, w - 0.5f, offsetX: (w - 0.5f) * 0.5f);
                    Post(lib.wood, leaf.transform, new Vector3(w - 0.5f, 0f, 0f), def.height);
                    break;
                case StructureKind.Torch:
                    Post(lib.wood, root, Vector3.zero, def.height, thickness: 0.16f);
                    GameObject flame = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    flame.name = "Flame";
                    Object.DestroyImmediate(flame.GetComponent<Collider>());
                    flame.transform.SetParent(root, false);
                    flame.transform.localPosition = new Vector3(0f, def.height + 0.15f, 0f);
                    flame.transform.localScale = new Vector3(0.22f, 0.3f, 0.22f);
                    Material ember = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    ember.SetColor("_BaseColor", new Color(1f, 0.45f, 0.1f));
                    ember.EnableKeyword("_EMISSION");
                    ember.SetColor("_EmissionColor", new Color(1f, 0.45f, 0.1f) * 2.2f);
                    flame.GetComponent<MeshRenderer>().sharedMaterial = ember;
                    GameObject lightGo = new GameObject("TorchLight");
                    lightGo.transform.SetParent(root, false);
                    lightGo.transform.localPosition = new Vector3(0f, def.height + 0.4f, 0f);
                    Light light = lightGo.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = new Color(1f, 0.62f, 0.3f);
                    light.intensity = 12f;
                    light.range = 8f;
                    light.shadows = LightShadows.None;
                    lightGo.AddComponent<CampfireLight>();
                    break;
                default:
                    Block(lib.wood, root, new Vector3(w, def.height, d), new Vector3(0f, def.height * 0.5f, 0f));
                    break;
            }
        }

        private static void Post(Material mat, Transform parent, Vector3 basePos, float height, float thickness = 0.24f)
        {
            GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
            post.name = "Post";
            Object.DestroyImmediate(post.GetComponent<Collider>());
            post.transform.SetParent(parent, false);
            post.transform.localPosition = basePos + new Vector3(0f, height * 0.5f, 0f);
            post.transform.localScale = new Vector3(thickness, height, thickness);
            post.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void Rail(Material mat, Transform parent, float y, float length, float offsetX = 0f)
        {
            GameObject rail = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rail.name = "Rail";
            Object.DestroyImmediate(rail.GetComponent<Collider>());
            rail.transform.SetParent(parent, false);
            rail.transform.localPosition = new Vector3(offsetX, y, 0f);
            rail.transform.localScale = new Vector3(length, 0.14f, 0.1f);
            rail.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        private static void Block(Material mat, Transform parent, Vector3 size, Vector3 center)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "Block";
            Object.DestroyImmediate(block.GetComponent<Collider>());
            block.transform.SetParent(parent, false);
            block.transform.localPosition = center;
            block.transform.localScale = size;
            block.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }
    }
}
