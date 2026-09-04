using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 生成「可编辑暂停面板 Prefab」的编辑器工具(9.1 需求第 6 条)。
///
/// 和纹样 Prefab 同一套路:生成到 Resources/LijiangEchoMenu/PauseMenu.prefab 后,
/// 运行时 <see cref="LijiangEchoGameController"/> 会优先实例化它 —— 面板长什么样、图标多大、
/// 摆在哪,全部由你在 Prefab 里拖着定,不用改代码。删掉 Prefab 就回退到代码生成,游戏照常。
///
/// 【注意】四个图标物件的名字不能改:菜单主页 / 菜单音乐 / 菜单跳过 / 菜单返回。
/// 运行时按这几个名字找回它们 —— 悬停高亮和【点击判定区】都依赖这个,改名了这个按钮就点不动。
///
/// 位置和间距随便调:判定区是运行时按图标实际所在位置和包围盒算出来的(RebuildMenuHitRects),
/// 你在 Prefab 里怎么摆,能点的地方就在哪。根物件上的整体缩放也会保留。
/// </summary>
public static class LijiangEchoPauseMenuPrefabTool
{
    private const string PrefabPath = "Assets/Resources/LijiangEchoMenu/PauseMenu.prefab";
    private const string ArtRoot = "Assets/Resources/LijiangEchoArt/";

    // 与 LijiangEchoGameController 里的 MenuButtons / 尺寸常量保持一致。
    private const float IconSize = 0.66f;
    private const float IconY = 0.12f;
    private const float LabelY = -0.34f;

    private static readonly string[] IconArt = { "ui/home", "ui/music", "ui/skip", "ui/back" };
    private static readonly string[] Labels = { "主页", "音乐", "跳过", "返回" };
    private static readonly float[] PositionsX = { -1.30f, -0.44f, 0.44f, 1.30f };

    [MenuItem("漓江回声/暂停面板/生成可编辑暂停面板 Prefab", false, 0)]
    private static void GeneratePrefab()
    {
        string dir = Path.GetDirectoryName(PrefabPath);
        if (!AssetDatabase.IsValidFolder(dir))
        {
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }

        GameObject root = new GameObject("PauseMenu");

        AddSprite(root.transform, "系统菜单暗幕", "transition/purple_frame",
            new Vector3(0f, 0f, 0f), 6.4f, 80, 0.32f);
        AddSprite(root.transform, "系统菜单面板", "ui/card_back",
            new Vector3(0f, 0.04f, -0.64f), 3.75f, 82, 0.78f);

        for (int i = 0; i < IconArt.Length; i++)
        {
            // 名字必须是「菜单+标签」,运行时靠它找回来做悬停高亮。
            AddSprite(root.transform, "菜单" + Labels[i], IconArt[i],
                new Vector3(PositionsX[i], IconY, -0.7f), IconSize, 86, 0.96f, true);
            AddLabel(root.transform, Labels[i], new Vector3(PositionsX[i], LabelY, -0.72f));
        }

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.Refresh();

        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Selection.activeObject = saved;
        EditorGUIUtility.PingObject(saved);
        Debug.Log($"[漓江回声] 暂停面板 Prefab 已生成:{PrefabPath}\n" +
                  "双击它进 Prefab 模式即可拖着改大小/位置/换图;运行时会自动优先用它。\n" +
                  "四个图标物件的名字(菜单主页/菜单音乐/菜单跳过/菜单返回)请勿修改。");
    }

    [MenuItem("漓江回声/暂停面板/选中暂停面板 Prefab", false, 1)]
    private static void SelectPrefab()
    {
        GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (saved == null)
        {
            Debug.LogWarning($"[漓江回声] 还没有 {PrefabPath};先跑「生成可编辑暂停面板 Prefab」。");
            return;
        }

        Selection.activeObject = saved;
        EditorGUIUtility.PingObject(saved);
    }

    private static void AddSprite(Transform parent, string objectName, string artPath,
        Vector3 localPosition, float targetSize, int order, float alpha, bool byHeight = false)
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
            Debug.LogWarning($"[漓江回声] 找不到贴图 {artPath},「{objectName}」生成为空物件,请手动指定 Sprite。");
            return;
        }

        // 按目标宽度(或高度)换算缩放,和运行时 AddLayer/AddIcon 的观感一致。
        Vector2 size = sprite.bounds.size;
        float source = byHeight ? size.y : size.x;
        if (source > 0.0001f)
        {
            item.transform.localScale = Vector3.one * (targetSize / source);
        }
    }

    private static void AddLabel(Transform parent, string text, Vector3 localPosition)
    {
        GameObject item = new GameObject("文字" + text);
        item.transform.SetParent(parent, false);
        item.transform.localPosition = localPosition;

        TextMesh mesh = item.AddComponent<TextMesh>();
        mesh.text = text;
        mesh.characterSize = 0.024f;
        mesh.fontSize = 90;
        mesh.color = Color.white;
        mesh.anchor = TextAnchor.MiddleCenter;
        mesh.alignment = TextAlignment.Center;

        MeshRenderer renderer = item.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            renderer.sortingOrder = 90;
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
}
