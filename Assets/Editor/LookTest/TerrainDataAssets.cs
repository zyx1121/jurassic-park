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
            if (!AssetDatabase.IsValidFolder(Folder))
            {
                AssetDatabase.CreateFolder("Assets/Data", "Generated");
            }

            string path = $"{Folder}/{name}.asset";
            TerrainData existing = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (existing != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<TerrainData>(path);
        }
    }
}
