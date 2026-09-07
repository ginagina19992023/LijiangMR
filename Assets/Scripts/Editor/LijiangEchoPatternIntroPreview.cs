using UnityEditor;
using UnityEngine;

/// <summary>
/// 单独预览扫码功能的【入场动画】(见 docs/TODO-QR-PATTERN-SCAN.md 第②段)。
///
/// 扫码那部分还没做,但入场动画本身不依赖摄像头,所以可以先单独跑起来看效果。
///
/// 两条路:
///   ①【推荐,不用 Play】先点"在场景里创建预览物体",然后在 Inspector 里勾 previewInEditor、
///      拖 previewTime 时间轴,在 Scene 视图里转视角看 —— 这套动画有纵深(鸟忽远忽近、
///      蛇一半绕到圈后面),Game 视图正对着看是看不出来的。
///   ② Play 起来之后点下面四个菜单,动画在玩家面前实时播一遍(带音效)。
/// </summary>
public static class LijiangEchoPatternIntroPreview
{
    private const string Root = "漓江回声/5 调试/扫码入场动画预览/";
    private const string ObjectName = "漓江回声_扫码入场动画";

    // ————————————————————————————— 不用 Play 的那条路 —————————————————————————————

    /// <summary>这个脚本本来不挂在任何场景里(它是运行时被扫码流程拉起来的),
    /// 所以想在编辑器里调轨迹,得先有个物体承载它。这个菜单就是干这个的。</summary>
    [MenuItem(Root + "在场景里创建预览物体(不用 Play)", false, 0)]
    private static void CreatePreviewObject()
    {
        LijiangEchoPatternIntro intro = Object.FindFirstObjectByType<LijiangEchoPatternIntro>();
        GameObject host;

        if (intro != null)
        {
            host = intro.gameObject;
        }
        else
        {
            host = new GameObject(ObjectName);
            intro = host.AddComponent<LijiangEchoPatternIntro>();
            Undo.RegisterCreatedObjectUndo(host, "创建扫码入场动画预览物体");

            // 摆在当前 Scene 视图正前方,免得生成在原点、还得自己找
            SceneView view = SceneView.lastActiveSceneView;
            if (view != null && view.camera != null)
            {
                host.transform.position = view.camera.transform.position
                    + view.camera.transform.forward * 3f;
            }
        }

        // previewInEditor 是私有字段,只能走 SerializedObject 改
        SerializedObject so = new SerializedObject(intro);
        SerializedProperty preview = so.FindProperty("previewInEditor");
        if (preview != null)
        {
            preview.boolValue = true;
        }

        so.ApplyModifiedProperties();

        Selection.activeGameObject = host;
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
        }

        Debug.Log("[漓江回声] 预览物体已就位:" + host.name
            + "\n在 Inspector 里换 previewPattern、拖 previewTime 时间轴;"
            + "轨迹线在 Scene 视图里显示,转视角能看清深度。");
    }

    [MenuItem(Root + "选中场景里的预览物体", false, 1)]
    private static void SelectPreviewObject()
    {
        LijiangEchoPatternIntro intro = Object.FindFirstObjectByType<LijiangEchoPatternIntro>();
        if (intro == null)
        {
            EditorUtility.DisplayDialog("漓江回声",
                "当前场景里没有挂 LijiangEchoPatternIntro 的物体。\n"
                + "先点上面那条「在场景里创建预览物体」。", "知道了");
            return;
        }

        Selection.activeGameObject = intro.gameObject;
        if (SceneView.lastActiveSceneView != null)
        {
            SceneView.lastActiveSceneView.FrameSelected();
        }
    }

    // ————————————————————————————— Play 起来实时看 —————————————————————————————

    [MenuItem(Root + "播放:鱼纹(跃入 + 探头涟漪)", false, 20)]
    private static void PreviewFish() => Play(LijiangEchoPatternIntro.Pattern.Fish);

    [MenuItem(Root + "播放:蛇纹(嘶嘶 → 缠绕光圈)", false, 21)]
    private static void PreviewSnake() => Play(LijiangEchoPatternIntro.Pattern.Snake);

    [MenuItem(Root + "播放:蛙纹(荷叶间跳跃)", false, 22)]
    private static void PreviewFrog() => Play(LijiangEchoPatternIntro.Pattern.Frog);

    [MenuItem(Root + "播放:鸟纹(盘旋忽远忽近)", false, 23)]
    private static void PreviewBird() => Play(LijiangEchoPatternIntro.Pattern.Bird);

    private static void Play(LijiangEchoPatternIntro.Pattern which)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("漓江回声",
                "这几条要在 Play 模式下才能看。\n\n"
                + "不想 Play 的话,用上面的「在场景里创建预览物体」——\n"
                + "勾上 previewInEditor 后拖时间轴就能逐帧看轨迹,还能转视角看深度。", "知道了");
            return;
        }

        LijiangEchoPatternIntro intro = Object.FindFirstObjectByType<LijiangEchoPatternIntro>();
        if (intro == null)
        {
            GameObject holder = new GameObject("漓江回声_入场动画预览");
            intro = holder.AddComponent<LijiangEchoPatternIntro>();
        }

        // anchor 传 null = 在玩家面前起一个舞台(接上扫码后改成传二维码的空间位置)
        intro.Begin(which, null, () => Debug.Log($"[漓江回声] 入场动画播完:{which} → 这里接③打击环节"));
        Debug.Log($"[漓江回声] 正在预览入场动画:{which}");
    }
}
