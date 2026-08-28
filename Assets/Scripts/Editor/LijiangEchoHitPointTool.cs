using UnityEditor;
using UnityEngine;

/// <summary>
/// 生成「打击点」Prefab 模板的编辑器工具(对应会议:把带 Collider 的打击对象封装成 Prefab
/// 便于搭关卡)。生成后把 Prefab 拖进场景、指定 Sprite/类型/位置即可手摆关卡。
/// </summary>
public static class LijiangEchoHitPointTool
{
    [MenuItem("漓江回声/打击点/生成「打击点」Prefab 模板")]
    public static void CreateHitPointPrefab()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }

        GameObject go = new GameObject("打击点");
        go.AddComponent<SpriteRenderer>();

        BoxCollider2D collider = go.AddComponent<BoxCollider2D>();
        collider.size = new Vector2(0.3f, 0.3f);
        collider.isTrigger = true;

        go.AddComponent<LijiangHitPoint>();

        string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Prefabs/打击点.prefab");
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);

        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        EditorUtility.DisplayDialog(
            "已生成打击点 Prefab",
            "打击点 Prefab 已生成:\n" + path +
            "\n\n用法:把它从 Project 拖进场景 → 在 Inspector 的 LijiangHitPoint 上指定 Sprite(纹样)、" +
            "类型(单/双/长按)、位置 → 想摆几个摆几个,就成了一关的打击点布局。",
            "好");
    }
}
