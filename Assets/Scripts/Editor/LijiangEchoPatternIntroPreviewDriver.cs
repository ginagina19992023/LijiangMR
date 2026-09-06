using UnityEditor;
using UnityEngine;

/// <summary>
/// 编辑模式下推动入场动画预览的"时钟"。
///
/// 为什么需要它:`[ExecuteAlways]` 的 Update 在编辑模式下【不是每帧都跑的】,
/// 只有编辑器决定重绘时才跑一拍。原来只在 Inspector 重绘时推一下,于是
/// 「点了关闭预览就再也打不开」—— 其实是重新打开后没人推,动画卡死在
/// previewTime = 0 那一帧,而那一帧所有生物本来就是全透明的,看着就像没生成。
///
/// 现在挂在 EditorApplication.update 上:只要场景里还有预览开着,就持续推,
/// 和 Inspector 有没有在显示、鼠标在不在 Scene 视图上都无关。
/// </summary>
[InitializeOnLoad]
public static class LijiangEchoPatternIntroPreviewDriver
{
    // 编辑器的 update 大约 100Hz,没必要跟着跑那么快;30 帧足够看动画了
    private const double TickInterval = 1.0 / 30.0;

    private static double nextTick;

    static LijiangEchoPatternIntroPreviewDriver()
    {
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
    }

    private static void Tick()
    {
        if (Application.isPlaying)
        {
            return;   // Play 模式有正常的游戏循环,不用管
        }

        double now = EditorApplication.timeSinceStartup;
        if (now < nextTick)
        {
            return;
        }

        nextTick = now + TickInterval;

        if (!LijiangEchoPatternIntro.AnyEditorPreviewActive())
        {
            return;   // 没有预览开着就彻底不干活,不白白刷新编辑器
        }

        EditorApplication.QueuePlayerLoopUpdate();   // 这一句才会真正调到 Update
        SceneView.RepaintAll();
    }
}
