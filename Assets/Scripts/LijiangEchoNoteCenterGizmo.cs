using UnityEngine;

/// <summary>
/// 挂在纹样 Prefab 根上:在 Scene 视图画一个"中心十字点",标出该音符的对齐原点(飞入时被驱动的点)。
/// 只在编辑器 Scene 视图用 Gizmos 绘制,运行时(游戏画面)完全不显示,不影响任何逻辑。
/// 方便你在 Prefab 里拖 Visual 子物体时对准中心。
/// </summary>
public class LijiangEchoNoteCenterGizmo : MonoBehaviour
{
    [Tooltip("十字/点的大小(世界单位)")]
    public float size = 0.09f;

    [Tooltip("颜色")]
    public Color color = new Color(1f, 0.2f, 0.2f, 1f);

#if UNITY_EDITOR
    // 只在"编辑时"画,Play 一律不画(避免运行时每个飞行音符都在 Scene 视图冒红叉)。
    // 需 Scene / Prefab 视图右上角「Gizmos」开关是打开的。
    private void OnDrawGizmos()
    {
        if (Application.isPlaying)
        {
            return;
        }

        Gizmos.color = color;
        Vector3 p = transform.position;
        Gizmos.DrawSphere(p, size * 0.35f);
        Gizmos.DrawLine(p - transform.right * size, p + transform.right * size);
        Gizmos.DrawLine(p - transform.up * size, p + transform.up * size);
    }
#endif
}
