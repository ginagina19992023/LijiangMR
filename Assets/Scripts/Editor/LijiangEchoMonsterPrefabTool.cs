using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 把战斗场景里的怪物(「怪物分层」整棵子树)做成一个共用 Prefab,让所有关卡都引用同一个 Prefab 实例。
/// 之后只要改这个 Prefab(位置/动效/贴图),所有用它的关卡自动同步 —— 比"逐关同步位置"更工程化。
///
/// 用法:
///   先在参考关(如 Battle_level1)把怪物调好:布局同步/③ 修手臂关节 + 为战斗背景补挂动效组件。
///   ① 把当前「怪物分层」存为怪物 Prefab:生成 Assets/Prefabs/BattleMonster.prefab,并把本场景的
///      怪物连接成该 Prefab 的实例(以后改 Prefab,这一关也跟着变)。
///   ② 用怪物 Prefab 替换本场景「怪物分层」:在其它关卡场景执行,把它本地那份怪物换成 Prefab 实例
///      (保留原来的位置/旋转/缩放)。这样所有关卡共用一个怪物。
/// </summary>
public static class LijiangEchoMonsterPrefabTool
{
    private const string MarkerName = "怪物分层";
    private const string PrefabPath = "Assets/Prefabs/BattleMonster.prefab";

    [MenuItem("漓江回声/场景化/4 怪物做成共用Prefab(改一次全关卡同步)/A 把本场景怪物存成Prefab", false, 40)]
    public static void SaveMonsterPrefab()
    {
        Transform monster = FindMonster();
        if (monster == null)
        {
            EditorUtility.DisplayDialog("没找到怪物", "当前场景里没有「怪物分层」节点。请先打开战斗关卡场景。", "好");
            return;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }

        GameObject prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(monster.gameObject, PrefabPath, InteractionMode.UserAction);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("保存失败", "无法保存 Prefab,请看 Console。", "好");
            return;
        }

        EditorSceneManager.MarkSceneDirty(monster.gameObject.scene);
        Debug.Log($"[漓江回声怪物Prefab] 已保存 {PrefabPath},并把本场景怪物连接为其实例。");
        EditorUtility.DisplayDialog("怪物 Prefab 已生成",
            $"已保存:\n{PrefabPath}\n\n本场景的怪物已连接成该 Prefab 的实例(改 Prefab 会同步到这里)。\n" +
            "其它关卡请打开后执行「② 用怪物Prefab替换本场景怪物分层」。\n记得 Ctrl+S。", "好");
    }

    [MenuItem("漓江回声/场景化/4 怪物做成共用Prefab(改一次全关卡同步)/B 别的关卡用它替换本地怪物", false, 41)]
    public static void ReplaceWithPrefab()
    {
        GameObject prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefabAsset == null)
        {
            EditorUtility.DisplayDialog("没有怪物 Prefab", $"未找到 {PrefabPath}。请先在参考关执行「① 存为怪物Prefab」。", "好");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        Transform existing = FindMonster();

        // 记录旧怪物的父节点/局部变换/兄弟序,替换后保持一致的位置
        Transform parent = existing != null ? existing.parent : null;
        Vector3 pos = existing != null ? existing.localPosition : Vector3.zero;
        Quaternion rot = existing != null ? existing.localRotation : Quaternion.identity;
        Vector3 scale = existing != null ? existing.localScale : Vector3.one;
        int siblingIndex = existing != null ? existing.GetSiblingIndex() : -1;

        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, scene);
        Undo.RegisterCreatedObjectUndo(inst, "替换为怪物Prefab");
        inst.transform.SetParent(parent, false);
        if (existing != null)
        {
            inst.transform.localPosition = pos;
            inst.transform.localRotation = rot;
            inst.transform.localScale = scale;
            if (siblingIndex >= 0)
            {
                inst.transform.SetSiblingIndex(siblingIndex);
            }

            Undo.DestroyObjectImmediate(existing.gameObject);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"[漓江回声怪物Prefab] 已用 {PrefabPath} 替换本场景怪物({(existing != null ? "替换了旧怪物" : "本场景原无怪物,直接放入")})。");
        EditorUtility.DisplayDialog("已替换为怪物 Prefab",
            $"本场景怪物已换成共用 Prefab 实例{(existing != null ? "(位置/旋转/缩放沿用旧的)" : "")}。\n" +
            "以后改 Prefab,这一关也会跟着变。\n记得 Ctrl+S。", "好");
    }

    private static Transform FindMonster()
    {
        foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            Transform found = FindDeep(go.transform, MarkerName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static Transform FindDeep(Transform current, string targetName)
    {
        if (current.name == targetName)
        {
            return current;
        }

        for (int i = 0; i < current.childCount; i++)
        {
            Transform found = FindDeep(current.GetChild(i), targetName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
