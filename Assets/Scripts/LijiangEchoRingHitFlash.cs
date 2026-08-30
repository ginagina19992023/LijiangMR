using UnityEngine;

/// <summary>
/// 圆环反馈【示例子类】:在默认"按拍脉动"之上,命中一个音符时让圆环白闪一下 + 轻微放大,随后 ~0.18s 衰减回默认。
///
/// 这就是"加自定义反馈"的模板做法:
///   1) 继承 LijiangEchoRingFeedback;
///   2) 重写 OnHit(记一个闪光计时器)和 OnBeat(先 base.OnBeat 保留旧观感,再在其上叠加闪光);
///      不重写的方法(OnMiss 等)自动沿用默认。
///   3) 存盘编译 → 本类会自动出现在「纹样绑定总表 · ④中间圆环」的反馈脚本下拉里,一键挂到圆环 Prefab 即生效。
/// 全程不改控制器代码。想要涟漪/换色/漏接变红,照这个套路再写别的子类即可。
///
/// 注意:不挂本脚本时,圆环用基类默认(观感同旧版);本脚本只在被挂上后才多出命中闪光。
/// </summary>
public class LijiangEchoRingHitFlash : LijiangEchoRingFeedback
{
    [Tooltip("命中白闪的持续时间(秒)")]
    [SerializeField] private float flashDuration = 0.18f;

    [Tooltip("命中瞬间额外放大的比例(0.12 = 最多再大 12%)")]
    [SerializeField] private float flashScaleBoost = 0.12f;

    [Tooltip("命中瞬间向白色靠拢的强度(0~1)")]
    [SerializeField] private float flashWhiten = 0.75f;

    private float flashTimer;

    public override void OnHit(int kind, bool good)
    {
        if (good)
        {
            flashTimer = flashDuration; // 触发一次闪光;衰减在 OnBeat 里逐帧进行
        }
    }

    public override void OnBeat(float normalized)
    {
        base.OnBeat(normalized); // 先保留默认观感(脉动 + 金色渐亮)

        if (flashTimer <= 0f || scaleTarget == null)
        {
            return;
        }

        flashTimer -= Time.deltaTime;
        float f = flashDuration > 0f ? Mathf.Clamp01(flashTimer / flashDuration) : 0f; // 1→0

        // 在默认结果上叠加:轻微放大 + 向白色靠拢(f 越大越明显,随时间衰减回默认)
        scaleTarget.localScale *= 1f + flashScaleBoost * f;
        if (ring != null)
        {
            ring.color = Color.Lerp(ring.color, Color.white, flashWhiten * f);
        }
    }
}
