using UnityEditor;
using UnityEngine;

/// <summary>
/// 单独预览扫码功能的【入场动画】(见 docs/TODO-QR-PATTERN-SCAN.md 第②段)。
///
/// 扫码那部分还没做,但入场动画本身不依赖摄像头,所以可以先单独跑起来看效果:
/// Play 起来之后点这里的菜单,动画就在玩家面前播一遍。四种各点各的。
/// </summary>
public static class LijiangEchoPatternIntroPreview
{
    private const string Root = "漓江回声/5 调试/扫码入场动画预览/";

    [MenuItem(Root + "鱼纹(跃入 + 探头涟漪)", false, 0)]
    private static void PreviewFish() => Play(LijiangEchoPatternIntro.Pattern.Fish);

    [MenuItem(Root + "蛇纹(嘶嘶 → 缠绕光圈)", false, 1)]
    private static void PreviewSnake() => Play(LijiangEchoPatternIntro.Pattern.Snake);

    [MenuItem(Root + "蛙纹(荷叶间跳跃)", false, 2)]
    private static void PreviewFrog() => Play(LijiangEchoPatternIntro.Pattern.Frog);

    [MenuItem(Root + "鸟纹(盘旋忽远忽近)", false, 3)]
    private static void PreviewBird() => Play(LijiangEchoPatternIntro.Pattern.Bird);

    private static void Play(LijiangEchoPatternIntro.Pattern which)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("漓江回声",
                "入场动画要在 Play 模式下才能看。\n先按 Play,再点这个菜单。", "知道了");
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
