using JurassicPark.Dinosaurs;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    /// <summary>Creates Assets/Data/SpawnTable.asset with the raptor entry.</summary>
    public static class BuildSpawnTable
    {
        public const string TablePath = "Assets/Data/SpawnTable.asset";

        [CliCommand("build_spawn_table", "Create the spawn table asset")]
        public static string Build()
        {
            SpawnTable table = AssetDatabase.LoadAssetAtPath<SpawnTable>(TablePath);
            if (table == null)
            {
                table = ScriptableObject.CreateInstance<SpawnTable>();
                AssetDatabase.CreateAsset(table, TablePath);
            }

            table.entries = new[]
            {
                new SpawnEntry
                {
                    species = "Raptor",
                    prefab = AssetDatabase.LoadAssetAtPath<GameObject>(BuildRaptorPrefab.PrefabPath),
                    stats = AssetDatabase.LoadAssetAtPath<DinosaurStats>("Assets/Data/RaptorStats.asset"),
                    fromNight = 1,
                    packSize = 3,
                    basePacks = 1,
                    packsPerNight = 0.5f,
                    cap = 9,
                },
            };
            EditorUtility.SetDirty(table);
            AssetDatabase.SaveAssets();
            return $"{TablePath} ({table.entries.Length} entries)";
        }
    }
}
