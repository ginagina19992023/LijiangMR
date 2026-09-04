using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 描绘环节的控制器。对应旧 LijiangEchoGameController 里的
/// ShowTrace / UpdateTrace(单手 + 双手各描各半) / CompleteTrace / BuildTracePath。
/// 视觉/输入统一用 LijiangEchoStageKit(精灵拼装 + 每只手的射线落点)。
///
/// 【不是场景控制器】9.1 需求第 3 条要求描绘在行进过程中穿插三次,所以它是一个
/// **可重入的模块**:由 IntroStageController 调 Begin(第几个图案, 双手?, 画完回调) 拉起,
/// 描完走回调、调 Teardown() 收起,行进段接着走。同一个实例可以反复 Begin/Teardown。
/// 自己不决定"下一步去哪"——去哪是调用方的事。
/// </summary>
public class TraceStageController : MonoBehaviour
{
    private const float TracePointTolerance = 0.105f;   // 笔迹离下一个待描点多近算"描到"

    // 三个描绘图案,按【第几次绘制】取(不是按第几关):0=蛇纹 1=鸟纹 2=铜钱。
    // 需求确认:蛙纹关里三次绘制把这三个纹样依次走一遍。
    private readonly string[] tracePaths = { "pattern/snake_trace", "pattern/bird_trace", "pattern/coin_trace" };
    private readonly string[] donePaths = { "pattern/snake_done", "pattern/bird_done", "pattern/coin_done" };
    private readonly RectInt[] traceCrops =
    {
        new RectInt(273, 2314, 1951, 2547),
        new RectInt(1822, 2125, 2973, 2185),
        new RectInt(995, 836, 1335, 1359)
    };
    private readonly RectInt[] doneCrops =
    {
        new RectInt(1258, 2332, 948, 2510),
        new RectInt(3289, 2141, 1488, 2151),
        new RectInt(1629, 836, 701, 1359)
    };
    private readonly string[] completionSounds = { "snake", "swipe", "coin" };

    /// <summary>绘制次数(= 图案数)。调用方用它判断还剩几次。</summary>
    public static int PatternCount => 3;

    /// <summary>取第 index 个图案的素材路径与裁剪框。行进段的「浮动纹样」用它,
    /// 保证卡点上飘的那个纹样和点进去要描的是同一张图。</summary>
    public static bool TryGetPatternArt(int index, out string resourcePath, out RectInt crop)
    {
        string[] paths = { "pattern/snake_trace", "pattern/bird_trace", "pattern/coin_trace" };
        RectInt[] crops =
        {
            new RectInt(273, 2314, 1951, 2547),
            new RectInt(1822, 2125, 2973, 2185),
            new RectInt(995, 836, 1335, 1359)
        };

        if (index < 0 || index >= paths.Length)
        {
            resourcePath = null;
            crop = default;
            return false;
        }

        resourcePath = paths[index];
        crop = crops[index];
        return true;
    }

    private Transform stageRoot;
    private readonly List<GameObject> spawnedObjects = new List<GameObject>();
    private readonly List<LijiangEchoStageKit.MotionItem> motions = new List<LijiangEchoStageKit.MotionItem>();

    private int patternIndex;          // 这一次画三个图案里的哪一个
    private Action onTraceComplete;    // 画完通知调用方(IntroStageController)
    private bool active;               // Begin 之后为真,Teardown 后为假

    private Vector3[] tracePoints;
    private int tracePointIndex;
    private Vector3[] traceLeftPoints;
    private int traceLeftIndex;
    private bool traceTwoHands;

    private LineRenderer traceDrawRenderer;
    private LineRenderer traceMirrorDrawRenderer;
    private Transform tracePointer;
    private Transform traceMirrorPointer;
    private TextMesh traceFeedbackText;

    private bool traceCompleted;
    private float traceCompleteTimer;

    private Vector3 previousTracePointer;
    private bool hasPreviousTracePointer;
    private Vector3 previousTraceLeftPointer;
    private bool hasPreviousTraceLeftPointer;

    /// <summary>拉起一次描绘。pattern = 三个图案里的第几个(0蛇/1鸟/2铜钱);
    /// twoHands = 双手各描一半还是单手描整条;onComplete = 描完(含 1.05s 成功动效)后的回调。</summary>
    public void Begin(int pattern, bool twoHands, Action onComplete)
    {
        Teardown();                                    // 同一个实例反复用,先清干净上一次

        patternIndex = Mathf.Clamp(pattern, 0, tracePaths.Length - 1);
        traceTwoHands = twoHands;
        onTraceComplete = onComplete;

        traceCompleted = false;
        traceCompleteTimer = 0f;
        tracePointIndex = 0;
        traceLeftIndex = 0;
        hasPreviousTracePointer = false;
        hasPreviousTraceLeftPointer = false;

        stageRoot = LijiangEchoStageKit.PrepareStageRoot("漓江回声_描绘舞台");
        BuildTraceStage();
        active = true;
    }

    /// <summary>收起这一次描绘:销毁描绘台、清空生成物与动效,手柄射线交还给调用方。
    /// Begin 会先自动调它,所以调用方只在"整段结束"时需要显式调一次。</summary>
    public void Teardown()
    {
        active = false;
        onTraceComplete = null;
        CancelInvoke();
        StopControllerVibration();

        spawnedObjects.Clear();
        motions.Clear();
        traceDrawRenderer = null;
        traceMirrorDrawRenderer = null;
        tracePointer = null;
        traceMirrorPointer = null;
        traceFeedbackText = null;
        tracePoints = null;
        traceLeftPoints = null;

        if (stageRoot != null)
        {
            Destroy(stageRoot.gameObject);             // 描绘台是 Begin 里新建的根,整个销毁
            stageRoot = null;
        }
    }

    private void Update()
    {
        if (!active || stageRoot == null)
        {
            return;
        }

        LijiangEchoStageKit.UpdateControllerInput(stageRoot);   // 每帧刷新手柄射线/扳机缓存,供落点判定
        LijiangEchoStageKit.UpdateMotions(motions);
        UpdateTrace();
    }

    // 描绘结束 → 通知调用方。去哪(继续走 / 播视频 / 进战斗)由调用方决定,这里不管。
    private void Finish()
    {
        Action callback = onTraceComplete;
        active = false;
        StopControllerVibration();
        callback?.Invoke();
    }

    // ————————————————————————————— 可编辑描绘台 Prefab —————————————————————————————

    /// <summary>Resources 下描绘台 Prefab 的路径前缀,后面接图案序号(0蛇/1鸟/2铜钱)。</summary>
    public const string PanelPrefabPrefix = "LijiangEchoTrace/TracePanel_";

    /// <summary>Prefab 里两条路线物件的名字。运行时按名字找,请勿修改。</summary>
    public const string PathObjectOneHand = "描绘路线_单手";
    public const string PathObjectTwoHand = "描绘路线_双手右半";

    /// <summary>有 Prefab 就实例化并从里面读出描绘路线;没有则返回 null 让调用方回退代码生成。
    /// 路线点按【物件当前的位置/旋转/缩放】换算到舞台坐标 —— 所以你在 Prefab 里拖动、
    /// 旋转、缩放那条线,运行时的判定路径会跟着一起变,和你看到的完全一致。</summary>
    private Vector3[] TryBuildFromPanelPrefab(bool splitHands)
    {
        GameObject prefab = Resources.Load<GameObject>(PanelPrefabPrefix + patternIndex);
        if (prefab == null)
        {
            return null;
        }

        GameObject panel = Instantiate(prefab, stageRoot);
        panel.transform.localPosition = Vector3.zero;
        panel.transform.localRotation = Quaternion.identity;
        panel.transform.localScale = Vector3.one;
        spawnedObjects.Add(panel);

        // 两条线都先关掉显示:它们只是"路径数据 + 编辑时的可视参考",
        // 运行时的指引线/已描线由下面的代码另外画,免得叠成两条。
        LineRenderer oneHand = FindLine(panel.transform, PathObjectOneHand);
        LineRenderer twoHand = FindLine(panel.transform, PathObjectTwoHand);
        LineRenderer source = splitHands ? (twoHand ?? oneHand) : (oneHand ?? twoHand);

        if (oneHand != null) { oneHand.enabled = false; }
        if (twoHand != null) { twoHand.enabled = false; }

        if (source == null || source.positionCount < 2)
        {
            Debug.LogWarning($"[漓江回声] {PanelPrefabPrefix}{patternIndex} 里没找到可用的" +
                             $"「{PathObjectOneHand}」或「{PathObjectTwoHand}」,本次描绘回退代码生成的路径。");
            return null;
        }

        Vector3[] points = new Vector3[source.positionCount];
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 local = source.GetPosition(i);
            Vector3 world = source.useWorldSpace ? local : source.transform.TransformPoint(local);
            Vector3 inStage = stageRoot.InverseTransformPoint(world);
            inStage.z = LijiangEchoStageKit.TracePlaneZ;   // 判定统一压到描绘平面,深度由代码保证一致
            points[i] = inStage;
        }

        return points;
    }

    private static LineRenderer FindLine(Transform root, string objectName)
    {
        if (root.name == objectName)
        {
            return root.GetComponent<LineRenderer>();
        }

        for (int i = 0; i < root.childCount; i++)
        {
            LineRenderer found = FindLine(root.GetChild(i), objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    // ————————————————————————————— 搭建描绘台 —————————————————————————————

    private void BuildTraceStage()
    {
        bool splitHands = traceTwoHands;               // Begin 传进来的,决定单手整条还是双手各半

        // 9.1 需求第 4 条:可编辑描绘台 Prefab 优先。
        // Resources/LijiangEchoTrace/TracePanel_{图案序号} 存在就用它 —— 背景、参考纹样、
        // 描绘路线全部由 Prefab 决定,你在 Prefab 模式里拖着把纹样和路线对齐、一起放大即可。
        // 没有 Prefab 就回退到下面的代码生成,和改造前完全一致。
        tracePoints = TryBuildFromPanelPrefab(splitHands);

        if (tracePoints == null)
        {
            LijiangEchoStageKit.AddLayer(stageRoot, spawnedObjects, "transition/purple_frame", "描绘阶段淡紫边框",
                Vector3.zero, LijiangEchoStageKit.MainCanvasWidth, -20, 0.14f);
            LijiangEchoStageKit.AddLayer(stageRoot, spawnedObjects, "pattern/drawing_card", "纹样描绘台",
                new Vector3(0f, 0f, -0.22f), 4.25f, -4, 0.72f);

            tracePoints = BuildTracePath(patternIndex, splitHands);

            GameObject sourcePattern = LijiangEchoStageKit.AddCroppedSprite(
                stageRoot, spawnedObjects, tracePaths[patternIndex], "描绘参考纹样",
                traceCrops[patternIndex], new Vector3(0f, 0.02f, -0.48f), 0.88f, 18, 0.74f, false);
            LijiangEchoStageKit.RegisterMotion(motions, sourcePattern, LijiangEchoStageKit.MotionKind.Pulse, 0.01f, 1.7f, 0f);
        }

        // 全程「淡淡指引线」——沿纹样形状铺满整条路径,给玩家指引方向。
        LineRenderer traceGuideRenderer = LijiangEchoStageKit.AddLineRenderer(
            stageRoot, spawnedObjects, "纹样描绘指引", 0.03f, new Color(1f, 0.9f, 0.55f, 0.16f), 30);
        traceGuideRenderer.positionCount = tracePoints.Length;
        for (int gi = 0; gi < tracePoints.Length; gi++)
        {
            traceGuideRenderer.SetPosition(gi, tracePoints[gi] + new Vector3(0f, 0f, -0.018f));
        }

        traceDrawRenderer = LijiangEchoStageKit.AddLineRenderer(
            stageRoot, spawnedObjects, "已描绘轨迹", 0.072f, new Color(1f, 0.86f, 0.28f, 0.98f), 34);

        // 已描绘的线沿绘制方向从暗金渐变到亮发光(头→尾逐渐点亮)。
        Gradient traceGlowGradient = new Gradient();
        traceGlowGradient.SetKeys(
            new GradientColorKey[]
            {
                new GradientColorKey(new Color(0.85f, 0.6f, 0.2f), 0f),
                new GradientColorKey(new Color(1f, 0.95f, 0.6f), 1f)
            },
            new GradientAlphaKey[]
            {
                new GradientAlphaKey(0.55f, 0f),
                new GradientAlphaKey(1f, 1f)
            });
        traceDrawRenderer.colorGradient = traceGlowGradient;

        GameObject pointerObject = LijiangEchoStageKit.AddIcon(
            stageRoot, spawnedObjects, "battle/hit_ring_center", "手柄描绘光标",
            new Vector3(0f, 0f, LijiangEchoStageKit.TracePlaneZ - 0.04f), 0.105f, 42, 0.92f);
        tracePointer = pointerObject.transform;
        tracePointer.gameObject.SetActive(false);

        // 双手独立描绘:左半轨迹 = 右半的水平镜像;左手用自己的指引线/已描绘线/光标,独立描、独立判定。
        if (splitHands)
        {
            traceLeftPoints = new Vector3[tracePoints.Length];
            for (int gi = 0; gi < tracePoints.Length; gi++)
            {
                Vector3 gp = tracePoints[gi];
                traceLeftPoints[gi] = new Vector3(-gp.x, gp.y, gp.z);
            }

            LineRenderer mirrorGuide = LijiangEchoStageKit.AddLineRenderer(
                stageRoot, spawnedObjects, "纹样描绘指引(左手)", 0.03f, new Color(1f, 0.9f, 0.55f, 0.16f), 30);
            mirrorGuide.positionCount = traceLeftPoints.Length;
            for (int gi = 0; gi < traceLeftPoints.Length; gi++)
            {
                mirrorGuide.SetPosition(gi, traceLeftPoints[gi] + new Vector3(0f, 0f, -0.018f));
            }

            traceMirrorDrawRenderer = LijiangEchoStageKit.AddLineRenderer(
                stageRoot, spawnedObjects, "已描绘轨迹(左手)", 0.072f, new Color(1f, 0.86f, 0.28f, 0.98f), 34);
            traceMirrorDrawRenderer.colorGradient = traceGlowGradient;

            GameObject mirrorPointerObject = LijiangEchoStageKit.AddIcon(
                stageRoot, spawnedObjects, "battle/hit_ring_center", "手柄描绘光标(左手)",
                new Vector3(0f, 0f, LijiangEchoStageKit.TracePlaneZ - 0.04f), 0.105f, 42, 0.92f);
            traceMirrorPointer = mirrorPointerObject.transform;
            traceMirrorPointer.gameObject.SetActive(false);
        }
        else
        {
            traceLeftPoints = null;
            traceMirrorDrawRenderer = null;
            traceMirrorPointer = null;
        }

        traceFeedbackText = LijiangEchoStageKit.AddText(
            stageRoot, spawnedObjects, "绘制纹样",
            new Vector3(0f, 0.78f, -0.56f), 0.027f, new Color(1f, 0.93f, 0.72f, 0.94f), 44);
    }

    // ————————————————————————————— 描绘推进 —————————————————————————————

    private void UpdateTrace()
    {
        if (traceCompleted)
        {
            traceCompleteTimer += Time.deltaTime;
            if (traceCompleteTimer >= 1.05f)
            {
                Finish();
            }

            return;
        }

        if (traceTwoHands)
        {
            UpdateTraceTwoHands();
        }
        else
        {
            UpdateTraceSingle();
        }
    }

    // 单手:一只手(哪只压扳机用哪只)从头描到尾,描完整只算完成。
    private void UpdateTraceSingle()
    {
        if (!LijiangEchoStageKit.TryGetActivePointer(stageRoot, out Vector3 localPoint, out bool drawing))
        {
            if (tracePointer != null)
            {
                tracePointer.gameObject.SetActive(false);
            }

            hasPreviousTracePointer = false;
            return;
        }

        if (tracePointer != null)
        {
            tracePointer.gameObject.SetActive(true);
            tracePointer.localPosition = new Vector3(localPoint.x, localPoint.y, LijiangEchoStageKit.TracePlaneZ - 0.04f);
        }

        if (!drawing || tracePoints == null || tracePointIndex >= tracePoints.Length)
        {
            hasPreviousTracePointer = false;
            if (traceFeedbackText != null)
            {
                traceFeedbackText.text = tracePointIndex == 0
                    ? "按住扳机,从亮起的起点沿纹样描画"
                    : $"描画进度 {Mathf.RoundToInt(tracePointIndex * 100f / tracePoints.Length)}%";
            }
            return;
        }

        Vector3 pointerOnPlane = new Vector3(localPoint.x, localPoint.y, LijiangEchoStageKit.TracePlaneZ);
        tracePointIndex = AdvanceTraceHand(tracePoints, tracePointIndex, pointerOnPlane, ref previousTracePointer, ref hasPreviousTracePointer);
        UpdateTraceLine();
        if (traceFeedbackText != null && tracePointIndex < tracePoints.Length)
        {
            traceFeedbackText.text = $"描画进度 {Mathf.RoundToInt(tracePointIndex * 100f / tracePoints.Length)}%";
        }

        if (tracePointIndex >= tracePoints.Length)
        {
            CompleteTrace();
        }
    }

    // 真·双手独立:右手描右半(tracePoints)、左手描左半(traceLeftPoints),各自指针/进度/判定,两半都完成才成功。
    private void UpdateTraceTwoHands()
    {
        // 编辑器鼠标兜底:默认画右手;【按住 Shift 时改画左手】,这样单鼠标也能把左右两半都描完。
        bool mouseHas = LijiangEchoStageKit.TryGetMousePointer(stageRoot, out Vector3 mousePoint, out bool mouseDraw);
        bool mouseToLeft = Keyboard.current != null &&
                           (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

        // —— 右手 → 右半 ——
        bool rightHas = LijiangEchoStageKit.TryGetHandPointer(stageRoot, true, out Vector3 rPoint, out bool rDraw);
        if (!rightHas && mouseHas && !mouseToLeft)
        {
            rightHas = true;
            rPoint = mousePoint;
            rDraw = mouseDraw;
        }

        UpdateTraceCursor(tracePointer, rightHas, rPoint);
        if (rightHas && rDraw && tracePoints != null && tracePointIndex < tracePoints.Length)
        {
            tracePointIndex = AdvanceTraceHand(tracePoints, tracePointIndex, new Vector3(rPoint.x, rPoint.y, LijiangEchoStageKit.TracePlaneZ), ref previousTracePointer, ref hasPreviousTracePointer);
        }
        else
        {
            hasPreviousTracePointer = false;
        }

        // —— 左手 → 左半(编辑器按住 Shift 时鼠标画这半)——
        bool leftHas = LijiangEchoStageKit.TryGetHandPointer(stageRoot, false, out Vector3 lPoint, out bool lDraw);
        if (!leftHas && mouseHas && mouseToLeft)
        {
            leftHas = true;
            lPoint = mousePoint;
            lDraw = mouseDraw;
        }

        UpdateTraceCursor(traceMirrorPointer, leftHas, lPoint);
        if (leftHas && lDraw && traceLeftPoints != null && traceLeftIndex < traceLeftPoints.Length)
        {
            traceLeftIndex = AdvanceTraceHand(traceLeftPoints, traceLeftIndex, new Vector3(lPoint.x, lPoint.y, LijiangEchoStageKit.TracePlaneZ), ref previousTraceLeftPointer, ref hasPreviousTraceLeftPointer);
        }
        else
        {
            hasPreviousTraceLeftPointer = false;
        }

        DrawTraceHalf(traceDrawRenderer, tracePoints, tracePointIndex);
        DrawTraceHalf(traceMirrorDrawRenderer, traceLeftPoints, traceLeftIndex);

        bool rightDone = tracePoints != null && tracePointIndex >= tracePoints.Length;
        bool leftDone = traceLeftPoints != null && traceLeftIndex >= traceLeftPoints.Length;

        if (traceFeedbackText != null)
        {
            if (tracePointIndex == 0 && traceLeftIndex == 0)
            {
                traceFeedbackText.text = "双手各按住扳机,左右手分别沿两侧描画";
            }
            else
            {
                int rp = tracePoints != null && tracePoints.Length > 0 ? Mathf.RoundToInt(tracePointIndex * 100f / tracePoints.Length) : 0;
                int lp = traceLeftPoints != null && traceLeftPoints.Length > 0 ? Mathf.RoundToInt(traceLeftIndex * 100f / traceLeftPoints.Length) : 0;
                traceFeedbackText.text = $"左手 {lp}%　·　右手 {rp}%";
            }
        }

        if (rightDone && leftDone)
        {
            CompleteTrace();
        }
    }

    // 沿路径推进"已描到"的下标:当前笔迹(上一帧→本帧)离下一个待描点足够近就吃掉它。返回新的下标。
    private int AdvanceTraceHand(Vector3[] points, int index, Vector3 pointerOnPlane, ref Vector3 previousPointer, ref bool hasPrevious)
    {
        int advanced = 0;
        while (points != null && index < points.Length && advanced < 10)
        {
            float distance = hasPrevious
                ? DistanceToSegment(points[index], previousPointer, pointerOnPlane)
                : Vector3.Distance(points[index], pointerOnPlane);
            if (distance > TracePointTolerance)
            {
                break;
            }

            index++;
            advanced++;
        }

        previousPointer = pointerOnPlane;
        hasPrevious = true;
        return index;
    }

    private void UpdateTraceCursor(Transform cursor, bool visible, Vector3 localPoint)
    {
        if (cursor == null)
        {
            return;
        }

        cursor.gameObject.SetActive(visible);
        if (visible)
        {
            cursor.localPosition = new Vector3(localPoint.x, localPoint.y, LijiangEchoStageKit.TracePlaneZ - 0.04f);
        }
    }

    private void UpdateTraceLine()
    {
        if (traceDrawRenderer == null || tracePoints == null)
        {
            return;
        }

        int count = Mathf.Clamp(tracePointIndex, 0, tracePoints.Length);
        traceDrawRenderer.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            traceDrawRenderer.SetPosition(i, tracePoints[i] + new Vector3(0f, 0f, -0.025f));
        }
    }

    private void DrawTraceHalf(LineRenderer renderer, Vector3[] points, int index)
    {
        if (renderer == null || points == null)
        {
            return;
        }

        int count = Mathf.Clamp(index, 0, points.Length);
        renderer.positionCount = count;
        for (int i = 0; i < count; i++)
        {
            renderer.SetPosition(i, points[i] + new Vector3(0f, 0f, -0.025f));
        }
    }

    private void CompleteTrace()
    {
        traceCompleted = true;
        traceCompleteTimer = 0f;
        if (traceFeedbackText != null)
        {
            traceFeedbackText.text = "绘制成功";
            traceFeedbackText.color = new Color(1f, 0.88f, 0.3f, 1f);
        }

        GameObject completedPattern = LijiangEchoStageKit.AddCroppedSprite(
            stageRoot, spawnedObjects, donePaths[patternIndex], "完成纹样光效",
            doneCrops[patternIndex], new Vector3(0f, 0.02f, -0.68f), 0.92f, 48, 0.94f, false);
        LijiangEchoStageKit.RegisterMotion(motions, completedPattern, LijiangEchoStageKit.MotionKind.Pulse, 0.035f, 3.2f, 0f);
        LijiangEchoStageKit.PlaySfx(completionSounds[patternIndex], 0.68f);
        OVRInput.SetControllerVibration(0.45f, 0.65f, OVRInput.Controller.LTouch | OVRInput.Controller.RTouch);
        Invoke(nameof(StopControllerVibration), 0.16f);
    }

    private void StopControllerVibration()
    {
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch | OVRInput.Controller.RTouch);
    }

    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        if (segment.sqrMagnitude < 0.000001f)
        {
            return Vector3.Distance(point, start);
        }

        float t = Mathf.Clamp01(Vector3.Dot(point - start, segment) / segment.sqrMagnitude);
        return Vector3.Distance(point, start + segment * t);
    }

    // 每关纹样的路径点(与旧 BuildTracePath 完全一致):rightHalfOnly=true 时只生成右半,
    // 左半由 traceLeftPoints 水平镜像补出;蛙纹(0)/鸟纹(1)贝塞尔细分,铜钱纹(2)是圆。
    /// <summary>生成某个纹样的描绘路径点。public static:编辑器工具烘 Prefab 时调同一份数学,
    /// 保证 Prefab 里看到的线和运行时判定用的线是同一条。</summary>
    public static Vector3[] BuildTracePath(int level, bool rightHalfOnly)
    {
        float planeZ = LijiangEchoStageKit.TracePlaneZ;
        List<Vector3> points = new List<Vector3>();
        if (level == 2)
        {
            if (rightHalfOnly)
            {
                const int halfPoints = 36;
                for (int i = 0; i < halfPoints; i++)
                {
                    float angle = Mathf.PI * 0.5f - i / (float)(halfPoints - 1) * Mathf.PI;
                    points.Add(new Vector3(Mathf.Cos(angle) * 0.43f, Mathf.Sin(angle) * 0.43f + 0.02f, planeZ));
                }

                return points.ToArray();
            }

            const int circlePoints = 72;
            for (int i = 0; i < circlePoints; i++)
            {
                float angle = Mathf.PI * 0.5f - i / (float)(circlePoints - 1) * Mathf.PI * 2f;
                points.Add(new Vector3(Mathf.Cos(angle) * 0.43f, Mathf.Sin(angle) * 0.43f + 0.02f, planeZ));
            }

            return points.ToArray();
        }

        Vector2[] controls;
        if (rightHalfOnly)
        {
            controls = level == 0
                ? new[]
                {
                    new Vector2(0f, -0.47f), new Vector2(0.18f, -0.34f),
                    new Vector2(0.10f, -0.14f), new Vector2(0.34f, -0.03f),
                    new Vector2(0.17f, 0.13f), new Vector2(0.38f, 0.28f),
                    new Vector2(0.20f, 0.44f)
                }
                : new[]
                {
                    new Vector2(0f, 0.38f), new Vector2(0.10f, 0.14f),
                    new Vector2(0.34f, 0.30f), new Vector2(0.52f, 0.02f),
                    new Vector2(0.25f, -0.06f), new Vector2(0f, -0.45f)
                };
        }
        else
        {
            controls = level == 0
                ? new[]
                {
                    new Vector2(-0.20f, 0.44f), new Vector2(-0.38f, 0.28f),
                    new Vector2(-0.17f, 0.13f), new Vector2(-0.34f, -0.03f),
                    new Vector2(-0.10f, -0.14f), new Vector2(-0.18f, -0.34f),
                    new Vector2(0f, -0.47f), new Vector2(0.18f, -0.34f),
                    new Vector2(0.10f, -0.14f), new Vector2(0.34f, -0.03f),
                    new Vector2(0.17f, 0.13f), new Vector2(0.38f, 0.28f),
                    new Vector2(0.20f, 0.44f)
                }
                : new[]
                {
                    new Vector2(-0.52f, 0.02f), new Vector2(-0.34f, 0.30f),
                    new Vector2(-0.10f, 0.14f), new Vector2(0f, 0.38f),
                    new Vector2(0.10f, 0.14f), new Vector2(0.34f, 0.30f),
                    new Vector2(0.52f, 0.02f), new Vector2(0.25f, -0.06f),
                    new Vector2(0f, -0.45f), new Vector2(-0.25f, -0.06f),
                    new Vector2(-0.52f, 0.02f)
                };
        }

        const int subdivisions = 6;
        for (int segment = 0; segment < controls.Length - 1; segment++)
        {
            for (int step = 0; step < subdivisions; step++)
            {
                Vector2 point = Vector2.Lerp(controls[segment], controls[segment + 1], step / (float)subdivisions);
                points.Add(new Vector3(point.x, point.y + 0.02f, planeZ));
            }
        }

        Vector2 last = controls[^1];
        points.Add(new Vector3(last.x, last.y + 0.02f, planeZ));
        return points.ToArray();
    }
}
