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

    private const string StageStartScenePath = "Assets/Scenes/Stages/Stage_Start.unity";
    private const string StageRootName = "开始舞台";
    private const float PositionTolerance = 0.0005f;
    private const float ScaleTolerance = 0.0005f;
    private const float AlphaTolerance = 0.002f;

    [MenuItem("漓江回声/场景化/2. 烘焙 Stage_Start 场景")]
    public static void BakeStageStart()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("无法烘焙", "请先退出 Play 模式。", "好");
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "烘焙 Stage_Start",
            $"将打开 {StageStartScenePath}，把开始界面的 20 个图层固化成场景物体并保存。\n\n" +
            "已存在的「开始舞台」节点会被整个替换。是否继续？",
            "继续",
            "取消");
        if (!confirmed)
        {
            return;
        }

        UnityEditor.SceneManagement.EditorSceneManager.OpenScene(StageStartScenePath, UnityEditor.SceneManagement.OpenSceneMode.Single);

        Transform existing = FindStageRootInOpenScene();
        if (existing != null)
        {
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }

        LayerRecordSet set = BuildLayoutAndRecord(out GameObject tempRoot);
        UnityEngine.Object.DestroyImmediate(tempRoot);

        GameObject stageRootObject = new GameObject(StageRootName);
        stageRootObject.transform.position = Vector3.zero;
        stageRootObject.transform.rotation = Quaternion.identity;
        stageRootObject.transform.localScale = Vector3.one;

        int meshMismatchCount = 0;
        foreach (LayerRecord record in set.layers)
        {
            Sprite assetSprite = LoadSpriteAsset(record.spriteAssetPath);
            if (assetSprite == null)
            {
                Debug.LogError($"[漓江回声场景化] 找不到精灵资产，已跳过：{record.name} ← {record.spriteAssetPath}");
                continue;
            }

            GameObject layerObject = new GameObject(record.name);
            layerObject.transform.SetParent(stageRootObject.transform, false);
            layerObject.transform.localPosition = record.localPosition;
            layerObject.transform.localRotation = Quaternion.identity;
            layerObject.transform.localScale = record.localScale;

            SpriteRenderer renderer = layerObject.AddComponent<SpriteRenderer>();
            renderer.sprite = assetSprite;
            renderer.sortingOrder = record.sortingOrder;
            renderer.color = new Color(1f, 1f, 1f, record.alpha);

            // 记录导入资产与运行时精灵的边界差异（设计文档风险 4.1）。
            // 位置与缩放一律以运行时数值为准，此处仅报告，供人判断是否需要处理。
            float assetWidth = assetSprite.bounds.size.x * Mathf.Abs(record.localScale.x);
            float assetHeight = assetSprite.bounds.size.y * Mathf.Abs(record.localScale.y);
            LijiangEchoSpriteLayer layer = layerObject.AddComponent<LijiangEchoSpriteLayer>();
            layer.sprite = assetSprite;
            layer.sortingOrder = record.sortingOrder;
            layer.alpha = record.alpha;
            // 拟合模式与目标尺寸按导入资产反推，保证「换图后自动拟合」用的是资产自身的尺度
            if (record.name == "开始界面底框" || assetWidth >= assetHeight)
            {
                layer.fitMode = LijiangEchoSpriteLayer.FitMode.Width;
                layer.fitSize = assetWidth;
            }
            else
            {
                layer.fitMode = LijiangEchoSpriteLayer.FitMode.Height;
                layer.fitSize = assetHeight;
            }

            // 先把 Transform 数值写回，抵消 AddComponent 触发 Apply 造成的重算
            layerObject.transform.localPosition = record.localPosition;
            layerObject.transform.localScale = record.localScale;

            if (!string.IsNullOrEmpty(record.motionKind))
            {
                LijiangEchoMotion motion = layerObject.AddComponent<LijiangEchoMotion>();
                motion.kind = (LijiangEchoStageKit.MotionKind)Enum.Parse(typeof(LijiangEchoStageKit.MotionKind), record.motionKind);
                motion.amplitude = record.motionAmplitude;
                motion.speed = record.motionSpeed;
                motion.phase = record.motionPhase;
            }

            if (Mathf.Abs(assetWidth - record.localScale.x * assetSprite.bounds.size.x) > 0.001f)
            {
                meshMismatchCount++;
            }
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(stageRootObject.scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(stageRootObject.scene);

        Debug.Log($"[漓江回声场景化] 已烘焙 {set.layers.Count} 个图层到 {StageStartScenePath}，网格差异计数 {meshMismatchCount}。");
        EditorUtility.DisplayDialog(
            "烘焙完成",
            $"已生成 {set.layers.Count} 个图层。\n\n接下来请执行「3. 校验 Stage_Start 与基线一致」。",
            "好");
    }

    /// <summary>
    /// 从贴图资产路径取出对应的 Sprite 子资产。项目美术已按 Sprite 单图模式导入
    /// （textureType: 8 / spriteMode: 1），故一张贴图对应一个 Sprite。
    /// </summary>
    private static Sprite LoadSpriteAsset(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath))
        {
            return null;
        }

        foreach (UnityEngine.Object item in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (item is Sprite sprite)
            {
                return sprite;
            }
        }

        return null;
    }

    [MenuItem("漓江回声/场景化/3. 校验 Stage_Start 与基线一致")]
    public static void VerifyAgainstBaseline()
    {
        string fullPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", BaselinePath));
        if (!File.Exists(fullPath))
        {
            EditorUtility.DisplayDialog("校验失败", "找不到基线文件，请先执行「1. 捕获 Stage_Start 基线」。", "好");
            return;
        }

        LayerRecordSet baseline = JsonUtility.FromJson<LayerRecordSet>(File.ReadAllText(fullPath));
        Transform stageRoot = FindStageRootInOpenScene();
        if (stageRoot == null)
        {
            EditorUtility.DisplayDialog(
                "校验失败",
                $"当前打开的场景里找不到名为「{StageRootName}」的根节点。\n请先打开 {StageStartScenePath} 并执行烘焙。",
                "好");
            return;
        }

        List<string> problems = new List<string>();
        foreach (LayerRecord expected in baseline.layers)
        {
            Transform actual = stageRoot.Find(expected.name);
            if (actual == null)
            {
                problems.Add($"缺少图层：{expected.name}");
                continue;
            }

            if (Vector3.Distance(actual.localPosition, expected.localPosition) > PositionTolerance)
            {
                problems.Add($"{expected.name} 位置不符：期望 {expected.localPosition:F4}，实际 {actual.localPosition:F4}");
            }

            if (Vector3.Distance(actual.localScale, expected.localScale) > ScaleTolerance)
            {
                problems.Add($"{expected.name} 缩放不符：期望 {expected.localScale:F4}，实际 {actual.localScale:F4}");
            }

            SpriteRenderer renderer = actual.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                problems.Add($"{expected.name} 没有 SpriteRenderer");
                continue;
            }

            if (renderer.sortingOrder != expected.sortingOrder)
            {
                problems.Add($"{expected.name} 层级不符：期望 {expected.sortingOrder}，实际 {renderer.sortingOrder}");
            }

            if (Mathf.Abs(renderer.color.a - expected.alpha) > AlphaTolerance)
            {
                problems.Add($"{expected.name} 透明度不符：期望 {expected.alpha:F3}，实际 {renderer.color.a:F3}");
            }

            if (renderer.sprite == null)
            {
                problems.Add($"{expected.name} 没有精灵");
            }

            LijiangEchoMotion motion = actual.GetComponent<LijiangEchoMotion>();
            bool expectMotion = !string.IsNullOrEmpty(expected.motionKind);
            if (expectMotion && motion == null)
            {
                problems.Add($"{expected.name} 缺少动效组件（期望 {expected.motionKind}）");
            }
            else if (!expectMotion && motion != null)
            {
                problems.Add($"{expected.name} 多出了不该有的动效组件");
            }
            else if (expectMotion && motion.kind.ToString() != expected.motionKind)
            {
                problems.Add($"{expected.name} 动效种类不符：期望 {expected.motionKind}，实际 {motion.kind}");
            }
        }

        if (problems.Count == 0)
        {
            Debug.Log($"[漓江回声场景化] 校验通过：{baseline.layers.Count} 个图层与基线完全一致。");
            EditorUtility.DisplayDialog("校验通过", $"{baseline.layers.Count} 个图层与基线完全一致。", "好");
            return;
        }

        foreach (string problem in problems)
        {
            Debug.LogError("[漓江回声场景化] " + problem);
        }

        EditorUtility.DisplayDialog("校验失败", $"发现 {problems.Count} 处不一致，详见 Console。", "好");
    }

    private static Transform FindStageRootInOpenScene()
    {
        foreach (GameObject root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == StageRootName)
            {
                return root.transform;
            }

            Transform found = root.transform.Find(StageRootName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }
}
