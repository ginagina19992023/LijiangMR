using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Stage_Start 场景化改造的一次性工具（见 docs/superpowers/plans/2026-08-28-stage-start-authoring.md）。
/// 三个命令按顺序使用：捕获基线 → 烘焙场景 → 比对校验。
/// 改造完成并验收通过后，本文件可以删除。
/// </summary>
public static class LijiangEchoStageBakeTool
{
    private const string BaselinePath = "ValidationCaptures/Baseline_Stage_Start.json";

    /// <summary>单个图层烘焙前后需要保持一致的全部数值。</summary>
    [Serializable]
    public class LayerRecord
    {
        public string name;
        public Vector3 localPosition;
        public Vector3 localScale;
        public int sortingOrder;
        public float alpha;
        public string spriteAssetPath;
        public string motionKind;
        public float motionAmplitude;
        public float motionSpeed;
        public float motionPhase;
    }

    [Serializable]
    public class LayerRecordSet
    {
        public List<LayerRecord> layers = new List<LayerRecord>();
    }

    [MenuItem("漓江回声/场景化/1. 捕获 Stage_Start 基线")]
    public static void CaptureBaseline()
    {
        LayerRecordSet set = BuildLayoutAndRecord(out GameObject tempRoot);
        UnityEngine.Object.DestroyImmediate(tempRoot);

        string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BaselinePath));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllText(fullPath, JsonUtility.ToJson(set, true));

        Debug.Log($"[漓江回声场景化] 已捕获 {set.layers.Count} 个图层的基线数值：{fullPath}");
        EditorUtility.DisplayDialog("捕获基线", $"已记录 {set.layers.Count} 个图层。\n\n{fullPath}", "好");
    }

    /// <summary>
    /// 在编辑模式下调用现有布局代码生成一份临时物体，并把每个物体的数值读成记录。
    /// 调用方负责销毁 tempRoot。
    /// </summary>
    private static LayerRecordSet BuildLayoutAndRecord(out GameObject tempRoot)
    {
        tempRoot = new GameObject("__烘焙临时根节点");
        tempRoot.transform.position = Vector3.zero;
        tempRoot.transform.rotation = Quaternion.identity;
        tempRoot.transform.localScale = Vector3.one;

        List<GameObject> spawned = new List<GameObject>();
        List<LijiangEchoStageKit.MotionItem> motions = new List<LijiangEchoStageKit.MotionItem>();
        StartStageController.BuildStartScreenLayout(tempRoot.transform, spawned, motions);

        LayerRecordSet set = new LayerRecordSet();
        foreach (GameObject item in spawned)
        {
            SpriteRenderer renderer = item.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                continue;
            }

            LayerRecord record = new LayerRecord
            {
                name = item.name,
                localPosition = item.transform.localPosition,
                localScale = item.transform.localScale,
                sortingOrder = renderer.sortingOrder,
                alpha = renderer.color.a,
                spriteAssetPath = ResolveSpriteAssetPath(renderer),
                motionKind = string.Empty
            };

            foreach (LijiangEchoStageKit.MotionItem motion in motions)
            {
                if (motion.Transform == item.transform)
                {
                    record.motionKind = motion.Kind.ToString();
                    record.motionAmplitude = motion.Amplitude;
                    record.motionSpeed = motion.Speed;
                    record.motionPhase = motion.Phase;
                    break;
                }
            }

            set.layers.Add(record);
        }

        return set;
    }

    /// <summary>
    /// 运行时精灵是 Sprite.Create 造的、无法序列化进场景。但它引用的 Texture2D 是
    /// Resources 里的真实资产，据此可以反查出已导入的 Sprite 资产路径。
    /// </summary>
    private static string ResolveSpriteAssetPath(SpriteRenderer renderer)
    {
        if (renderer.sprite == null || renderer.sprite.texture == null)
        {
            return string.Empty;
        }

        return AssetDatabase.GetAssetPath(renderer.sprite.texture);
    }
}
