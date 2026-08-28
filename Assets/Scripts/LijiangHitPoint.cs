using UnityEngine;

/// <summary>
/// 「可摆放的打击点」。挂在带 SpriteRenderer + Collider 的物体上(通常做成 Prefab),
/// 代表关卡里一个可被击打的点。把 Prefab 拖进场景、摆好位置、指定纹样与类型,即可
/// 手摆关卡,不必靠代码按拍子生成。后续的打击判定可用它的 Collider 做命中检测。
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class LijiangHitPoint : MonoBehaviour
{
    public enum HitKind
    {
        Single,
        Double,
        Hold
    }

    [Tooltip("这个打击点显示的纹样(直接拖美术图)")]
    public Sprite sprite;

    [Tooltip("单击 / 双击 / 长按")]
    public HitKind kind = HitKind.Single;

    [Tooltip("命中判定时长(秒),长按更长")]
    public float duration = 1f;

    [Tooltip("排序层级,数值越大越靠前")]
    public int sortingOrder = 200;

    private void Awake()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    /// <summary>把纹样与层级应用到同物体的 SpriteRenderer(编辑器里所见即所得)。</summary>
    public void Apply()
    {
        SpriteRenderer renderer = GetComponent<SpriteRenderer>();
        if (renderer == null || sprite == null)
        {
            return;
        }

        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;
    }
}
