using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 为「烘焙后的战斗背景场景」按图层名补挂动效组件(LijiangEchoMotion)并挂上通用驱动器
/// (BakedSceneMotionDriver),让烘焙出来的静态战斗背景动起来。
///
/// 背景:通用烘焙工具(通用A/通用B)只搬静态美术,不带动效。这里把
/// LijiangEchoGameController.BuildBattleBackground 里那张动效表(含已修好的「关节锁定」手臂参数:
/// 同一条手臂的大臂/小臂共用相同振幅·频率·相位,肘关节不再脱开)按图层名一次性补上。
///
/// 用法:打开烘焙出的战斗背景场景 → 执行本菜单。已存在的 LijiangEchoMotion 会被更新为表里的参数,
/// 未匹配到的图层名会在 Console 列出以便核对。补完可在 Inspector 里逐层微调。
/// </summary>
public static class LijiangEchoBattleMotionTool
{
    private struct MotionSpec
    {
        public LijiangEchoStageKit.MotionKind kind;
        public float amplitude;
        public float speed;
        public float phase;

        public MotionSpec(LijiangEchoStageKit.MotionKind k, float a, float s, float p)
        {
            kind = k;
            amplitude = a;
            speed = s;
            phase = p;
        }
    }

    // 图层名 → 动效参数。与 BuildBattleBackground 保持一致(手臂为关节锁定后的参数)。
    private static readonly Dictionary<string, MotionSpec> Table = new Dictionary<string, MotionSpec>
    {
        { "怪物完整底层", new MotionSpec(LijiangEchoStageKit.MotionKind.Monster, 0.012f, 1.2f, 0.2f) },
        { "怪物左翼", new MotionSpec(LijiangEchoStageKit.MotionKind.Wing, 0.026f, 2.8f, 0f) },
        { "怪物右翼", new MotionSpec(LijiangEchoStageKit.MotionKind.Wing, 0.026f, 2.8f, 1.4f) },
        { "怪物身体", new MotionSpec(LijiangEchoStageKit.MotionKind.Monster, 0.018f, 1.4f, 0.6f) },
        { "怪物手臂合层", new MotionSpec(LijiangEchoStageKit.MotionKind.Hand, 0.018f, 4.2f, 1.1f) },
        // 关节锁定:小臂与同侧大臂参数完全一致 → 肘部始终贴合
        { "怪物左上大臂", new MotionSpec(LijiangEchoStageKit.MotionKind.Hand, 0.022f, 4.6f, 0.2f) },
        { "怪物左上小臂", new MotionSpec(LijiangEchoStageKit.MotionKind.Hand, 0.022f, 4.6f, 0.2f) },
        { "怪物右上大臂", new MotionSpec(LijiangEchoStageKit.MotionKind.Hand, 0.022f, 4.7f, 1.1f) },
        { "怪物右上小臂", new MotionSpec(LijiangEchoStageKit.MotionKind.Hand, 0.022f, 4.7f, 1.1f) },
        { "怪物左下大臂", new MotionSpec(LijiangEchoStageKit.MotionKind.Hand, 0.02f, 4.1f, 2.4f) },
        { "怪物左下小臂", new MotionSpec(LijiangEchoStageKit.MotionKind.Hand, 0.02f, 4.1f, 2.4f) },
        { "怪物右下大臂", new MotionSpec(LijiangEchoStageKit.MotionKind.Hand, 0.02f, 4.2f, 3.2f) },
        { "怪物右下小臂", new MotionSpec(LijiangEchoStageKit.MotionKind.Hand, 0.02f, 4.2f, 3.2f) },
        { "宽火焰", new MotionSpec(LijiangEchoStageKit.MotionKind.Flame, 0.035f, 5.8f, 0f) },
        { "火焰光", new MotionSpec(LijiangEchoStageKit.MotionKind.Flame, 0.05f, 7.2f, 1.3f) },
    };

    [MenuItem("漓江回声/场景化/为战斗背景补挂动效组件")]
    public static void ApplyBattleMotions()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("没有场景", "请先打开烘焙出的战斗背景场景再执行。", "好");
            return;
        }

        int applied = 0;
        HashSet<GameObject> rootsWithMatches = new HashSet<GameObject>();
        List<string> matchedNames = new List<string>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (!Table.TryGetValue(t.name, out MotionSpec spec))
                {
                    continue;
                }

                LijiangEchoMotion m = t.GetComponent<LijiangEchoMotion>();
                if (m == null)
                {
                    m = Undo.AddComponent<LijiangEchoMotion>(t.gameObject);
                }
                else
                {
                    Undo.RecordObject(m, "更新战斗动效参数");
                }

                m.kind = spec.kind;
                m.amplitude = spec.amplitude;
                m.speed = spec.speed;
                m.phase = spec.phase;
                EditorUtility.SetDirty(m);

                applied++;
                matchedNames.Add(t.name);
                rootsWithMatches.Add(root);
            }
        }

        // 给含匹配层的根节点挂上通用驱动器(每帧驱动收集到的动效层)。
        int drivers = 0;
        foreach (GameObject root in rootsWithMatches)
        {
            if (root.GetComponent<BakedSceneMotionDriver>() == null)
            {
                Undo.AddComponent<BakedSceneMotionDriver>(root);
                drivers++;
            }
        }

        // 列出表里没匹配到的图层名,便于核对烘焙后的命名是否一致。
        List<string> missing = new List<string>();
        foreach (string key in Table.Keys)
        {
            if (!matchedNames.Contains(key))
            {
                missing.Add(key);
            }
        }

        if (applied > 0)
        {
            EditorSceneManager.MarkSceneDirty(scene);
        }

        string missingMsg = missing.Count == 0
            ? "全部图层名都匹配上了。"
            : "以下图层名未在场景中找到(可能烘焙后改名了,请核对或告诉我实际名字):\n · " + string.Join("\n · ", missing);
        Debug.Log($"[漓江回声] 战斗动效补挂:匹配 {applied} 层,新挂驱动器 {drivers} 个。{(missing.Count > 0 ? "未匹配:" + string.Join("、", missing) : "")}");
        EditorUtility.DisplayDialog(
            "战斗动效补挂完成",
            $"已给 {applied} 个图层补/更新 LijiangEchoMotion,新挂 {drivers} 个驱动器。\n\n{missingMsg}\n\n" +
            "记得 Ctrl+S 保存场景。运行该场景即可看到背景/怪物/火焰动起来。",
            "好");
    }
}
