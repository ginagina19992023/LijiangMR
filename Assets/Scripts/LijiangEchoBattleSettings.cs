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
