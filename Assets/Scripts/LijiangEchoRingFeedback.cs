using UnityEngine;

/// <summary>
/// 中间圆环「反馈脚本」基类 —— 和音符一样走 Prefab:把这个(或它的子类)挂在圆环 Prefab 上,
/// 战斗控制器会在每帧 / 命中 / 漏接时自动回调它。【不用改控制器代码,想要别的反馈就换脚本】。
///
/// 关键约定(和"prefab 更安全"一致):
///   · 圆环 Prefab 上【没挂】任何反馈脚本时,控制器会自动补一个本基类实例 → 观感和旧版完全一致。
///   · 本基类的默认实现 = 旧版观感:OnBeat 按拍脉动(缩放 + 变色);命中不额外闪(保持迁移前后一致)。
///   · 想要更丰富的反馈(命中闪光/涟漪/换色…):新建一个脚本继承本类、重写 OnBeat/OnHit/OnMiss,
///     再用「纹样绑定总表」把带你脚本的圆环 Prefab 绑上去即可。
///
/// 控制器接线见 LijiangEchoGameController:创建圆环时 GetComponentInChildren&lt;本类&gt;() 找脚本、
/// 调 Init(渲染器, 基准缩放);UpdateRingVisual 调 OnBeat;HitCurrentNote 调 OnHit。
/// </summary>
public class LijiangEchoRingFeedback : MonoBehaviour
{
    protected SpriteRenderer ring;      // 圆环主渲染器(控制器传入,用于变色)
    protected Transform scaleTarget;    // 被缩放的对象(控制器传入:Prefab 用根、贴图兜底用贴图物体)
    protected Vector3 baseScale = Vector3.one;

    private bool initialized;

    /// <summary>
    /// 控制器在创建圆环后调用一次:renderer=变色的圆环渲染器;scaleRoot=要缩放的对象(通常是圆环根,
    /// 缩放它会连子物体一起放大,子物体在 Prefab 里的原始大小得以保留);baseScale=scaleRoot 的初始缩放。
    /// </summary>
    public virtual void Init(SpriteRenderer renderer, Transform scaleRoot, Vector3 ringBaseScale)
    {
        ring = renderer;
        scaleTarget = scaleRoot != null ? scaleRoot : (renderer != null ? renderer.transform : transform);
        baseScale = ringBaseScale;
        initialized = ring != null && scaleTarget != null;
    }

    /// <summary>
    /// 每帧回调。normalized = 距下一个音符判定点的接近度:0=还远,1=正好到判定点。
    /// 默认 = 旧版观感:越接近判定越收缩、越亮(金色)。
    /// </summary>
    public virtual void OnBeat(float normalized)
    {
        if (!initialized)
        {
            return;
        }

        float scale = Mathf.Lerp(1.12f, 0.92f, normalized);
        scaleTarget.localScale = baseScale * scale;
        ring.color = new Color(1f, 0.92f, 0.45f, Mathf.Lerp(0.42f, 1f, normalized));
    }

    /// <summary>
    /// 命中一个音符时回调。kind = 音符类型(0=单击/鱼,1=双击/鸟,2=长按/蛇,3=挥划/蛙,与 NoteKind 对应),
    /// good = 是否算命中。默认【不做额外反馈】,以保证 prefab 迁移前后观感一致 —— 想要命中闪光请重写本方法。
    /// </summary>
    public virtual void OnHit(int kind, bool good)
    {
    }

    /// <summary>漏接一个音符时回调。默认不做。</summary>
    public virtual void OnMiss()
    {
    }
}
