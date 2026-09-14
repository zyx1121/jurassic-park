using UnityEditor;
using UnityEngine;

namespace JurassicPark.EditorTools
{
    /// <summary>
    /// Pixel art defaults for everything under Assets/Sprites and Assets/Textures:
    /// point filtering, no compression, no mipmaps, so HD-2D sprites stay crisp.
    /// Runs only on first import so hand-tuned settings survive reimports.
    /// </summary>
    public sealed class PixelArtImportPostprocessor : AssetPostprocessor
    {
        private const int PixelsPerUnit = 64;

        private void OnPreprocessTexture()
        {
            if (!assetImporter.importSettingsMissing)
            {
                return;
            }

            bool isSprite = assetPath.StartsWith("Assets/Sprites/");
            bool isTexture = assetPath.StartsWith("Assets/Textures/");
            if (!isSprite && !isTexture)
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = isSprite ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;

            if (isSprite)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spritePixelsPerUnit = PixelsPerUnit;
            }
        }
    }
}
