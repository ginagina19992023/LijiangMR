# Stage_Start 场景化改造 实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 把开始界面的美术内容从「运行时用代码生成」改成「预先摆在 `Stage_Start.unity` 场景里」，使美术资源可在 Unity 编辑器中直接查看、拖动和替换。

**Architecture:** 新增两个数据型组件承载「这是哪张图、多大、第几层、什么动效」，新增一个一次性烘焙工具把现有代码生成的结果固化成场景物体，最后把 `StartStageController` 里的布局代码删光、只留逻辑。全程以「烘焙前的运行时数值」为基准做自动比对，确保视觉零变化。

**Tech Stack:** Unity 6000.3.10f1 / C# / Meta XR SDK v201 / 目标平台 Quest 3 (Android)

**Spec:** `docs/superpowers/specs/2026-08-28-stage-start-authoring-design.md`

## Global Constraints

- 工作目录：`D:\GitHub\LijiangMR`，当前分支 `main`
- Unity 版本固定 `6000.3.10f1`，编辑器路径 `D:\Unity\6000.3.10f1\Editor\Unity.exe`
- **不得使用 Unity 批处理模式**（`-batchmode`）：本机 headless 授权不可用，会以 `No valid Unity Editor license found` 退出码 198 失败。所有编辑器操作由人在 GUI 中手动执行
- **本改造范围仅限 Stage_Start**：不得修改 `Stage_Select.unity`、`LijiangEchoMR_Main.unity`、`LijiangEchoGameController.cs` 的行为
- 视觉零变化是硬性要求：烘焙后每个图层的 `localPosition` / `localScale` / `sortingOrder` / `alpha` 必须与烘焙前的运行时数值一致（容差见 Task 4）
- 代码注释与日志一律用中文，与现有代码风格一致
- 本项目**没有接入单元测试框架**（有 `com.unity.test-framework` 包但无 `.asmdef`，asmdef 程序集无法引用默认程序集）。本计划的自动校验通过**编辑器菜单自检命令**实现
- 提交信息末尾附带：
  ```
  Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01C2u3cwEqwsuqHAnxgWWrRs
  ```

## 文件结构

| 文件 | 职责 |
|---|---|
| `Assets/Scripts/Stages/LijiangEchoSpriteLayer.cs` | **新建**。数据型组件：这个物体用哪张精灵、按宽/高拟合到多大、排序层级、透明度。负责把这些值应用到同物体的 `SpriteRenderer` |
| `Assets/Scripts/Stages/LijiangEchoMotion.cs` | **新建**。数据型组件：动效种类与三个参数。自身不做运算，由阶段 Controller 收集后交给 `LijiangEchoStageKit.UpdateMotions` |
| `Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs` | **新建**。三个编辑器菜单命令：捕获基线、烘焙场景、比对校验 |
| `Assets/Scripts/Bootstrap/LijiangEchoStageKit.cs` | **修改**。新增 `AnchorStageRoot(Transform)`；`AddLayer`/`AddIcon` 创建时顺带挂载并配置 `LijiangEchoSpriteLayer` |
| `Assets/Scripts/Stages/StartStageController.cs` | **修改**。布局代码先提为可从编辑器调用的静态方法（Task 1），最终删除（Task 5），只留逻辑 |
| `Assets/Scenes/Stages/Stage_Start.unity` | **修改**。由烘焙工具写入，不手工编辑 YAML |
| `ValidationCaptures/Baseline_Stage_Start.json` | **产物**。烘焙前捕获的基线数值，作为比对基准 |

---

### Task 1: 提取布局方法并捕获基线数值

把 `BuildStartScreen()` 里的布局代码提成一个 `public static` 方法，使编辑器工具能在**不进 Play 模式**的情况下调用它、拿到真实的运行时数值作为基线。这样后续烘焙的正确性有据可依，且避免了把 20 行布局参数手工抄进工具带来的转写错误。

**Files:**
- Modify: `Assets/Scripts/Stages/StartStageController.cs:62-107`（`BuildStartScreen` 方法体）
- Create: `Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs`

**Interfaces:**
- Produces: `StartStageController.BuildStartScreenLayout(Transform stageRoot, List<GameObject> spawned, List<LijiangEchoStageKit.MotionItem> motions)` — 纯布局，不碰音频、不碰按钮引用
- Produces: `LijiangEchoStageBakeTool.CaptureBaseline()` — 菜单 `漓江回声/场景化/1. 捕获 Stage_Start 基线`
- Produces: 序列化类型 `LijiangEchoStageBakeTool.LayerRecord` 与 `LijiangEchoStageBakeTool.LayerRecordSet`

- [ ] **Step 1: 把布局代码提为静态方法**

修改 `Assets/Scripts/Stages/StartStageController.cs`。把现有 `BuildStartScreen()` 拆成两半：音频与按钮引用留在实例方法里，纯布局搬进静态方法。

```csharp
    private void BuildStartScreen()
    {
        LijiangEchoStageKit.PlayStageLoop("ambience_water", 0.32f);
        LijiangEchoStageKit.PlaySfx("birds", 0.22f);

        BuildStartScreenLayout(stageRoot, spawnedObjects, motionItems);

        startButtonPanelRenderer = FindLayerRenderer("进入游戏主按钮");
        startButtonRenderer = FindLayerRenderer("开始按钮高光");
    }

    private SpriteRenderer FindLayerRenderer(string objectName)
    {
        foreach (GameObject item in spawnedObjects)
        {
            if (item != null && item.name == objectName)
            {
                return item.GetComponent<SpriteRenderer>();
            }
        }

        return null;
    }

    /// <summary>
    /// 开始界面的纯布局表。提为 public static 是为了让编辑器烘焙工具能在非 Play 模式下
    /// 调用它、取得与运行时完全一致的数值作为基线，避免手工转写这张表出错。
    /// 场景化改造完成后本方法连同调用一并删除（见实施计划 Task 5）。
    /// </summary>
    public static void BuildStartScreenLayout(
        Transform stageRoot,
        List<GameObject> spawned,
        List<LijiangEchoStageKit.MotionItem> motions)
    {
        LijiangEchoStageKit.AddLayer(stageRoot, spawned, "start/frame_16_9", "开始界面底框", Vector3.zero, LijiangEchoStageKit.MainCanvasWidth, -20, 0.04f);
        LijiangEchoStageKit.AddLayer(stageRoot, spawned, "start/back_mountain_1", "开始远山一", new Vector3(0f, -0.02f, 0.34f), LijiangEchoStageKit.WideStripWidth, -16, 0.9f);
        LijiangEchoStageKit.AddLayer(stageRoot, spawned, "start/back_mountain_2", "开始远山二", new Vector3(0f, -0.02f, 0.25f), LijiangEchoStageKit.WideStripWidth, -15, 0.82f);
        LijiangEchoStageKit.AddLayer(stageRoot, spawned, "start/back_mountain_3", "开始远山三", new Vector3(0f, -0.02f, 0.16f), LijiangEchoStageKit.WideStripWidth, -14, 0.78f);
        LijiangEchoStageKit.AddLayer(stageRoot, spawned, "start/back_building", "开始建筑", new Vector3(0f, -0.02f, 0.07f), LijiangEchoStageKit.WideStripWidth, -13, 0.88f);

        GameObject cloudOne = LijiangEchoStageKit.AddLayer(stageRoot, spawned, "start/back_cloud_1", "开始后云一", new Vector3(-0.02f, -0.02f, -0.04f), LijiangEchoStageKit.WideStripWidth, -10, 0.76f);
        GameObject cloudTwo = LijiangEchoStageKit.AddLayer(stageRoot, spawned, "start/back_cloud_2", "开始后云二", new Vector3(0.02f, -0.02f, -0.12f), LijiangEchoStageKit.WideStripWidth, -9, 0.62f);
        LijiangEchoStageKit.RegisterMotion(motions, cloudOne, LijiangEchoStageKit.MotionKind.FloatX, 0.045f, 0.55f, 0f);
        LijiangEchoStageKit.RegisterMotion(motions, cloudTwo, LijiangEchoStageKit.MotionKind.FloatX, 0.032f, 0.42f, 1.4f);

        LijiangEchoStageKit.AddLayer(stageRoot, spawned, "start/front_mountain_left", "开始前山左", new Vector3(0f, -0.02f, -0.25f), LijiangEchoStageKit.WideStripWidth, -6);
        LijiangEchoStageKit.AddLayer(stageRoot, spawned, "start/front_mountain_right", "开始前山右", new Vector3(0f, -0.02f, -0.32f), LijiangEchoStageKit.WideStripWidth, -5);

        GameObject frontCloudLeft = LijiangEchoStageKit.AddLayer(stageRoot, spawned, "start/front_cloud_left", "开始前云左", new Vector3(0f, -0.02f, -0.40f), LijiangEchoStageKit.WideStripWidth, -3, 0.9f);
        GameObject frontCloudRight = LijiangEchoStageKit.AddLayer(stageRoot, spawned, "start/front_cloud_right", "开始前云右", new Vector3(0f, -0.02f, -0.46f), LijiangEchoStageKit.WideStripWidth, -2, 0.9f);
        LijiangEchoStageKit.RegisterMotion(motions, frontCloudLeft, LijiangEchoStageKit.MotionKind.FloatX, 0.038f, 0.5f, 2f);
        LijiangEchoStageKit.RegisterMotion(motions, frontCloudRight, LijiangEchoStageKit.MotionKind.FloatX, 0.036f, 0.48f, 4f);

        GameObject buttonPanel = LijiangEchoStageKit.AddIcon(stageRoot, spawned, "start/start_ui", "进入游戏主按钮", new Vector3(0f, -0.38f, -0.53f), 0.52f, 5, 0.98f);
        GameObject button = LijiangEchoStageKit.AddIcon(stageRoot, spawned, "start/start_button", "开始按钮高光", new Vector3(0f, -0.48f, -0.55f), 0.095f, 6, 0.88f);
        LijiangEchoStageKit.RegisterMotion(motions, buttonPanel, LijiangEchoStageKit.MotionKind.Pulse, 0.01f, 2.1f, 0.7f);
        LijiangEchoStageKit.RegisterMotion(motions, button, LijiangEchoStageKit.MotionKind.Pulse, 0.022f, 2.4f, 0f);

        GameObject ball = LijiangEchoStageKit.AddIcon(stageRoot, spawned, "start/embroidered_ball", "绣球", new Vector3(0f, 0.23f, -0.66f), 0.72f, 7, 0.96f);
        GameObject birdBig = LijiangEchoStageKit.AddIcon(stageRoot, spawned, "start/bird_big", "大鸟", new Vector3(1.28f, 0.68f, -0.61f), 0.19f, 8, 0.92f);
        GameObject birdSmall = LijiangEchoStageKit.AddIcon(stageRoot, spawned, "start/bird_small", "小鸟", new Vector3(1.74f, 0.52f, -0.63f), 0.16f, 8, 0.78f);
        LijiangEchoStageKit.RegisterMotion(motions, ball, LijiangEchoStageKit.MotionKind.FloatY, 0.035f, 1.4f, 0f);
        LijiangEchoStageKit.RegisterMotion(motions, birdBig, LijiangEchoStageKit.MotionKind.FloatY, 0.025f, 2.1f, 1.2f);
        LijiangEchoStageKit.RegisterMotion(motions, birdSmall, LijiangEchoStageKit.MotionKind.FloatY, 0.022f, 1.8f, 2.8f);

        LijiangEchoStageKit.AddIcon(stageRoot, spawned, "start/progress_bar", "开始进度底条", new Vector3(0f, -0.74f, -0.2f), 0.12f, 9, 0.82f);
        GameObject pattern = LijiangEchoStageKit.AddIcon(stageRoot, spawned, "start/progress_pattern", "开始进度纹样", new Vector3(-0.72f, -0.74f, -0.21f), 0.08f, 10, 0.95f);
        LijiangEchoStageKit.RegisterMotion(motions, pattern, LijiangEchoStageKit.MotionKind.FloatX, 0.34f, 0.72f, 1.7f);

        LijiangEchoStageKit.AddLayer(stageRoot, spawned, "start/start_border", "开始外框纹样", new Vector3(0f, -0.02f, -0.23f), LijiangEchoStageKit.WideStripWidth, 24, 0.95f);

        LijiangEchoStageKit.AddIcon(stageRoot, spawned, "ui/settings", "左上设置入口", new Vector3(-2.42f, 1.05f, -0.28f), 0.24f, 30, 0.88f);
    }
```

同时删除文件末尾原有的三个私有辅助方法 `AddLayer` / `AddIcon` / `RegisterMotion`（第 109-122 行），它们已无调用者。

- [ ] **Step 2: 编译验证 —— 确认行为未变**

在 Unity 中等待编译完成，Console 不得有报错。打开 `Assets/Scenes/Bootstrap.unity` 进 Play 模式，确认开始界面显示正常、手柄射线悬停按钮有高亮、扣扳机能进选关。退出 Play。

这一步是纯重构，**画面必须和改动前完全一样**。若有任何差异，说明提取过程出错，回退重做。

- [ ] **Step 3: 写基线捕获工具**

新建 `Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs`：

```csharp
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
```

- [ ] **Step 4: 运行捕获，确认基线成立**

在 Unity 菜单点 **漓江回声 → 场景化 → 1. 捕获 Stage_Start 基线**。

预期：弹窗显示 **20 个图层**，`ValidationCaptures/Baseline_Stage_Start.json` 生成。

打开该 JSON 人工抽查三条：
- `开始界面底框` 的 `sortingOrder` 应为 `-20`，`alpha` 应为 `0.04`
- `绣球` 的 `spriteAssetPath` 应为 `Assets/Resources/LijiangEchoArt/start/embroidered_ball.png`
- `开始后云一` 的 `motionKind` 应为 `FloatX`，`motionSpeed` 应为 `0.55`

任何一条对不上就停下排查，不要继续后面的任务。

- [ ] **Step 5: 提交**

```bash
git add Assets/Scripts/Stages/StartStageController.cs Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs ValidationCaptures/Baseline_Stage_Start.json
git commit -m "$(cat <<'EOF'
refactor: 提取开始界面布局为静态方法并捕获场景化基线

为 Stage_Start 场景化改造做准备。把 BuildStartScreen 里的纯布局部分提为
public static BuildStartScreenLayout，使编辑器工具能在非 Play 模式下调用它、
取得与运行时完全一致的数值，避免手工转写这张 20 行的布局表出错。

新增 LijiangEchoStageBakeTool 的第一个命令：捕获基线，把每个图层的位置、
缩放、层级、透明度、精灵资产路径与动效参数记录成 JSON，作为后续烘焙的
正确性判据。

本次为纯重构，画面无变化。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01C2u3cwEqwsuqHAnxgWWrRs
EOF
)"
```

---

### Task 2: 新增两个数据型组件

让每个图层物体自己携带「用哪张图、怎么拟合、第几层、多透明」和「什么动效」。同时让 `LijiangEchoStageKit.AddLayer` / `AddIcon` 在创建物体时就挂上并配好 `LijiangEchoSpriteLayer`——这样烘焙工具只需读组件，不必猜哪些是按宽拟合、哪些是按高拟合。

**设计说明（对设计文档 3.2 的一处简化）：** 设计文档写的是 `[ExecuteAlways]`。实际不需要——精灵一旦赋给 `SpriteRenderer`，Scene 视图本来就能看见。组件只需在 `OnValidate()`（Inspector 改动时）和 `Awake()`（运行时）各应用一次即可，避免每帧写入场景数据。这消除了设计文档风险 4.3 的绝大部分。

**Files:**
- Create: `Assets/Scripts/Stages/LijiangEchoSpriteLayer.cs`
- Create: `Assets/Scripts/Stages/LijiangEchoMotion.cs`
- Modify: `Assets/Scripts/Bootstrap/LijiangEchoStageKit.cs`（`AddLayer` 约第 489 行、`AddIcon` 约第 496 行）

**Interfaces:**
- Consumes: `LijiangEchoStageKit.MotionKind`（已存在的枚举）
- Produces: `LijiangEchoSpriteLayer`，公开字段 `sprite` / `fitMode` / `fitSize` / `sortingOrder` / `alpha`，公开方法 `Apply()`
- Produces: `LijiangEchoSpriteLayer.FitMode` 枚举，成员 `None` / `Width` / `Height`
- Produces: `LijiangEchoMotion`，公开字段 `kind` / `amplitude` / `speed` / `phase`

- [ ] **Step 1: 写 LijiangEchoSpriteLayer**

新建 `Assets/Scripts/Stages/LijiangEchoSpriteLayer.cs`：

```csharp
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
```

- [ ] **Step 2: 写 LijiangEchoMotion**

新建 `Assets/Scripts/Stages/LijiangEchoMotion.cs`：

```csharp
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
```

- [ ] **Step 3: 让 StageKit 创建物体时挂上组件**

修改 `Assets/Scripts/Bootstrap/LijiangEchoStageKit.cs`。把 `AddLayer` 与 `AddIcon` 两个方法替换为下面版本（其余方法不动）：

```csharp
    public static GameObject AddLayer(Transform stageRoot, List<GameObject> spawned, string resourcePath, string objectName, Vector3 localPosition, float targetWidth, int order, float alpha = 1f, Transform parent = null)
    {
        GameObject spriteObject = AddSprite(stageRoot, spawned, resourcePath, objectName, localPosition, Vector3.one, order, alpha, false, parent);
        SpriteRenderer renderer = spriteObject.GetComponent<SpriteRenderer>();
        FitRendererWidth(renderer, targetWidth);
        AttachSpriteLayer(spriteObject, renderer, LijiangEchoSpriteLayer.FitMode.Width, targetWidth, order, alpha);
        return spriteObject;
    }

    public static GameObject AddIcon(Transform stageRoot, List<GameObject> spawned, string resourcePath, string objectName, Vector3 visibleCenter, float targetHeight, int order, float alpha = 1f)
    {
        GameObject spriteObject = AddSprite(stageRoot, spawned, resourcePath, objectName, visibleCenter, Vector3.one, order, alpha, true);
        SpriteRenderer renderer = spriteObject.GetComponent<SpriteRenderer>();
        FitRendererHeight(renderer, targetHeight);
        PlaceVisibleCenter(spriteObject.transform, renderer, visibleCenter);
        AttachSpriteLayer(spriteObject, renderer, LijiangEchoSpriteLayer.FitMode.Height, targetHeight, order, alpha);
        return spriteObject;
    }

    /// <summary>
    /// 给运行时生成的图层补上 LijiangEchoSpriteLayer，记录它「按什么拟合、拟合到多大」。
    /// 场景化烘焙工具据此判断每个物体该用哪种拟合模式，不必再去猜。
    /// 注意：先赋值字段再挂组件会触发 Awake 里的 Apply，故这里直接用已算好的值填充，
    /// Apply 的结果与上面几行的计算等价，不会改变外观。
    /// </summary>
    private static void AttachSpriteLayer(
        GameObject spriteObject,
        SpriteRenderer renderer,
        LijiangEchoSpriteLayer.FitMode fitMode,
        float fitSize,
        int order,
        float alpha)
    {
        LijiangEchoSpriteLayer layer = spriteObject.AddComponent<LijiangEchoSpriteLayer>();
        layer.sprite = renderer.sprite;
        layer.fitMode = fitMode;
        layer.fitSize = fitSize;
        layer.sortingOrder = order;
        layer.alpha = alpha;
    }
```

- [ ] **Step 4: 重新捕获基线，确认数值一字未变**

先把现有基线改名留档：

```bash
cd /d/GitHub/LijiangMR
cp ValidationCaptures/Baseline_Stage_Start.json ValidationCaptures/Baseline_Stage_Start.before-components.json
```

回 Unity 等编译完成，再次点 **漓江回声 → 场景化 → 1. 捕获 Stage_Start 基线**，然后比对：

```bash
diff ValidationCaptures/Baseline_Stage_Start.before-components.json ValidationCaptures/Baseline_Stage_Start.json
```

预期：**没有任何输出**（两个文件完全相同）。

若有差异，说明 `AttachSpriteLayer` 里的 `Apply()` 改变了缩放——多半是 `FitMode.Height` 的物体被按新逻辑重算、丢掉了 `PlaceVisibleCenter` 的位置补偿。停下排查，不要继续。

比对通过后删除留档文件：

```bash
rm ValidationCaptures/Baseline_Stage_Start.before-components.json
```

- [ ] **Step 5: 确认 Stage_Select 未受影响**

打开 `Assets/Scenes/Bootstrap.unity` 进 Play，走到选关界面，确认三张关卡卡片显示正常、左右切换有反应。退出 Play。

（`AddLayer`/`AddIcon` 是共用方法，Stage_Select 也在调，必须确认没被改坏。）

- [ ] **Step 6: 提交**

```bash
git add Assets/Scripts/Stages/LijiangEchoSpriteLayer.cs Assets/Scripts/Stages/LijiangEchoMotion.cs Assets/Scripts/Bootstrap/LijiangEchoStageKit.cs
git commit -m "$(cat <<'EOF'
feat: 新增图层与动效数据组件，供场景化烘焙使用

LijiangEchoSpriteLayer 把「哪张精灵、按宽还是按高拟合到多大、第几层、多透明」
从代码搬到 Inspector，往 sprite 字段拖新图即可自动重新拟合缩放。
LijiangEchoMotion 承载动效四参数，算法仍复用 StageKit.UpdateMotions。

StageKit 的 AddLayer/AddIcon 在创建物体时顺带挂载并配好 SpriteLayer，使烘焙
工具能直接读出拟合模式，无需推断。基线 JSON 逐字节比对确认外观无变化，
Stage_Select 已回归验证。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01C2u3cwEqwsuqHAnxgWWrRs
EOF
)"
```

---

### Task 3: StageKit 支持锚定场景中已有的舞台根节点

现有 `PrepareStageRoot(string)` 每次都 `new GameObject(...)`。场景化之后根节点是预先摆在场景里的，需要一个「只做定位、不做创建」的入口。

**Files:**
- Modify: `Assets/Scripts/Bootstrap/LijiangEchoStageKit.cs:170-189`（`PrepareStageRoot` 方法）

**Interfaces:**
- Produces: `LijiangEchoStageKit.AnchorStageRoot(Transform stageRoot)` — 把给定节点摆到相机前方并设好朝向与缩放
- `PrepareStageRoot(string)` 签名与行为保持不变（Stage_Select 仍在用）

- [ ] **Step 1: 拆分方法**

把 `Assets/Scripts/Bootstrap/LijiangEchoStageKit.cs` 中的 `PrepareStageRoot` 替换为：

```csharp
    /// <summary>
    /// 在当前激活场景里创建一个新的舞台根节点，锚定在相机前方（不随转头移动）。
    /// 供尚未场景化的阶段使用；已场景化的阶段请改用 AnchorStageRoot。
    /// </summary>
    public static Transform PrepareStageRoot(string rootName)
    {
        GameObject rootObject = new GameObject(rootName);
        AnchorStageRoot(rootObject.transform);
        return rootObject.transform;
    }

    /// <summary>
    /// 把一个已存在的舞台根节点摆到相机前方。场景化后的阶段，其根节点预先放在场景里
    /// （这样美术内容作为子物体在 Scene 视图中可见可拖），运行时只需要重新定位。
    /// </summary>
    public static void AnchorStageRoot(Transform stageRoot)
    {
        if (stageRoot == null)
        {
            return;
        }

        Camera camera = EnsureCamera();

        Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();
        stageRoot.SetParent(null, true);
        stageRoot.position = camera.transform.position + forward * StageDistance + Vector3.down * 0.02f;
        stageRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);
        stageRoot.localScale = Vector3.one * StageWorldScale;

        CacheControllerAnchors();
    }
```

- [ ] **Step 2: 回归验证两个已有阶段**

Unity 编译完成后，打开 `Assets/Scenes/Bootstrap.unity` 进 Play：

- 开始界面位置、大小与之前一致（应出现在正前方约 2.35 米、缩放 0.78）
- 点开始进入选关，选关界面同样正常

退出 Play。这一步纯属拆分，行为不应有任何变化。

- [ ] **Step 3: 提交**

```bash
git add Assets/Scripts/Bootstrap/LijiangEchoStageKit.cs
git commit -m "$(cat <<'EOF'
refactor: StageKit 拆出 AnchorStageRoot 以支持场景中已有的舞台根节点

场景化后的阶段，其舞台根节点预先摆在场景里（美术内容作为子物体才能在
Scene 视图中可见可拖），运行时只需重新定位而非创建。PrepareStageRoot
改为调用 AnchorStageRoot，对外签名与行为不变，Stage_Select 不受影响。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01C2u3cwEqwsuqHAnxgWWrRs
EOF
)"
```

---

### Task 4: 烘焙工具 —— 把布局固化进场景

这是本计划的核心，也是设计文档风险 4.1（FullRect vs Tight 网格差异）的验证点。策略：**位置与缩放一律采用运行时算出的数值**（写死进 Transform），精灵字段指向**已导入的 Sprite 资产**。两者若有差异，以运行时数值为准并在 Console 里报告差异幅度。

**Files:**
- Modify: `Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs`（新增两个菜单命令）
- Modify: `Assets/Scenes/Stages/Stage_Start.unity`（由工具写入）

**Interfaces:**
- Consumes: `LijiangEchoStageBakeTool.LayerRecord` / `LayerRecordSet`（Task 1）
- Consumes: `LijiangEchoSpriteLayer` / `LijiangEchoMotion`（Task 2）
- Produces: 菜单 `漓江回声/场景化/2. 烘焙 Stage_Start 场景`
- Produces: 菜单 `漓江回声/场景化/3. 校验 Stage_Start 与基线一致`
- Produces: 场景中名为 `开始舞台` 的根节点，其下为 20 个图层子物体

- [ ] **Step 1: 先写校验命令（此时必然失败）**

在 `LijiangEchoStageBakeTool` 类中追加：

```csharp
    private const string StageStartScenePath = "Assets/Scenes/Stages/Stage_Start.unity";
    private const string StageRootName = "开始舞台";
    private const float PositionTolerance = 0.0005f;
    private const float ScaleTolerance = 0.0005f;
    private const float AlphaTolerance = 0.002f;

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
```

- [ ] **Step 2: 运行校验，确认它确实失败**

打开 `Assets/Scenes/Stages/Stage_Start.unity`，点 **漓江回声 → 场景化 → 3. 校验 Stage_Start 与基线一致**。

预期：弹窗提示 **找不到名为「开始舞台」的根节点**。

这一步是必须的——先看到红灯，才能确认后面的绿灯是真的。

- [ ] **Step 3: 写烘焙命令**

在 `LijiangEchoStageBakeTool` 类中追加：

```csharp
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
```

- [ ] **Step 4: 执行烘焙**

Unity 编译完成后，点 **漓江回声 → 场景化 → 2. 烘焙 Stage_Start 场景**，确认弹窗后等待完成。

预期：提示已生成 **20 个图层**；Hierarchy 里出现 `开始舞台` 节点，展开可见 20 个子物体；Scene 视图里能看到开始界面的画面。

- [ ] **Step 5: 运行校验，确认转绿**

点 **漓江回声 → 场景化 → 3. 校验 Stage_Start 与基线一致**。

预期：弹窗 **「20 个图层与基线完全一致」**。

若 Console 报出不一致，按提示逐条排查。最可能的是 `FitMode.Height` 类物体的缩放差异（设计文档风险 4.1 兑现）——此时应确认 Step 3 中「先把 Transform 数值写回」那两行确实生效。

- [ ] **Step 6: 提交**

```bash
git add Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs Assets/Scenes/Stages/Stage_Start.unity
git commit -m "$(cat <<'EOF'
feat: 烘焙 Stage_Start 布局进场景，美术内容改为可视化编辑

新增烘焙与校验两个编辑器命令。烘焙把开始界面的 20 个图层固化成场景物体：
位置与缩放采用运行时算出的数值以保证视觉零变化，精灵字段则指向已导入的
Sprite 资产（运行时 Sprite.Create 的产物无法序列化进场景）。

校验命令逐个比对位置、缩放、层级、透明度与动效种类，容差 0.0005，通过后
方可继续。至此 Scene 视图中可直接看到并拖动开始界面的每一个图层。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01C2u3cwEqwsuqHAnxgWWrRs
EOF
)"
```

---

### Task 5: StartStageController 瘦身 —— 删掉全部布局代码

场景里已有内容，Controller 不再需要生成任何东西，只保留逻辑。

**Files:**
- Modify: `Assets/Scripts/Stages/StartStageController.cs`（整体重写）
- Modify: `Assets/Scenes/Stages/Stage_Start.unity`（在 Inspector 里拖三个引用）

**Interfaces:**
- Consumes: `LijiangEchoStageKit.AnchorStageRoot(Transform)`（Task 3）
- Consumes: `LijiangEchoMotion`（Task 2）
- Consumes: `LijiangEchoGameFlow.Instance.GoToStage(string)`（已存在）

- [ ] **Step 1: 重写 Controller**

把 `Assets/Scripts/Stages/StartStageController.cs` 整个替换为：

```csharp
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 开始阶段场景（Stage_Start）的控制器。
/// 画面内容已固化在场景里（见 docs/superpowers/specs/2026-08-28-stage-start-authoring-design.md），
/// 本脚本只负责逻辑：把舞台摆到玩家面前、驱动动效、处理开始按钮、跳转到选关。
/// </summary>
public class StartStageController : MonoBehaviour
{
    [Header("场景引用")]
    [Tooltip("承载全部美术图层的根节点，运行时会被摆到相机前方")]
    [SerializeField] private Transform stageRoot;

    [Tooltip("开始按钮的面板底图，用于悬停高亮")]
    [SerializeField] private SpriteRenderer startButtonPanelRenderer;

    [Tooltip("开始按钮的高光，用于悬停高亮")]
    [SerializeField] private SpriteRenderer startButtonRenderer;

    private readonly List<LijiangEchoStageKit.MotionItem> motionItems = new List<LijiangEchoStageKit.MotionItem>();
    private bool ready;
    private bool confirmed;

    private IEnumerator Start()
    {
        if (stageRoot == null)
        {
            Debug.LogError("[漓江回声] Stage_Start 的舞台根节点未在 Inspector 中指定");
            yield break;
        }

        while (LijiangEchoGameFlow.Instance == null)
        {
            yield return null;
        }

        LijiangEchoStageKit.AnchorStageRoot(stageRoot);
        CollectMotions();

        LijiangEchoStageKit.PlayStageLoop("ambience_water", 0.32f);
        LijiangEchoStageKit.PlaySfx("birds", 0.22f);

        ready = true;
    }

    /// <summary>把场景里挂了动效组件的图层收集成 StageKit 认识的形式，动效算法不变。</summary>
    private void CollectMotions()
    {
        motionItems.Clear();
        foreach (LijiangEchoMotion motion in stageRoot.GetComponentsInChildren<LijiangEchoMotion>(true))
        {
            LijiangEchoStageKit.RegisterMotion(
                motionItems,
                motion.gameObject,
                motion.kind,
                motion.amplitude,
                motion.speed,
                motion.phase);
        }
    }

    private void Update()
    {
        if (!ready || confirmed)
        {
            return;
        }

        LijiangEchoStageKit.UpdateControllerInput(stageRoot);
        LijiangEchoStageKit.UpdateMotions(motionItems);

        Rect startButtonBounds = new Rect(-0.72f, -0.72f, 1.44f, 0.58f);
        bool hovered = LijiangEchoStageKit.TryGetControllerHover(stageRoot, startButtonBounds, out bool pointerPressed);
        if (startButtonPanelRenderer != null)
        {
            startButtonPanelRenderer.color = hovered ? Color.white : new Color(1f, 1f, 1f, 0.92f);
        }

        if (startButtonRenderer != null)
        {
            startButtonRenderer.color = hovered
                ? new Color(1f, 0.9f, 0.42f, 1f)
                : new Color(1f, 1f, 1f, 0.88f);
        }

        if (pointerPressed || LijiangEchoStageKit.NonPointerConfirmPressed())
        {
            confirmed = true;
            LijiangEchoStageKit.PlaySfx("button", 0.62f);
            LijiangEchoGameFlow.Instance.GoToStage("Stage_Select");
        }
    }
}
```

**注意：** `BuildStartScreenLayout` 一并删除。这会导致 `LijiangEchoStageBakeTool` 编译报错——这是预期的，下一步处理。

- [ ] **Step 2: 让烘焙工具停止依赖已删除的方法**

烘焙已完成、基线已固化，`BuildLayoutAndRecord` 不再需要。修改 `Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs`：

- 删除 `BuildLayoutAndRecord` 方法与 `ResolveSpriteAssetPath` 方法
- 删除 `CaptureBaseline` 命令（基线已存档在 git 中，不需要重新生成）
- 删除 `BakeStageStart` 命令（一次性任务已完成）
- **保留** `VerifyAgainstBaseline` 命令与 `LayerRecord` / `LayerRecordSet` / `FindStageRootInOpenScene`，它们今后仍可用于回归检查

在类的文档注释中补一行说明：

```csharp
/// 烘焙与基线捕获命令已在改造完成后移除，仅保留校验命令供日后回归使用。
```

- [ ] **Step 3: 在 Inspector 里接好三个引用**

打开 `Assets/Scenes/Stages/Stage_Start.unity`，选中 `漓江回声_开始阶段` 物体，在 Inspector 的 StartStageController 上拖入：

- **Stage Root** ← Hierarchy 里的 `开始舞台` 节点
- **Start Button Panel Renderer** ← `开始舞台/进入游戏主按钮`
- **Start Button Renderer** ← `开始舞台/开始按钮高光`

保存场景（Ctrl+S）。

- [ ] **Step 4: 校验仍然通过**

点 **漓江回声 → 场景化 → 3. 校验 Stage_Start 与基线一致**，预期仍为 **20 个图层与基线完全一致**。

- [ ] **Step 5: 完整流程验证**

打开 `Assets/Scenes/Bootstrap.unity` 进 Play：

- 开始界面正常显示，与改造前观感一致
- 云、鸟、绣球、按钮的动效都在动
- 手柄射线悬停开始按钮时面板与高光变色
- 扣扳机进入选关界面

退出 Play。

- [ ] **Step 6: 提交**

```bash
git add Assets/Scripts/Stages/StartStageController.cs Assets/Scripts/Editor/LijiangEchoStageBakeTool.cs Assets/Scenes/Stages/Stage_Start.unity
git commit -m "$(cat <<'EOF'
refactor: StartStageController 移除全部布局代码，只保留逻辑

画面内容已固化在 Stage_Start.unity 中，Controller 不再生成任何物体：
舞台根节点、按钮的两个 SpriteRenderer 改为 Inspector 引用，动效改为
从场景中收集 LijiangEchoMotion 组件。文件从 123 行降至约 100 行，
且不再含任何美术参数。

烘焙与基线捕获命令随一次性任务结束一并移除，保留校验命令供回归使用。

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01C2u3cwEqwsuqHAnxgWWrRs
EOF
)"
```

---

### Task 6: 人工工作流验收

这是本次改造**真正的验收标准**——前面所有自动校验只能证明「没改坏」，唯有本任务能证明「达成了目的」。由使用者本人在 Unity 中执行。

**Files:**
- 无代码改动（除非验收暴露问题）

- [ ] **Step 1: 可见性验收**

打开 `Assets/Scenes/Stages/Stage_Start.unity`。

预期：**不进 Play 模式**，Scene 视图中就能看到完整的开始界面——远山、建筑、云、绣球、鸟、开始按钮、外框纹样。Hierarchy 中 `开始舞台` 下 20 个图层名称清晰可辨。

- [ ] **Step 2: 换图验收**

选中 `开始舞台/开始远山一`，在 Inspector 的 LijiangEchoSpriteLayer 上，把 **Sprite** 字段换成另一张图（例如 `back_mountain_3`）。

预期：Scene 视图**立即**反映变化，且缩放自动按 `fitSize` 重新拟合，不需要手工调整。

验收后按 Ctrl+Z 撤销。

- [ ] **Step 3: 移动验收**

选中 `开始舞台/绣球`，在 Scene 视图中直接拖动它到一个明显不同的位置。

打开 `Bootstrap.unity` 进 Play，确认绣球出现在你刚才拖到的位置。

退出 Play，按 Ctrl+Z 撤销（或手工改回原位后重新运行校验命令确认一致）。

- [ ] **Step 4: 判定试点结论**

三步都符合预期 → 试点通过，可按设计文档第 6 节的顺序推广到其余阶段（卡片 → 结算 → 描绘 → 过场 → 战斗）。

任何一步不符合预期 → 记录具体现象，评估是修补还是回退。回退命令：

```bash
git log --oneline -6
git revert --no-commit <本计划的全部提交>
```

- [ ] **Step 5: 记录结论并提交**

在设计文档 `docs/superpowers/specs/2026-08-28-stage-start-authoring-design.md` 顶部把「状态：待评审」改为「状态：试点已完成，结论见文末」，并在文末追加一节记录三步验收的实际结果与后续决定。

```bash
git add docs/superpowers/specs/2026-08-28-stage-start-authoring-design.md
git commit -m "$(cat <<'EOF'
docs: 记录 Stage_Start 场景化试点验收结论

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01C2u3cwEqwsuqHAnxgWWrRs
EOF
)"
```
