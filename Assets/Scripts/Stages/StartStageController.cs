using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 开始阶段场景（Stage_Start）的控制器，对应旧 LijiangEchoGameController 里的
/// ShowStart/UpdateStart。内容拼装逻辑与旧版保持一致，只是改用
/// LijiangEchoStageKit 的公共方法，并把舞台内容放在本场景自己的根节点下。
/// </summary>
public class StartStageController : MonoBehaviour
{
    private Transform stageRoot;
    private readonly List<GameObject> spawnedObjects = new List<GameObject>();
    private readonly List<LijiangEchoStageKit.MotionItem> motionItems = new List<LijiangEchoStageKit.MotionItem>();
    private SpriteRenderer startButtonPanelRenderer;
    private SpriteRenderer startButtonRenderer;
    private bool confirmed;

    private IEnumerator Start()
    {
        while (LijiangEchoGameFlow.Instance == null)
        {
            yield return null;
        }

        stageRoot = LijiangEchoStageKit.PrepareStageRoot("漓江回声_开始舞台");
        BuildStartScreen();
    }

    private void Update()
    {
        if (stageRoot == null || confirmed)
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
}
