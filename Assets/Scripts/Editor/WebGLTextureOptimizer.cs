using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// itch.io rejects HTML builds with any file over 200 MB, and Unity packs every asset into one
/// .data file. Most of it was uncompressed 1-4K textures that the PSX look (40% render scale)
/// never shows. This adds WebGL-only overrides to every texture the build uses:
/// 3D textures max 512 px, UI/sprites and skyboxes max 1024 px, all GPU-compressed.
/// Windows/macOS builds keep full quality. Re-run after adding new art.
/// </summary>
public static class WebGLTextureOptimizer
{
    private const string Platform = "WebGL";
    private const int WorldTextureSize = 512;
    private const int UiAndSkySize = 1024;

    [MenuItem("Trick or Threat/Optimize Textures for WebGL")]
    public static void Optimize()
    {
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        string[] textures = AssetDatabase.GetDependencies(scenes, true)
            .Where(path => AssetImporter.GetAtPath(path) is TextureImporter)
            .ToArray();

        int changed = 0;
        try
        {
            AssetDatabase.StartAssetEditing();
            for (int i = 0; i < textures.Length; i++)
            {
                string path = textures[i];
                if (EditorUtility.DisplayCancelableProgressBar("Optimize Textures for WebGL", path, i / (float)textures.Length))
                    break;

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                bool uiOrSky = importer.textureType == TextureImporterType.Sprite ||
                               importer.textureType == TextureImporterType.GUI ||
                               importer.textureShape == TextureImporterShape.TextureCube ||
                               path.EndsWith(".hdr") || path.EndsWith(".exr");
                int maxSize = uiOrSky ? UiAndSkySize : WorldTextureSize;

                TextureImporterPlatformSettings current = importer.GetPlatformTextureSettings(Platform);
                if (current.overridden && current.maxTextureSize <= maxSize &&
                    current.textureCompression != TextureImporterCompression.Uncompressed)
                    continue; // already small enough (or set by hand): leave it

                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = Platform,
                    overridden = true,
                    maxTextureSize = Mathf.Min(maxSize, current.overridden ? current.maxTextureSize : maxSize),
                    format = TextureImporterFormat.Automatic,
                    textureCompression = TextureImporterCompression.Compressed,
                    compressionQuality = 50,
                    crunchedCompression = false
                });
                importer.SaveAndReimport();
                changed++;
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            EditorUtility.ClearProgressBar();
        }

        Debug.Log($"WebGLTextureOptimizer: {changed} of {textures.Length} build textures now have WebGL overrides " +
                  $"({WorldTextureSize}px world, {UiAndSkySize}px UI/sky, compressed). Desktop builds are unchanged.");
    }
}
