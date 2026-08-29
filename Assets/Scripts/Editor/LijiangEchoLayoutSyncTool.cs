using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 战斗场景「布局同步」工具:在关卡场景之间同步物件的位置/旋转,以及一键把怪物手臂重置回贴合基准姿势。
///
/// 背景:通用烘焙是在 Play 中捕获的,那一刻怪物手臂正处于运动偏移(且是旧的不同步运动)状态,
/// 于是烘焙下来的静止位置本身就是错开的 → 肘关节脱开。代码里所有怪物图层的 rest 位置都是
/// x=0,y=0(只有 z 不同,美术按此拼接),所以把它们重置回 (0,0,z)+旋转归零即贴合。
///
/// 三个菜单:
///   ① 采集当前场景布局:把战斗根下每个物体的 localPosition/旋转 记成 JSON(按相对路径,区分重名)。
///   ② 应用布局到当前场景:读 JSON,按路径匹配,套到当前场景 → 改好一关就能同步到其它关卡。
///   ③ 修复怪物手臂关节:把「怪物分层」下所有图层重置为 (0,0,z)+旋转归零,肘关节不再脱开。
/// 只同步位置/旋转,不碰缩放(缩放由 LijiangEchoSpriteLayer 按 fitSize 自动拟合,碰了会被覆盖/打架)。
/// </summary>
public static class LijiangEchoLayoutSyncTool
{
    private const string MarkerName = "怪物分层";
    private const string LayoutFile = "ValidationCaptures/BattleLayout.json";

    [Serializable]
    private class Item
    {
        public string path;
        public Vector3 pos;
        public Vector3 euler;
    }

    [Serializable]
    private class LayoutSet
    {
        public string capturedAt;
        public List<Item> items = new List<Item>();
    }

    [MenuItem("漓江回声/场景化/多关卡布局同步(高级)/采集本场景布局", false, 60)]
    public static void Capture()
    {
        Transform root = FindBattleRoot();
        if (root == null)
        {
            NoRoot();
            return;
        }

        LayoutSet set = new LayoutSet { capturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") };
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root)
            {
                continue;
            }

            set.items.Add(new Item { path = RelativePath(root, t), pos = t.localPosition, euler = t.localEulerAngles });
        }

        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", LayoutFile));
        Directory.CreateDirectory(Path.GetDirectoryName(full));
        File.WriteAllText(full, JsonUtility.ToJson(set, true));

        Debug.Log($"[漓江回声布局] 采集 {set.items.Count} 个物体位置/旋转 → {LayoutFile}");
        EditorUtility.DisplayDialog("采集完成",
            $"已记录 {set.items.Count} 个物体的位置/旋转。\n\n打开别的关卡战斗场景 → 执行「②应用布局到当前场景」即可同步。", "好");
    }

    [MenuItem("漓江回声/场景化/多关卡布局同步(高级)/应用布局到本场景", false, 61)]
    public static void Apply()
    {
        Transform root = FindBattleRoot();
        if (root == null)
        {
            NoRoot();
            return;
        }

        string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", LayoutFile));
        if (!File.Exists(full))
        {
            EditorUtility.DisplayDialog("没有布局数据", "请先在参考场景执行「①采集当前场景布局」。", "好");
            return;
        }

        LayoutSet set = JsonUtility.FromJson<LayoutSet>(File.ReadAllText(full));
        if (set == null || set.items.Count == 0)
        {
            EditorUtility.DisplayDialog("布局数据为空", "请重新采集。", "好");
            return;
        }

        Dictionary<string, Transform> map = new Dictionary<string, Transform>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root)
            {
                continue;
            }

            map[RelativePath(root, t)] = t;
        }

        Undo.RegisterFullObjectHierarchyUndo(root.gameObject, "应用布局");
        int applied = 0, missing = 0;
        foreach (Item item in set.items)
        {
            if (map.TryGetValue(item.path, out Transform t))
            {
                t.localPosition = item.pos;
                t.localEulerAngles = item.euler;
                applied++;
            }
            else
            {
                missing++;
            }
        }

        EditorSceneManager.MarkSceneDirty(root.gameObject.scene);
        Debug.Log($"[漓江回声布局] 应用布局:命中 {applied},本场景缺失 {missing}");
        EditorUtility.DisplayDialog("应用完成",
            $"已同步 {applied} 个物体的位置/旋转。{(missing > 0 ? $"\n{missing} 个在本场景找不到(已跳过)。" : "")}\n记得 Ctrl+S 保存。", "好");
    }

    [MenuItem("漓江回声/场景化/3 修怪物手臂关节脱开", false, 21)]
    public static void FixMonsterArms()
    {
        Transform root = FindBattleRoot();
        if (root == null)
        {
            NoRoot();
            return;
        }

        Transform monster = FindDeep(root, MarkerName);
        if (monster == null)
        {
            EditorUtility.DisplayDialog("没找到怪物分层", "本场景里没有「怪物分层」节点。", "好");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(monster.gameObject, "修复怪物手臂关节");
        int n = 0;
        for (int i = 0; i < monster.childCount; i++)
        {
            Transform c = monster.GetChild(i);
            c.localPosition = new Vector3(0f, 0f, c.localPosition.z); // 代码里所有怪物图层 rest 位置都是 x=0,y=0
            c.localRotation = Quaternion.identity;
            n++;
        }

        EditorSceneManager.MarkSceneDirty(monster.gameObject.scene);
        Debug.Log($"[漓江回声布局] 已把「怪物分层」下 {n} 个图层重置为贴合基准姿势(x=0,y=0,旋转归零)。");
        EditorUtility.DisplayDialog("已修复手臂关节",
            $"已把「怪物分层」下 {n} 个图层重置为贴合基准位(x=0,y=0,旋转归零)。\n" +
            "运行时动效会从这个贴合姿势一起摆动,肘关节不再脱开。\n记得 Ctrl+S 保存。", "好");
    }

    private static void NoRoot()
    {
        EditorUtility.DisplayDialog("没找到战斗场景根",
            "当前场景里没有含「怪物分层」的战斗根。请先打开烘焙出的战斗场景。", "好");
    }

    private static Transform FindBattleRoot()
    {
        foreach (GameObject go in SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (FindDeep(go.transform, MarkerName) != null)
            {
                return go.transform;
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

    private static string RelativePath(Transform root, Transform t)
    {
        string path = t.name;
        Transform cur = t.parent;
        while (cur != null && cur != root)
        {
            path = cur.name + "/" + path;
            cur = cur.parent;
        }

        return path;
    }
}
