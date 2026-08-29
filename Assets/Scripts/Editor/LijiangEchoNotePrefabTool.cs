using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 一键生成 4 个「可编辑纹样 Prefab」(鱼/鸟/蛇/蛙)到 Resources/LijiangEchoNotes/。
/// 在编辑器里(贴图可读)把每个纹样精确居中、按内容尺寸摆好,生成后完全交给你在 Inspector 改:
/// 换贴图、拖大小、挪位置、改光晕都行 —— 运行时会原样实例化你的 Prefab,飞进来只动位置+淡入,
/// 不再有任何运行时裁剪/居中/读像素(彻底绕过 Read/Write 不生效的问题)。
///
/// 若某个纹样 Prefab 不存在,运行时自动回退到旧的代码生成(不破坏现状)。
/// </summary>
public static class LijiangEchoNotePrefabTool
{
    private const string OutFolder = "Assets/Resources/LijiangEchoNotes";
    private const float AlphaThreshold = 12f;

    private struct Spec
    {
        public string prefabName;   // 运行时按此名加载(NotePrefabName 对应)
        public string artRel;       // Resources/LijiangEchoArt/ 下的相对路径
        public float targetSize;    // 内容较大边的目标世界尺寸(和旧代码 targetHeight*NoteSizeScale 同量级)

        public Spec(string n, string a, float s)
        {
            prefabName = n;
            artRel = a;
            targetSize = s;
        }
    }

    private static readonly Spec[] Specs =
    {
        new Spec("Note_Fish", "select/fish_symbol", 0.36f),
        new Spec("Note_Bird", "pattern/bird_done", 0.40f),
        new Spec("Note_Snake", "pattern/snake_done", 0.43f),
        new Spec("Note_Frog", "battle/frog_swipe", 0.22f),
    };

    [MenuItem("漓江回声/纹样/生成4个可编辑纹样Prefab")]
    public static void GenerateNotePrefabs()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
        }

        if (!AssetDatabase.IsValidFolder(OutFolder))
        {
            AssetDatabase.CreateFolder("Assets/Resources", "LijiangEchoNotes");
        }

        int ok = 0;
        System.Text.StringBuilder report = new System.Text.StringBuilder();
        foreach (Spec spec in Specs)
        {
            string msg = BuildOne(spec);
            report.AppendLine("· " + spec.prefabName + ":" + msg);
            if (msg.StartsWith("OK"))
            {
                ok++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[漓江回声纹样Prefab] 生成完成:\n" + report);
        EditorUtility.DisplayDialog("纹样 Prefab 生成完成",
            $"成功 {ok}/{Specs.Length} 个,输出到 {OutFolder}/\n\n{report}\n" +
            "现在可以双击任意 Note_* Prefab 直接改(换贴图/拖大小/挪位置/调光晕)。\n" +
            "运行时会自动用这些 Prefab;删掉某个则该纹样回退到旧代码生成。", "好");
    }

    private static string BuildOne(Spec spec)
    {
        Texture2D tex = Resources.Load<Texture2D>("LijiangEchoArt/" + spec.artRel);
        if (tex == null)
        {
            return "找不到贴图 LijiangEchoArt/" + spec.artRel;
        }

        string texPath = AssetDatabase.GetAssetPath(tex);
        TextureImporter ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (ti != null)
        {
            bool changed = false;
            if (ti.textureType != TextureImporterType.Sprite)
            {
                ti.textureType = TextureImporterType.Sprite;
                changed = true;
            }

            if (ti.spriteImportMode != SpriteImportMode.Single)
            {
                ti.spriteImportMode = SpriteImportMode.Single;
                changed = true;
            }

            if (!ti.isReadable)
            {
                ti.isReadable = true;
                changed = true;
            }

            if (changed)
            {
                ti.SaveAndReimport();
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
            }
        }

        Sprite spr = AssetDatabase.LoadAssetAtPath<Sprite>(texPath);
        if (spr == null)
        {
            return "贴图未生成 Sprite(导入类型异常)";
        }

        // 计算不透明像素紧包围盒 + 内容中心相对贴图中心的偏移(局部单位)
        Color32[] px;
        try
        {
            px = tex.GetPixels32();
        }
        catch
        {
            return "贴图不可读(Read/Write),无法计算居中";
        }

        int w = tex.width, h = tex.height;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        int step = Mathf.Max(1, Mathf.Max(w, h) / 512);
        for (int y = 0; y < h; y += step)
        {
            int row = y * w;
            for (int x = 0; x < w; x += step)
            {
                if (px[row + x].a >= AlphaThreshold)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (maxX < minX)
        {
            return "整张透明,无内容";
        }

        float ppu = spr.pixelsPerUnit > 0f ? spr.pixelsPerUnit : 100f;
        float bboxWLocal = (maxX - minX + 1) / ppu;
        float bboxHLocal = (maxY - minY + 1) / ppu;
        float contentMax = Mathf.Max(bboxWLocal, bboxHLocal, 1e-4f);
        float ccx = (minX + maxX + 1) * 0.5f; // 内容中心像素
        float ccy = (minY + maxY + 1) * 0.5f;
        float offXLocal = (ccx - w * 0.5f) / ppu;   // 相对贴图中心(=精灵 pivot)的偏移
        float offYLocal = (ccy - h * 0.5f) / ppu;
        float scale = spec.targetSize / contentMax;

        // 组装 Prefab:根(飞入时被驱动)→ Visual(本体)+ Glow(柔光)
        GameObject root = new GameObject(spec.prefabName);

        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = Vector3.one * scale;
        visual.transform.localPosition = new Vector3(-offXLocal * scale, -offYLocal * scale, 0f); // 内容中心对齐根原点
        SpriteRenderer vr = visual.AddComponent<SpriteRenderer>();
        vr.sprite = spr;
        vr.sortingOrder = 230;
        vr.color = Color.white; // 显示原彩色纹样(要白剪影可自行换材质)

        GameObject glow = new GameObject("Glow");
        glow.transform.SetParent(root.transform, false);
        float glowScale = scale * 1.35f;
        glow.transform.localScale = Vector3.one * glowScale;
        glow.transform.localPosition = new Vector3(-offXLocal * glowScale, -offYLocal * glowScale, 0.01f); // 与本体同心
        SpriteRenderer gr = glow.AddComponent<SpriteRenderer>();
        gr.sprite = spr;
        gr.sortingOrder = 229;
        gr.color = new Color(1f, 0.86f, 0.42f, 0.35f); // 金色柔光(可自行调/删)

        string prefabPath = OutFolder + "/" + spec.prefabName + ".prefab";
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);

        if (saved == null)
        {
            return "保存 Prefab 失败";
        }

        return $"OK(内容 {maxX - minX + 1}x{maxY - minY + 1}px,scale {scale:F3})";
    }
}
