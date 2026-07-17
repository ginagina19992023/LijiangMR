using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class LijiangEchoAssetImportSettings : AssetPostprocessor
{
    private const string AudioRoot = "Assets/Resources/LijiangEchoAudio/";
    private const string FrogSpritePath = "Assets/Resources/LijiangEchoArt/battle/frog_swipe.png";

    private void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(AudioRoot))
        {
            return;
        }

        ConfigureAudioImporter((AudioImporter)assetImporter, assetPath);
    }

    private void OnPreprocessTexture()
    {
        if (assetPath != FrogSpritePath)
        {
            return;
        }

        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.isReadable = false;
        importer.maxTextureSize = 1024;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;

        TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
        android.overridden = true;
        android.maxTextureSize = 1024;
        android.format = TextureImporterFormat.ASTC_6x6;
        android.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.SetPlatformTextureSettings(android);
    }

    public static void ConfigureAll()
    {
        string audioDirectory = Path.Combine(Application.dataPath, "Resources/LijiangEchoAudio");
        if (Directory.Exists(audioDirectory))
        {
            foreach (string filePath in Directory.GetFiles(audioDirectory, "*.ogg", SearchOption.TopDirectoryOnly))
            {
                string assetPath = "Assets" + filePath.Substring(Application.dataPath.Length).Replace('\\', '/');
                AudioImporter importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
                if (importer == null)
                {
                    AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
                    importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
                }

                if (importer != null)
                {
                    ConfigureAudioImporter(importer, assetPath);
                    importer.SaveAndReimport();
                }
            }
        }

        TextureImporter frogImporter = AssetImporter.GetAtPath(FrogSpritePath) as TextureImporter;
        if (frogImporter != null)
        {
            frogImporter.textureType = TextureImporterType.Sprite;
            frogImporter.spriteImportMode = SpriteImportMode.Single;
            frogImporter.alphaIsTransparency = true;
            frogImporter.mipmapEnabled = false;
            frogImporter.isReadable = false;
            frogImporter.maxTextureSize = 1024;
            frogImporter.SaveAndReimport();
        }
    }

    public static void ConfigureFrogAndExit()
    {
        AssetDatabase.ImportAsset(FrogSpritePath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        EditorApplication.Exit(0);
    }

    private static void ConfigureAudioImporter(AudioImporter importer, string path)
    {
        bool stream = path.Contains("battle_music") ||
                      path.Contains("ambience") ||
                      path.Contains("water");
        AudioImporterSampleSettings settings = importer.defaultSampleSettings;
        settings.loadType = stream ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
        settings.compressionFormat = AudioCompressionFormat.Vorbis;
        settings.quality = stream ? 0.55f : 0.68f;
        settings.sampleRateSetting = AudioSampleRateSetting.OptimizeSampleRate;
        settings.preloadAudioData = !stream;
        importer.defaultSampleSettings = settings;
        importer.forceToMono = false;
        importer.loadInBackground = stream;
    }
}
