using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 生成「可编辑描绘台 Prefab」的编辑器工具(9.1 需求第 4 条:图案与绘制路线没对齐、图案整体过小)。
///
/// 需求要的是「统一调整图案和绘制路线的位置、角度及比例,同时适当放大图案」——
/// 这件事本质上是对齐工作,靠改代码试参数很慢。生成 Prefab 后:
///   1. 双击 Assets/Resources/LijiangEchoTrace/TracePanel_0/1/2.prefab 进 Prefab 模式;
///   2. 里面能同时看到【参考纹样】和【描绘路线】两条线,拖/转/缩放到重合为止;
///   3. 想整体放大就选中根物件 TracePanel 一起放大,纹样和路线同步变大,不会再错位;
///   4. Ctrl+S 保存。运行时自动优先用它,判定路径就是你看到的那条线。
/// 删掉 Prefab 就回退到代码生成,游戏照常。
///
/// 【注意】两条线的物件名不能改(描绘路线_单手 / 描绘路线_双手右半),运行时按名字找。
/// 双手模式下左半是右半的水平镜像,由运行时自动补出,所以只需要对齐右半那条。
/// </summary>
public static class LijiangEchoTracePanelPrefabTool
{
    private const string FolderPath = "Assets/Resources/LijiangEchoTrace";
    private const string ArtRoot = "Assets/Resources/LijiangEchoArt/";

    // 三个图案:0=蛇纹 1=鸟纹 2=铜钱,与 TraceStageController 的 tracePaths 顺序一致。
    private static readonly string[] PatternArt = { "pattern/snake_trace", "pattern/bird_trace", "pattern/coin_trace" };
    private static readonly string[] PatternNames = { "蛇纹", "鸟纹", "铜钱" };
    private static readonly RectInt[] PatternCrops =
    {
        new RectInt(273, 2314, 1951, 2547),
        new RectInt(1822, 2125, 2973, 2185),
        new RectInt(995, 836, 1335, 1359)
    };

    // 与代码生成版一致的初值。需求说图案过小,这里已经把参考纹样从 0.88 提到 1.30;
    // 觉得还不够大就在 Prefab 里继续放大(记得纹样和路线一起放大)。
    private const float PatternHeight = 1.30f;
    private const float PanelWidth = 4.25f;

    [MenuItem("漓江回声/描绘台/生成三个可编辑描绘台 Prefab", false, 0)]
    private static void GenerateAll()
    {
        if (!AssetDatabase.IsValidFolder(FolderPath))
        {
            Directory.CreateDirectory(FolderPath);
            AssetDatabase.Refresh();
        }

        for (int i = 0; i < PatternArt.Length; i++)
        {
            GenerateOne(i);
        }

        AssetDatabase.Refresh();
        GameObject first = AssetDatabase.LoadAssetAtPath<GameObject>($"{FolderPath}/TracePanel_0.prefab");
        Selection.activeObject = first;
        EditorGUIUtility.PingObject(first);
        Debug.Log($"[漓江回声] 三个描绘台 Prefab 已生成到 {FolderPath}/TracePanel_0/1/2.prefab\n" +
                  "双击进 Prefab 模式,把【参考纹样】和【描绘路线】拖到重合;想整体放大就选根物件一起缩放。\n" +
                  "运行时自动优先用它,判定路径就是你看到的那条线。两条线的物件名请勿修改。");
    }

    private static void GenerateOne(int patternIndex)
    {
        GameObject root = new GameObject("TracePanel_" + patternIndex);

        AddSprite(root.transform, "描绘阶段淡紫边框", "transition/purple_frame",
            new Vector3(0f, 0f, 0f), 6.4f, -20, 0.14f);
        AddSprite(root.transform, "纹样描绘台", "pattern/drawing_card",
            new Vector3(0f, 0f, -0.22f), PanelWidth, -4, 0.72f);

        // 参考纹样先建空壳,Sprite 在 Prefab 存盘之后再作为子资源挂上去(见下面的说明)。
        GameObject patternObject = new GameObject("描绘参考纹样");
        patternObject.transform.SetParent(root.transform, false);
        patternObject.transform.localPosition = new Vector3(0f, 0.02f, -0.48f);
        SpriteRenderer patternRenderer = patternObject.AddComponent<SpriteRenderer>();
        patternRenderer.sortingOrder = 18;
        patternRenderer.color = new Color(1f, 1f, 1f, 0.74f);

        // 两条路线:单手用整条,双手只用右半(左半运行时镜像补出)。
        // 用的是 TraceStageController.BuildTracePath 同一份数学 —— Prefab 里看到的就是判定用的。
        AddPathLine(root.transform, TraceStageController.PathObjectOneHand,
            TraceStageController.BuildTracePath(patternIndex, false),
            new Color(1f, 0.9f, 0.55f, 0.55f));
        AddPathLine(root.transform, TraceStageController.PathObjectTwoHand,
            TraceStageController.BuildTracePath(patternIndex, true),
            new Color(0.55f, 0.95f, 1f, 0.55f));

        string path = $"{FolderPath}/TracePanel_{patternIndex}.prefab";
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);

        // 裁剪出来的 Sprite 是 Sprite.Create 造的运行时对象,不是资源;
        // 直接塞进 Prefab 存盘后引用会变成 None。必须先把它 AddObjectToAsset 挂成 Prefab 的子资源,
        // 再在 Prefab 资源上赋值并保存。
        AttachCroppedSprite(saved, path, patternIndex);
        Debug.Log($"[漓江回声] 已生成 {PatternNames[patternIndex]} 描绘台 → {path}");
    }

    private static void AttachCroppedSprite(GameObject prefabAsset, string prefabPath, int patternIndex)
    {
        if (prefabAsset == null)
        {
            return;
        }

        Transform target = prefabAsset.transform.Find("描绘参考纹样");
        SpriteRenderer renderer = target != null ? target.GetComponent<SpriteRenderer>() : null;
        if (renderer == null)
        {
            return;
        }

        Texture2D texture = LoadTexture(PatternArt[patternIndex]);
        if (texture == null)
        {
            Debug.LogWarning($"[漓江回声] 找不到贴图 {PatternArt[patternIndex]}," +
                             "「描绘参考纹样」留空,请在 Prefab 里手动指定 Sprite。");
            return;
        }

        RectInt crop = PatternCrops[patternIndex];
        int x = Mathf.Clamp(crop.x, 0, texture.width - 1);
        int width = Mathf.Clamp(crop.width, 1, texture.width - x);
        int height = Mathf.Clamp(crop.height, 1, texture.height);
        int y = Mathf.Clamp(texture.height - crop.y, 0, texture.height - height);   // 左上原点 → 左下原点

        Sprite sprite = Sprite.Create(texture, new Rect(x, y, width, height), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = "描绘参考纹样_" + PatternNames[patternIndex];

        AssetDatabase.AddObjectToAsset(sprite, prefabPath);
        renderer.sprite = sprite;

        float spriteHeight = sprite.bounds.size.y;
        if (spriteHeight > 0.0001f)
        {
            target.localScale = Vector3.one * (PatternHeight / spriteHeight);
        }

        PrefabUtility.SavePrefabAsset(prefabAsset);
        AssetDatabase.SaveAssets();
    }

    private static void AddPathLine(Transform parent, string objectName, Vector3[] points, Color color)
    {
        GameObject item = new GameObject(objectName);
        item.transform.SetParent(parent, false);

        LineRenderer line = item.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.positionCount = points.Length;
        line.SetPositions(points);
        line.widthMultiplier = 0.03f;
        line.numCapVertices = 4;
        line.numCornerVertices = 4;
        line.sortingOrder = 30;
        // 必须用「资源」材质:new Material(...) 是运行时对象,存进 Prefab 后引用会丢成 None。
        line.material = AssetDatabase.GetBuiltinExtraResource<Material>("Sprites-Default.mat");
        line.startColor = color;
        line.endColor = color;
    }

    private static void AddSprite(Transform parent, string objectName, string artPath,
        Vector3 localPosition, float targetWidth, int order, float alpha)
    {
        Sprite sprite = LoadSprite(artPath);
        GameObject item = new GameObject(objectName);
        item.transform.SetParent(parent, false);
        item.transform.localPosition = localPosition;

        SpriteRenderer renderer = item.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.sortingOrder = order;
        renderer.color = new Color(1f, 1f, 1f, alpha);

        if (sprite == null)
        {
            Debug.LogWarning($"[漓江回声] 找不到贴图 {artPath},「{objectName}」生成为空物件。");
            return;
        }

        float width = sprite.bounds.size.x;
        if (width > 0.0001f)
        {
            item.transform.localScale = Vector3.one * (targetWidth / width);
        }
    }

    private static Sprite LoadSprite(string artPath)
    {
        foreach (string ext in new[] { ".png", ".jpg" })
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ArtRoot + artPath + ext);
            if (sprite != null)
            {
                return sprite;
            }
        }

        return null;
    }

    private static Texture2D LoadTexture(string artPath)
    {
        foreach (string ext in new[] { ".png", ".jpg" })
        {
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ArtRoot + artPath + ext);
            if (texture != null)
            {
                return texture;
            }
        }

        return null;
    }
}
