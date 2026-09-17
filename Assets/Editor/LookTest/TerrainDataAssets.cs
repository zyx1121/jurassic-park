using JurassicPark.World;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    /// <summary>Saves generated TerrainData as assets so scenes stay text-serialized and loadable in players.</summary>
    [InitializeOnLoad]
    public static class TerrainDataAssets
    {
        private const string Folder = "Assets/Data/Generated";

        static TerrainDataAssets()
        {
            IslandBuilder.PersistTerrainData = Persist;
            TerrainBuilder.PersistTerrainData = Persist;
        }

        /// <summary>Baked NavMeshData left in memory also forces binary scenes; save it as an asset too.</summary>
        public static void PersistNavMesh(Unity.AI.Navigation.NavMeshSurface surface, string name)
        {
            if (surface == null || surface.navMeshData == null) return;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Data", "Generated");
            string path = $"{Folder}/{name}.asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(surface.navMeshData, path);
            AssetDatabase.SaveAssets();
            surface.navMeshData = AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(path);
            EditorUtility.SetDirty(surface);
        }

        public static TerrainData Persist(TerrainData data, string name)
        {
            float[,,] paint = data.alphamapLayers > 0
                ? data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight)
                : null;
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets/Data", "Generated");
            }

            string path = $"{Folder}/{name}.asset";
            TerrainData existing = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (existing != null && existing != data)
            {
                // Replacing a deleted terrain asset at the same path can reload stale control textures.
                // Update its native data in place so scene references and texture ownership stay stable.
                existing.heightmapResolution = data.heightmapResolution;
                existing.size = data.size;
                existing.alphamapResolution = data.alphamapResolution;
                existing.terrainLayers = data.terrainLayers;
                existing.SetHeights(0, 0, data.GetHeights(0, 0, data.heightmapResolution, data.heightmapResolution));
                Object.DestroyImmediate(data);
                data = existing;
            }
            else if (existing == null) AssetDatabase.CreateAsset(data, path);
            // Creating a TerrainData asset resets its control textures to layer zero in Unity 6.
            if (paint != null)
            {
                data.SetAlphamaps(0, 0, paint);
                foreach (Texture2D texture in data.alphamapTextures) EditorUtility.SetDirty(texture);
            }
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        }
    }
}
