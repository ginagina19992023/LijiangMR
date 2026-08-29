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

            // 注:不再要求 Read/Write。Crunch/压缩贴图无法开 Read/Write,像素改由
            // RenderTexture blit 读取(见 MakeReadableCopy),兼容任何格式、且不改压缩设置。
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

        // 计算不透明像素紧包围盒 + 内容中心相对贴图中心的偏移(局部单位)。
        // 用 GPU blit 拷一份可读副本 → 兼容 Crunch/压缩/未开 Read/Write 的贴图。
        Texture2D readable = MakeReadableCopy(tex);
        if (readable == null)
        {
            return "无法读取贴图像素(blit 失败)";
        }

        Color32[] px = readable.GetPixels32();
        int w = readable.width, h = readable.height;
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

        Object.DestroyImmediate(readable); // 用完释放副本

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
        root.AddComponent<LijiangEchoNoteCenterGizmo>(); // Scene 视图中心对齐点(游戏里不显示)

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

    private struct HandSpec
    {
        public string prefabName;
        public string artRel;
        public float reach;   // 手离轴心(肩)多远,即臂长
        public float height;  // 手显示高度(世界单位)
    }

    private static readonly HandSpec[] Hands =
    {
        new HandSpec { prefabName = "Hand_Left", artRel = "battle/7左手", reach = 1.0f, height = 1.6f },
        new HandSpec { prefabName = "Hand_Right", artRel = "battle/7右手", reach = 1.0f, height = 1.6f },
    };

    [MenuItem("漓江回声/纹样/生成左右手Prefab（可自己调位置/大小/深度）")]
    public static void GenerateHandPrefabs()
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
        foreach (HandSpec h in Hands)
        {
            string msg = BuildHand(h);
            report.AppendLine("· " + h.prefabName + ":" + msg);
            if (msg.StartsWith("OK"))
            {
                ok++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[漓江回声手部Prefab] 生成完成:\n" + report);
        EditorUtility.DisplayDialog("左右手 Prefab 生成完成",
            $"成功 {ok}/{Hands.Length},输出到 {OutFolder}/Hand_Left、Hand_Right\n\n{report}\n" +
            "双击 Prefab → 拖里面的『Visual』改手的位置(上移=离肩更远/更靠上)、大小、深度(z=离镜头)。\n" +
            "根上的红色中心点是『旋转轴心(肩)』。运行时只驱动旋转+淡入,样子全由你摆。", "好");
    }

    private static string BuildHand(HandSpec spec)
    {
        Texture2D tex = Resources.Load<Texture2D>("LijiangEchoArt/" + spec.artRel);
        if (tex == null)
        {
            return "找不到贴图 LijiangEchoArt/" + spec.artRel;
        }

        string texPath = AssetDatabase.GetAssetPath(tex);
        TextureImporter ti = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (ti != null && (ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single))
        {
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.SaveAndReimport();
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
        }

        Sprite spr = AssetDatabase.LoadAssetAtPath<Sprite>(texPath);
        if (spr == null)
        {
            return "贴图未生成 Sprite";
        }

        Texture2D readable = MakeReadableCopy(tex);
        if (readable == null)
        {
            return "无法读取贴图像素";
        }

        Color32[] px = readable.GetPixels32();
        int w = readable.width, h = readable.height;
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

        Object.DestroyImmediate(readable);
        if (maxX < minX)
        {
            return "整张透明";
        }

        float ppu = spr.pixelsPerUnit > 0f ? spr.pixelsPerUnit : 100f;
        float bboxHLocal = Mathf.Max(1e-4f, (maxY - minY + 1) / ppu);
        float ccx = (minX + maxX + 1) * 0.5f;
        float ccy = (minY + maxY + 1) * 0.5f;
        float offX = (ccx - w * 0.5f) / ppu;
        float offY = (ccy - h * 0.5f) / ppu;
        float scale = spec.height / bboxHLocal; // 按高度适配到目标显示高度

        GameObject root = new GameObject(spec.prefabName);
        root.AddComponent<LijiangEchoNoteCenterGizmo>(); // 根 = 旋转轴心(肩)

        GameObject visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);
        visual.transform.localScale = Vector3.one * scale;
        // 手内容中心放到轴心正上方 reach 处
        visual.transform.localPosition = new Vector3(-offX * scale, spec.reach - offY * scale, 0f);
        SpriteRenderer vr = visual.AddComponent<SpriteRenderer>();
        vr.sprite = spr;
        vr.sortingOrder = 240;
        vr.color = Color.white;

        string prefabPath = OutFolder + "/" + spec.prefabName + ".prefab";
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        Object.DestroyImmediate(root);
        return saved != null ? $"OK(reach {spec.reach}, height {spec.height})" : "保存失败";
    }

    [MenuItem("漓江回声/纹样/纹样Prefab → 白剪影(统一白色)")]
    public static void SetWhite()
    {
        ApplyVisualMaterial(true);
    }

    [MenuItem("漓江回声/纹样/纹样Prefab → 原彩色")]
    public static void SetColor()
    {
        ApplyVisualMaterial(false);
    }

    /// <summary>给 4 个纹样 Prefab 的 Visual 换材质:白剪影(LijiangEcho/WhiteSilhouette)或原彩色(默认材质);顺带补上中心点。</summary>
    private static void ApplyVisualMaterial(bool white)
    {
        Material whiteMat = white ? GetOrCreateNoteMaterial("LijiangEcho/WhiteSilhouette", "NoteWhite") : null;
        if (white && whiteMat == null)
        {
            EditorUtility.DisplayDialog("缺少着色器", "找不到 LijiangEcho/WhiteSilhouette。请确认 Assets/Shaders/WhiteSilhouette.shader 存在。", "好");
            return;
        }

        int n = 0;
        foreach (Spec spec in Specs)
        {
            string prefabPath = OutFolder + "/" + spec.prefabName + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                continue;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            Transform visual = root.transform.Find("Visual");
            if (visual != null)
            {
                SpriteRenderer sr = visual.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    sr.sharedMaterial = whiteMat; // null = 默认精灵材质(原彩色)
                }
            }

            if (root.GetComponent<LijiangEchoNoteCenterGizmo>() == null)
            {
                root.AddComponent<LijiangEchoNoteCenterGizmo>();
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            n++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[漓江回声纹样Prefab] 已把 {n} 个纹样设为{(white ? "白剪影" : "原彩色")}。");
        EditorUtility.DisplayDialog("完成", $"已把 {n} 个纹样 Prefab 的本体设为{(white ? "白剪影(统一白色)" : "原彩色")}。", "好");
    }

    private static Material GetOrCreateNoteMaterial(string shaderName, string matName)
    {
        string path = OutFolder + "/" + matName + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            Shader sh = Shader.Find(shaderName);
            if (sh == null)
            {
                return null;
            }

            m = new Material(sh) { name = matName };
            AssetDatabase.CreateAsset(m, path);
        }

        return m;
    }

    /// <summary>
    /// 用 RenderTexture blit 把任意贴图(压缩/Crunch/未开 Read/Write)拷成一份 CPU 可读的 RGBA32 副本。
    /// 这是编辑器里读像素最稳的方式,不需要也不改贴图的 Read/Write / 压缩设置。调用方用完需 DestroyImmediate。
    /// </summary>
    private static Texture2D MakeReadableCopy(Texture2D src)
    {
        if (src == null || src.width <= 0 || src.height <= 0)
        {
            return null;
        }

        RenderTexture rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        RenderTexture prev = RenderTexture.active;
        try
        {
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            Texture2D readable = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0f, 0f, src.width, src.height), 0, 0);
            readable.Apply();
            return readable;
        }
        catch
        {
            return null;
        }
        finally
        {
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
        }
    }
}
