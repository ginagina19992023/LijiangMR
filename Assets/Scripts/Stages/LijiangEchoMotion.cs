using UnityEngine;

/// <summary>
/// 挂在需要浮动/呼吸动效的图层上，仅承载参数，不做运算。
/// 阶段 Controller 在 Start 时收集全部实例，交给 LijiangEchoStageKit.UpdateMotions 统一驱动，
/// 动效算法本身沿用原有实现，未作改动。
/// </summary>
public class LijiangEchoMotion : MonoBehaviour
{
    public LijiangEchoStageKit.MotionKind kind = LijiangEchoStageKit.MotionKind.FloatY;

    [Tooltip("振幅：位移类是米，缩放类是比例")]
    public float amplitude = 0.03f;

    [Tooltip("速度：每秒相位推进量")]
    public float speed = 1.5f;

    [Tooltip("相位偏移，用来让同类元素错开")]
    public float phase;
}
