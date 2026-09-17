using UnityEngine;

namespace JurassicPark.World
{
    /// <summary>Original rock-edge geometry defining a buildable clearing and one southern approach.</summary>
    public static class CampRidges
    {
        public static GameObject Build(Vector3 center, IslandConfig config, Transform parent)
        {
            if (config.campEntranceWidth >= config.baseClearingRadius * 2f)
                throw new System.ArgumentException("The camp entrance must be narrower than the clearing.");
            var root = new GameObject("Camp rock ridges");
            root.transform.SetParent(parent, false);
            root.transform.position = center;
            float radius = config.baseClearingRadius;
            float thickness = config.campRidgeThickness;
            float wallCenter = radius + thickness * 0.5f;
            float span = radius * 2f + thickness * 2f;
            Segment(root.transform, "West ridge", new Vector3(-wallCenter, 0f, 0f),
                new Vector2(thickness, span), config);
            Segment(root.transform, "East ridge", new Vector3(wallCenter, 0f, 0f),
                new Vector2(thickness, span), config);
            Segment(root.transform, "North ridge", new Vector3(0f, 0f, wallCenter),
                new Vector2(radius * 2f, thickness), config);
            float shoulder = radius - config.campEntranceWidth * 0.5f;
            float shoulderCenter = config.campEntranceWidth * 0.5f + shoulder * 0.5f;
            Segment(root.transform, "Southwest ridge", new Vector3(-shoulderCenter, 0f, -wallCenter),
                new Vector2(shoulder, thickness), config);
            Segment(root.transform, "Southeast ridge", new Vector3(shoulderCenter, 0f, -wallCenter),
                new Vector2(shoulder, thickness), config);
            return root;
        }

        private static void Segment(Transform parent, string name, Vector3 position, Vector2 size, IslandConfig config)
        {
            GameObject stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stone.name = name;
            stone.transform.SetParent(parent, false);
            stone.transform.localPosition = position + Vector3.up * config.campRidgeHeight * 0.5f;
            stone.transform.localScale = new Vector3(size.x, config.campRidgeHeight, size.y);
            if (config.campRockMaterial != null) stone.GetComponent<Renderer>().sharedMaterial = config.campRockMaterial;
            GameObject cap = GameObject.CreatePrimitive(PrimitiveType.Quad);
            cap.name = "Grass cap";
            Object.DestroyImmediate(cap.GetComponent<Collider>());
            cap.transform.SetParent(parent, false);
            cap.transform.localPosition = position + Vector3.up * (config.campRidgeHeight + config.campCapOffset);
            cap.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            cap.transform.localScale = new Vector3(size.x, size.y, 1f);
            if (config.campTopMaterial != null) cap.GetComponent<Renderer>().sharedMaterial = config.campTopMaterial;
        }
    }
}
