using UnityEngine;

/// <summary>
/// 挂在阶段场景中每个美术图层上，描述「这是哪张精灵、拟合到多大、排在第几层、多透明」。
/// 把这些原本硬编码在代码里的参数搬到 Inspector，使美术资源可以直接在编辑器里替换：
/// 往 sprite 字段拖一张新图，缩放会按 fitMode 自动重新拟合。
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class LijiangEchoSpriteLayer : MonoBehaviour
{
    public enum FitMode
    {
        /// <summary>不自动缩放，完全以 Transform 上的数值为准。</summary>
        None,
        /// <summary>把精灵缩放到 fitSize 指定的世界宽度（对应旧代码的 AddLayer）。</summary>
        Width,
        /// <summary>把精灵缩放到 fitSize 指定的世界高度（对应旧代码的 AddIcon）。</summary>
        Height
    }

    [Tooltip("直接把美术资源拖到这里替换")]
    public Sprite sprite;

    [Tooltip("按宽度还是高度自动拟合缩放")]
    public FitMode fitMode = FitMode.Width;

    [Tooltip("拟合的目标尺寸，世界单位")]
    public float fitSize = 5.65f;

    [Tooltip("排序层级，数值越大越靠前")]
    public int sortingOrder;

    [Range(0f, 1f)]
    public float alpha = 1f;

    private void Awake()
    {
        Apply();
    }

    private void OnValidate()
    {
        Apply();
    }

    /// <summary>把本组件的参数应用到同物体的 SpriteRenderer 上。</summary>
    public void Apply()
    {
        SpriteRenderer renderer = GetComponent<SpriteRenderer>();
        if (renderer == null || sprite == null)
        {
            return;
        }

        renderer.sprite = sprite;
        renderer.sortingOrder = sortingOrder;
        renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));

        if (fitMode == FitMode.None)
        {
            return;
        }

        Vector3 spriteSize = sprite.bounds.size;
        float source = fitMode == FitMode.Width ? spriteSize.x : spriteSize.y;
        if (source <= 0f)
        {
            return;
        }

        float scale = fitSize / source;
        // 保留原有的水平镜像（部分素材靠负 X 缩放做左右翻转）
        float sign = transform.localScale.x < 0f ? -1f : 1f;
        transform.localScale = new Vector3(sign * scale, scale, scale);
    }
}
