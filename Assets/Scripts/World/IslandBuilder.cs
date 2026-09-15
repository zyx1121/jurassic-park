using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>Instantiates an IslandPlan: terrain, facility markers, props. NavMesh baking is left to the caller (Editor or runtime surface).</summary>
    public static class IslandBuilder
    {
        public static GameObject Build(IslandPlan plan, IslandConfig cfg, Material propMaterial = null, Material seaMaterial = null)
        {
            TerrainConfig tc = cfg.terrain;
            GameObject root = new GameObject($"Island (seed {plan.seed})");

            TerrainData data = new TerrainData
            {
                heightmapResolution = tc.heightmapResolution,
                size = new Vector3(tc.size, tc.maxHeight, tc.size),
                alphamapResolution = Mathf.Max(16, tc.heightmapResolution - 1),
            };
            data.SetHeights(0, 0, plan.heights);
            if (tc.layers != null && tc.layers.Length >= TerrainNoise.LayerCount)
            {
                data.terrainLayers = tc.layers;
                data.SetAlphamaps(0, 0, TerrainNoise.Splat(plan.heights, plan.seed, tc, data.alphamapResolution));
            }

            GameObject terrainGo = Terrain.CreateTerrainGameObject(data);
            terrainGo.name = "Terrain";
            terrainGo.transform.SetParent(root.transform, false);
            terrainGo.transform.position = new Vector3(-tc.size * 0.5f, 0f, -tc.size * 0.5f);
            Terrain terrain = terrainGo.GetComponent<Terrain>();
            terrain.heightmapPixelError = 4f;
            terrain.drawInstanced = true;

            GameObject sea = GameObject.CreatePrimitive(PrimitiveType.Plane);
            sea.name = "Sea";
            Object.DestroyImmediate(sea.GetComponent<Collider>());
            sea.transform.SetParent(root.transform, false);
            sea.transform.position = new Vector3(0f, tc.seaLevel, 0f);
            sea.transform.localScale = new Vector3(tc.size / 5f * 1.5f, 1f, tc.size / 5f * 1.5f);
            if (seaMaterial != null) sea.GetComponent<MeshRenderer>().sharedMaterial = seaMaterial;

            GameObject facilities = new GameObject("Facilities");
            facilities.transform.SetParent(root.transform, false);
            foreach (FacilitySlot f in plan.facilities)
            {
                GameObject slot = new GameObject(f.name);
                slot.transform.SetParent(facilities.transform, false);
                slot.transform.position = f.position;
                slot.transform.rotation = Quaternion.Euler(0f, f.rotation, 0f);
                FacilityMarker marker = slot.AddComponent<FacilityMarker>();
                marker.facilityName = f.name;
                marker.isDock = f.isDock;
                // Placeholder footprint until the real facility prefabs (#38)
                GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pad.name = "Pad";
                pad.transform.SetParent(slot.transform, false);
                pad.transform.localScale = new Vector3(6f, 0.4f, 6f);
                pad.transform.localPosition = new Vector3(0f, 0.2f, 0f);
                GameObject post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = "Post";
                post.transform.SetParent(slot.transform, false);
                post.transform.localScale = new Vector3(0.4f, 3f, 0.4f);
                post.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            }

            GameObject baseGo = new GameObject("Base");
            baseGo.transform.SetParent(root.transform, false);
            baseGo.transform.position = plan.baseCenter;
            GameObject spawn = new GameObject("PlayerSpawn");
            spawn.transform.SetParent(baseGo.transform, false);
            spawn.transform.position = plan.playerSpawn;

            GameObject props = new GameObject("Props");
            props.transform.SetParent(root.transform, false);
            foreach (PropPlacement p in plan.props)
            {
                PropPlacer.Place(p.variant, p.position, p.scale, p.tint, props.transform, propMaterial, cfg.props.gatherRules);
            }

            return root;
        }
    }

    public sealed class FacilityMarker : MonoBehaviour
    {
        public string facilityName;
        public bool isDock;
    }
}
