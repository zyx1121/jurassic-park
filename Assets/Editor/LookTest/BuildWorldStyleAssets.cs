using System.IO;
using JurassicPark.World;
using Unity.Pipeline.Commands;
using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    public static class BuildWorldStyleAssets
    {
        public const string ConfigPath = "Assets/Data/WorldStyle.asset";
        public const string OutputRoot = "Assets/Textures/Classic";

        [CliCommand("build_world_style_assets", "Create the shared original-art palette and scale settings")]
        public static string Build()
        {
            Load();
            AssetDatabase.SaveAssets();
            return ConfigPath;
        }

        public static WorldStyleConfig Load()
        {
            WorldStyleConfig config = AssetDatabase.LoadAssetAtPath<WorldStyleConfig>(ConfigPath);
            if (config != null) return config;
            config = ScriptableObject.CreateInstance<WorldStyleConfig>();
            AssetDatabase.CreateAsset(config, ConfigPath);
            return config;
        }

        public static Material RidgeMaterial(string name, Texture2D texture)
        {
            if (texture == null) throw new System.ArgumentException("Generate the terrain textures before camp ridges.");
            const string folder = "Assets/Materials/Classic";
            EnsureFolder(folder);
            string path = folder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Smoothness", 0f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        public static Texture2D Restyle(string sourcePath, string group, WorldStyleConfig config)
        {
            if (!sourcePath.StartsWith("Assets/Sprites/") && !sourcePath.StartsWith("Assets/Textures/Terrain/"))
                throw new System.ArgumentException("Only the project's own credited source art can be restyled.");
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string sourceFile = Path.Combine(projectRoot, sourcePath);
            if (!File.Exists(sourceFile)) throw new FileNotFoundException("Missing original project art.", sourcePath);
            string folder = OutputRoot + "/" + group;
            EnsureFolder(folder);
            string output = folder + "/" + Path.GetFileName(sourcePath);
            var source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D texture = null;
            try
            {
                if (!source.LoadImage(File.ReadAllBytes(sourceFile), false))
                    throw new System.InvalidOperationException("Could not decode original project art: " + sourcePath);
                Color32[] pixels = PixelStyle.Remap(source.GetPixels32(), source.width, source.height,
                    config.pixelStep, config.palette, out int width, out int height,
                    group == "Terrain" && Path.GetFileNameWithoutExtension(sourcePath).StartsWith("grass")
                        ? config.foliageRampColors : 0);
                texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(projectRoot, output), texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(source);
                if (texture != null) Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(output, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(output);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.wrapMode = group == "Terrain" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(output);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
