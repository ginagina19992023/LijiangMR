using UnityEngine;

/// <summary>
/// 漓江回声 · 战斗可视化选项。
/// 这是一份"可在 Inspector 里勾选"的设置资源(ScriptableObject),存放在 Resources 下,
/// 运行时由 <see cref="LijiangEchoGameController"/> 读取 —— 审核组员【不用改代码】,
/// 在 Unity 里直接勾选即可。两种打开方式:
///   1) 菜单栏「漓江回声/战斗选项/…」直接勾选(带 ✓,点一下就切);
///   2) 菜单「漓江回声/战斗选项/选中设置资源」在 Project 里选中本资源,在 Inspector 勾选。
/// 资源不存在也不会报错:控制器会退回到脚本里的默认值。
/// </summary>
[CreateAssetMenu(fileName = "LijiangEchoBattleSettings", menuName = "漓江回声/战斗选项 (Battle Settings)", order = 0)]
public class LijiangEchoBattleSettings : ScriptableObject
{
    /// <summary>Resources 下的资源名(不带扩展名),Load/编辑器工具都用它。</summary>
    public const string ResourceName = "LijiangEchoBattleSettings";

    [Header("双击(鸟纹)飞入样式")]
    [Tooltip("勾上 = 镜像汇合:本体从一侧飞入,另生成一只对侧镜像分身,两只对称飞向圆心汇合(判定仍是一次命中)。\n不勾 = 单侧飞入(默认,和其它音符一致:从左或右一侧飞到圆心)。")]
    public bool doubleNoteMirrorConverge = false;

    [Header("音符按飞入方向自动镜像(让纹样朝向飞行方向)")]
    [Tooltip("总开关。勾上 = 从左侧飞入的音符水平镜像,使原本朝左的纹样朝向飞行方向(朝右);从右侧进入保持原朝向。\n下面按类型控制哪些纹样参与(默认只鱼纹/单击)。约定:纹样默认朝左,从右进入时即已朝向飞行方向。")]
    public bool autoMirrorNotesByDirection = true;

    [Tooltip("鱼纹(单击)参与自动镜像")]
    public bool mirrorStrike = true;

    [Tooltip("蛇纹(长按)参与自动镜像")]
    public bool mirrorHold = false;

    [Tooltip("蛙纹(滑动)参与自动镜像")]
    public bool mirrorSwipe = false;

    [Tooltip("鸟纹(双击)参与自动镜像(注意:若已开启上面的『双击=镜像汇合』,汇合本体固定从右进入,不受此项影响)")]
    public bool mirrorDouble = false;

    [Header("命中判定(灵敏度)")]
    [Tooltip("命中窗口(秒):按下时,与音符目标时间差在此范围内算命中;越大越宽松/灵敏(音符边缘碰到圆环就更容易算命中)。\n完美窗口 = 此值×0.4。默认 0.5(原来偏窄约 0.31,容易『太早』)。")]
    [Range(0.15f, 0.9f)]
    public float hitWindowSeconds = 0.5f;

    [Header("左右手判定(9.1 需求第 7 条)")]
    [Tooltip("勾上 = 从左侧飞入的音符只响应左手、右侧只响应右手,用错手不算命中(连击归零,但窗口内还能用对的手补救)。\n不勾 = 旧行为:任意一只手都能打所有音符,忽略方向。\nPC 调试:鼠标左键=右手,Shift+左键=左手(和描绘的左右手映射一致)。")]
    public bool handSideJudge = true;

    [Tooltip("双手音符(鸟纹/双击)『同时打击』的容差(秒):左右手先后按下的时间差在此范围内就算同时。\n越大越宽松。默认 0.35 已相当宽松;准确范围待队友确认(需求「开发前需确认」第 5 条)。")]
    [Range(0.05f, 0.8f)]
    public float twoHandSyncWindow = 0.35f;

    [Tooltip("勾上 = 双手音符必须左右手都到齐才算成功(需求第 7 条)。\n不勾 = 旧行为:双击音符一次命中即可。\n注意:PC 上没有手柄时,一次普通点击即视为双手到齐,方便无头显调试。")]
    public bool doubleNoteNeedsBothHands = true;

    private static LijiangEchoBattleSettings cached;

    /// <summary>运行时/编辑器读取:优先 Resources 里的资源;没有就用一份默认值实例(不落盘、不报错)。</summary>
    public static LijiangEchoBattleSettings Load()
    {
        if (cached != null)
        {
            return cached;
        }

        cached = Resources.Load<LijiangEchoBattleSettings>(ResourceName);
        if (cached == null)
        {
            cached = CreateInstance<LijiangEchoBattleSettings>(); // 缺资源时用默认值兜底
        }

        return cached;
    }
}
