using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 通用「已烘焙场景」动效驱动器。挂在烘焙场景(战斗背景 / 过场 / 结算等)的根节点上,
/// Start 时收集根节点下所有 <see cref="LijiangEchoMotion"/> 组件,每帧交给
/// <see cref="LijiangEchoStageKit.UpdateMotions"/> 统一驱动。
///
/// 设计意图:让通用烘焙工具(通用A/通用B)产出的静态场景无需各写一个 StageController
/// 即可"动起来"。动效算法本身沿用 StageKit 原实现,未作改动;每个图层的动效参数由它自己的
/// LijiangEchoMotion 组件承载,可在 Inspector 里逐个微调。
///
/// 用法:烘焙出场景后,执行菜单「漓江回声/场景化/为战斗背景补挂动效组件」自动补组件+挂本驱动器;
/// 或手动给图层加 LijiangEchoMotion、把本组件挂到根节点。
/// </summary>
[DisallowMultipleComponent]
public class BakedSceneMotionDriver : MonoBehaviour
{
    [Tooltip("动效收集根;留空则以本物体为根收集其所有子层。")]
    [SerializeField] private Transform motionRoot;

    private readonly List<LijiangEchoStageKit.MotionItem> motionItems = new List<LijiangEchoStageKit.MotionItem>();

    private void Start()
    {
        Collect();
    }

    /// <summary>重新收集动效层(运行时若动态增删图层可再调一次)。</summary>
    public void Collect()
    {
        Transform root = motionRoot != null ? motionRoot : transform;
        motionItems.Clear();
        foreach (LijiangEchoMotion motion in root.GetComponentsInChildren<LijiangEchoMotion>(true))
        {
            LijiangEchoStageKit.RegisterMotion(
                motionItems,
                motion.gameObject,
                motion.kind,
                motion.amplitude,
                motion.speed,
                motion.phase);
        }

        Debug.Log($"[漓江回声] 烘焙场景动效驱动:收集到 {motionItems.Count} 个动效层(根 {root.name})。");
    }

    private void Update()
    {
        if (motionItems.Count > 0)
        {
            LijiangEchoStageKit.UpdateMotions(motionItems);
        }
    }
}
