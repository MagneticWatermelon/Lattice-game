using UnityEditor;
using UnityEngine;

namespace Lattice.EditorTools
{
    /// <summary>
    /// Import rules for the game's textures, by name: *_normal are normal
    /// maps, *_mask (packed metallic, AO, height, smoothness) are linear,
    /// everything gets high-quality compression, and the ground's textures
    /// strong anisotropic filtering (they're seen at grazing angles).
    /// </summary>
    sealed class TextureRules : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/Lattice/"))
                return;
            var t = (TextureImporter)assetImporter;
            var name = System.IO.Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();
            bool ground = assetPath.Contains("/World/");
            if (name.EndsWith("_normal"))
            {
                t.textureType = TextureImporterType.NormalMap;
                t.sRGBTexture = false;
            }
            else if (name.EndsWith("_mask") || name.EndsWith("_data") || name.EndsWith("_metal"))
            {
                t.textureType = TextureImporterType.Default;
                t.sRGBTexture = false;
            }
            else if (name.EndsWith("_albedo"))
            {
                t.textureType = TextureImporterType.Default;
                t.sRGBTexture = true;
            }
            else
                return;
            t.mipmapEnabled = true;
            t.streamingMipmaps = true;
            t.textureCompression = TextureImporterCompression.CompressedHQ;
            t.maxTextureSize = 2048;
            t.anisoLevel = ground ? 8 : 4;
            t.filterMode = FilterMode.Trilinear;
            t.wrapMode = TextureWrapMode.Repeat;
        }
    }
}
