using UnityEngine;

/// <summary>
/// 挂在纹样 Prefab 根上:在 Scene 视图画一个"中心十字点",标出该音符的对齐原点(飞入时被驱动的点)。
/// 只在编辑器 Scene 视图用 Gizmos 绘制,运行时(游戏画面)完全不显示,不影响任何逻辑。
/// 方便你在 Prefab 里拖 Visual 子物体时对准中心。
/// </summary>
public class LijiangEchoNoteCenterGizmo : MonoBehaviour
{
    [Tooltip("十字/点的大小(世界单位)")]
    public float size = 0.04f;

    [Tooltip("颜色")]
    public Color color = new Color(1f, 0.25f, 0.25f, 1f);

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = color;
        Vector3 p = transform.position;
        Gizmos.DrawSphere(p, size * 0.3f);
        Gizmos.DrawLine(p - transform.right * size, p + transform.right * size);
        Gizmos.DrawLine(p - transform.up * size, p + transform.up * size);
    }
#endif
}
