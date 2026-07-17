using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class LijiangEchoMrValidation
{
    private const string SessionKey = "LijiangEcho.MrValidationRunning";
    private const string MainScenePath = "Assets/Scenes/LijiangEchoMR_Main.unity";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private static LijiangEchoGameController controller;
    private static int phase;
    private static int waitFrames;

    static LijiangEchoMrValidation()
    {
        if (SessionState.GetBool(SessionKey, false))
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }
    }

    public static void Run()
    {
        SessionState.SetBool(SessionKey, true);
        phase = 0;
        waitFrames = 0;
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        EditorApplication.update -= Tick;
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    public static void OpenMainScene()
    {
        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
    }

    public static void BuildAndroid()
    {
        string outputDirectory = Path.GetFullPath(Path.Combine(Application.dataPath, "../Builds"));
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, "LijiangEchoMR-Quest3.apk");
        if (File.Exists(outputPath))
        {
            File.Delete(outputPath);
        }

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { MainScenePath },
            locationPathName = outputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        bool success = report.summary.result == BuildResult.Succeeded;
        if (success)
        {
            Debug.Log($"[漓江回声构建] Android APK 构建成功：{outputPath}，大小 {report.summary.totalSize} bytes");
        }
        else
        {
            Debug.LogError($"[漓江回声构建] Android APK 构建失败：{report.summary.result}，错误 {report.summary.totalErrors}");
        }

        EditorApplication.Exit(success ? 0 : 1);
    }

    private static void Tick()
    {
        if (!EditorApplication.isPlaying)
        {
            return;
        }

        waitFrames++;
        if (waitFrames < 12)
        {
            return;
        }

        try
        {
            controller ??= UnityEngine.Object.FindFirstObjectByType<LijiangEchoGameController>();
            Require(controller != null, "运行时关卡控制器未创建");

            switch (phase)
            {
                case 0:
                    ValidateMrFoundation();
                    Capture("01_start");
                    ValidateStartPointerInteraction();
                    Invoke("ShowIntro");
                    NextPhase();
                    break;
                case 1:
                    SetField("stageTimer", 1.5f);
                    Invoke("UpdateIntroWalkStage");
                    Transform flyItem = GameObject.Find("近景山一")?.transform;
                    Require(flyItem != null && flyItem.localPosition.z > 2f, "过场素材没有从远处沿深度接近");
                    SetField("stageTimer", 3f);
                    Invoke("UpdateIntroWalkStage");
                    Capture("02_spatial_intro");
                    Invoke("StartIntroPreLevelVideo");
                    NextPhase();
                    break;
                case 2:
                    Transform scrollRoot = FindTransformIncludingInactive("过场漂浮素材");
                    Require(scrollRoot != null && !scrollRoot.gameObject.activeSelf, "关卡前视频播放时过场素材仍在遮挡");
                    Require(GameObject.Find("关卡前播放动画视频") != null, "正确的关卡前播放动画没有保留");
                    Invoke("ShowTrace");
                    NextPhase();
                    break;
                case 3:
                    Require(GameObject.Find("描绘参考纹样")?.GetComponent<SpriteRenderer>() != null, "真实待绘纹样没有进入描绘阶段");
                    Require(GameObject.Find("纹样轨迹引导") == null, "旧的程序引导线仍在遮挡真实纹样");
                    Require((int)GetField("tracePointIndex") == 0, "描绘阶段在没有连续输入时被自动跳过");
                    Capture("03_trace");
                    Invoke("ShowBattle");
                    NextPhase();
                    break;
                case 4:
                    Invoke("SpawnDueNotes", 21f);
                    ValidateBattle();
                    Capture("04_battle");
                    Finish(true, "MR 自动运行验收通过");
                    break;
            }
        }
        catch (Exception exception)
        {
            Finish(false, exception.ToString());
        }
    }

    private static void ValidateMrFoundation()
    {
        Transform stageRoot = GameObject.Find("漓江回声_关卡画面")?.transform;
        Require(stageRoot != null, "关卡画面根节点未创建");
        Require(stageRoot.parent == null, "关卡画面仍然挂在头显相机下");
        Require(Mathf.Abs(stageRoot.localScale.x - 0.78f) < 0.01f, "MR 内容空间缩放不正确");
        Transform cameraAnchor = GetField("cameraAnchor") as Transform;
        Require(cameraAnchor != null && cameraAnchor.name.Contains("CenterEye"),
            "关卡画面没有优先锚定到头显中央眼相机");

        Camera cameraComponent = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
        Require(cameraComponent != null, "游戏相机不存在");
        Require(cameraComponent.clearFlags == CameraClearFlags.SolidColor, "相机没有为透视合成使用透明清屏");
        Require(cameraComponent.backgroundColor.a < 0.01f, "相机背景 Alpha 会遮挡 Passthrough");
        Require(cameraComponent.stereoTargetEye == StereoTargetEyeMask.Both, "相机没有同时渲染双眼");

        OVRManager manager = UnityEngine.Object.FindFirstObjectByType<OVRManager>(FindObjectsInactive.Include);
        Require(manager != null && manager.gameObject.activeInHierarchy, "MR 相机架在启动时被关闭");
        Require(manager.isInsightPassthroughEnabled, "OVRManager 没有启用现实透视");
        Require(manager.launchSimultaneousHandsControllersOnStartup, "手部与控制器并行输入未开启");

        OVRPassthroughLayer layer = UnityEngine.Object.FindFirstObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
        Require(layer != null && layer.gameObject.activeInHierarchy && layer.enabled && !layer.hidden, "现实透视层未激活");
#pragma warning disable CS0618
        Require(layer.overlayType == OVROverlay.OverlayType.Underlay, "现实透视层仍处于不渲染或遮挡模式");
#pragma warning restore CS0618

        Require(FindTransformIncludingInactive("左手描画射线")?.GetComponent<LineRenderer>() != null,
            "左手控制器可视射线未创建");
        Require(FindTransformIncludingInactive("右手描画射线")?.GetComponent<LineRenderer>() != null,
            "右手控制器可视射线未创建");

        ScriptableObject config = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Oculus/OculusProjectConfig.asset");
        SerializedProperty passthrough = new SerializedObject(config).FindProperty("_insightPassthroughSupport");
        Require(passthrough != null && passthrough.enumValueIndex > 0, "Meta 项目级 Passthrough 能力未开启");
    }

    private static void ValidateBattle()
    {
        Transform stageRoot = GameObject.Find("漓江回声_关卡画面")?.transform;
        Require(stageRoot != null, "战斗验收找不到关卡根节点");
        Transform ring = GameObject.Find("中央节奏判定双圆环")?.transform;
        Require(ring != null, "中央判定圈未创建");
        Require(Mathf.Abs(ring.localPosition.x) < 0.001f && Mathf.Abs(ring.localPosition.y) < 0.001f,
            "判定圈没有位于画面正中心");

        Transform left = GameObject.Find("节奏击打_2")?.transform;
        Transform right = GameObject.Find("节奏击打_1")?.transform;
        Require(left != null && right != null, "左右节奏块未成对生成");
        Require(Mathf.Abs(left.localPosition.x + right.localPosition.x) < 0.001f, "左右节奏块起点不对称");
        Require(Mathf.Sign(left.localScale.x) == -Mathf.Sign(right.localScale.x), "右侧节奏块没有镜像");
        Require(GameObject.Find("蛇纹长按_0") != null, "蛇纹长按音符没有生成");
        Require(GameObject.Find("蛙纹滑动_3") != null, "蛙纹滑动音符没有生成");

        float[] noteTimes = GetField("noteTimes") as float[];
        Require(noteTimes != null && noteTimes.Length >= 100 && noteTimes[^1] > 100f,
            "战斗谱面没有覆盖整首音乐");
        AudioSource battleMusic = GetField("battleMusicSource") as AudioSource;
        Invoke("StartBattleMusic");
        Require(battleMusic != null && battleMusic.clip != null && battleMusic.clip.length > 104f,
            "指定战斗音乐未接入或被截短");
        battleMusic.Stop();

        Camera[] cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Require(cameras.Where(item => item.isActiveAndEnabled).All(item => item.stereoTargetEye == StereoTargetEyeMask.Both),
            "存在只渲染单眼的活动相机");

        SetField("controllerMotionReady", false);
        SetField("battleControllerButtonDown", true);
        bool controllerButtonAccepted = (bool)InvokeWithResult("BattleGesturePressed");
        SetField("battleControllerButtonDown", false);
        Require(controllerButtonAccepted, "控制器扳机、握把或面键没有接入打击判定");

        SetField("battleControllerButtonHeld", true);
        Require((bool)InvokeWithResult("BattleHoldHeld"), "控制器持续按住没有接入蛇纹长按");
        SetField("battleControllerButtonHeld", false);

        SetField("nextNoteIndex", 3);
        SetField("controllerMotionReady", true);
        SetField("swipeCooldown", 0f);
        SetField("leftControllerVelocity", -stageRoot.right * 1.2f);
        SetField("rightControllerVelocity", Vector3.zero);
        Require((bool)InvokeWithResult("BattleSwipePerformed"), "控制器挥划没有接入蛙纹判定");
    }

    private static void ValidateStartPointerInteraction()
    {
        Transform stageRoot = GameObject.Find("漓江回声_关卡画面")?.transform;
        Require(stageRoot != null, "无法测试开始按钮射线命中");

        GameObject fakeControllerObject = new GameObject("验收用控制器");
        Transform fakeController = fakeControllerObject.transform;
        fakeController.rotation = stageRoot.rotation;
        SetField("leftControllerAnchor", fakeController);
        SetField("leftControllerTracked", true);
        SetField("rightControllerTracked", false);

        fakeController.position = stageRoot.TransformPoint(new Vector3(1.6f, -0.45f, -1.5f));
        SetField("leftTriggerDown", true);
        Invoke("UpdateStart");
        Require(GetField("currentStage").ToString() == "Start", "开始界面仍可在按钮外随意点击跳过");

        fakeController.position = stageRoot.TransformPoint(new Vector3(0f, -0.45f, -1.5f));
        SetField("leftTriggerDown", true);
        Invoke("UpdateStart");
        Require(GetField("currentStage").ToString() == "Select", "控制器射线对准开始按钮后没有进入选关");

        UnityEngine.Object.DestroyImmediate(fakeControllerObject);
        SetField("leftControllerAnchor", null);
    }

    private static void Capture(string fileName)
    {
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        {
            Debug.Log("[漓江回声验收] 无显卡批处理模式跳过截图：" + fileName);
            return;
        }

        Camera cameraComponent = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
        Require(cameraComponent != null, "无法为验收截图找到相机");

        const int width = 1280;
        const int height = 720;
        RenderTexture renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGBA32, false);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = cameraComponent.targetTexture;
        try
        {
            cameraComponent.targetTexture = renderTexture;
            cameraComponent.Render();
            RenderTexture.active = renderTexture;
            screenshot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            screenshot.Apply();

            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../ValidationCaptures"));
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, fileName + ".png"), screenshot.EncodeToPNG());
        }
        finally
        {
            cameraComponent.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            renderTexture.Release();
            UnityEngine.Object.Destroy(renderTexture);
            UnityEngine.Object.Destroy(screenshot);
        }
    }

    private static Transform FindTransformIncludingInactive(string objectName)
    {
        return UnityEngine.Object
            .FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(item => item.name == objectName);
    }

    private static void Invoke(string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(LijiangEchoGameController).GetMethod(methodName, PrivateInstance);
        Require(method != null, "找不到验收方法：" + methodName);
        method.Invoke(controller, arguments);
    }

    private static object InvokeWithResult(string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(LijiangEchoGameController).GetMethod(methodName, PrivateInstance);
        Require(method != null, "找不到验收方法：" + methodName);
        return method.Invoke(controller, arguments);
    }

    private static object GetField(string fieldName)
    {
        FieldInfo field = typeof(LijiangEchoGameController).GetField(fieldName, PrivateInstance);
        Require(field != null, "找不到验收字段：" + fieldName);
        return field.GetValue(controller);
    }

    private static void SetField(string fieldName, object value)
    {
        FieldInfo field = typeof(LijiangEchoGameController).GetField(fieldName, PrivateInstance);
        Require(field != null, "找不到验收字段：" + fieldName);
        field.SetValue(controller, value);
    }

    private static void NextPhase()
    {
        phase++;
        waitFrames = 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static void Finish(bool success, string message)
    {
        SessionState.SetBool(SessionKey, false);
        EditorApplication.update -= Tick;
        if (success)
        {
            Debug.Log("[漓江回声验收] " + message);
        }
        else
        {
            Debug.LogError("[漓江回声验收] " + message);
        }

        EditorApplication.Exit(success ? 0 : 1);
    }
}
