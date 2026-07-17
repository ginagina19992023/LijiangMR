using UnityEditor;

/// <summary>
/// 漓江回声美术资源的自动导入规则。
/// 这些资源大多是透明分层图，统一按 Sprite 导入，并压到适合移动端运行的尺寸。
/// </summary>
public class LijiangEchoTexturePostprocessor : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (!assetPath.Contains("/Resources/LijiangEchoArt/"))
        {
            return;
        }

        bool compactRhythmSprite = assetPath.EndsWith("/battle/frog_swipe.png");
        int maximumSize = compactRhythmSprite ? 1024 : 2048;
        TextureImporter importer = (TextureImporter)assetImporter;
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 520f;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.isReadable = false;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = maximumSize;
        importer.textureCompression = compactRhythmSprite
            ? TextureImporterCompression.CompressedHQ
            : TextureImporterCompression.Compressed;
        importer.crunchedCompression = false;

        TextureImporterPlatformSettings androidSettings = new TextureImporterPlatformSettings
        {
            name = "Android",
            overridden = true,
            maxTextureSize = maximumSize,
            format = TextureImporterFormat.ASTC_6x6,
            textureCompression = compactRhythmSprite
                ? TextureImporterCompression.CompressedHQ
                : TextureImporterCompression.Compressed,
            compressionQuality = 50
        };
        importer.SetPlatformTextureSettings(androidSettings);
    }
}
