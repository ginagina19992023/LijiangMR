using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// 第二关行进段场景(Stage_Intro)的控制器。对应旧 LijiangEchoGameController 里的
/// ShowIntro / BuildIntroWalkStage / UpdateIntro,并按 9.1 需求第 3 条重新编排。
///
/// 【流程】前进① → 绘制① → 前进② → 绘制② → 前进③ → 绘制③ → 过渡视频 → 战斗。
/// 三次绘制穿插在三段前进之间,不是走完一次性画三遍;当前这段没画完就走不过卡点。
/// 描绘不是独立场景,是本场景里的 TraceStageController 模块,被 Begin/Teardown 拉起收起三次
/// (三个图案固定 蛇→鸟→铜钱,由第几次绘制决定,与选了哪一关无关)。
/// 视频按需求第 5 条挪到三次绘制【之后】,播完直接进打击关卡。
/// 视觉/输入统一用 LijiangEchoStageKit;视频自带(VideoPlayer + RenderTexture)。
/// </summary>
public class IntroStageController : MonoBehaviour
{
    // 整段行进的总长度(三段之和)。原为 38.85,但漂浮素材在 34.0 就放完了,末尾 4.85 一路空白;
    // 连同批次之间的空白一起压掉后,内容正好铺满 26.5。想走更久就调大(尾部会重新出现空窗)。
    [SerializeField] private float introWalkDuration = 26.5f;
    private const float PreLevelNoVideoSkip = 2.5f;   // 进入视频段 2.5s 还没开始播 → 判定坏了,跳过(短黑屏)
    private const float PreLevelSafetyCap = 60f;      // 视频段绝对上限,防异常长视频卡住
    private const int BattleStartStage = 4;           // ExternalStartStage:4 = 战斗
    private const string IntroPreLevelVideoPath = "LijiangEchoVideos/pre_level.mp4";

    private sealed class FlyItem
    {
        public SpriteRenderer Renderer;
        public Vector3 StartCenter;
        public Vector3 EndCenter;
        public float StartHeight;
        public float EndHeight;
        public float StartTime;
        public float EndTime;
        public float TargetAlpha;
        public float FloatPhase;
        public Vector3 StartRotation;
        public Vector3 EndRotation;
    }

    private Transform stageRoot;
    private readonly List<GameObject> spawnedObjects = new List<GameObject>();
    private readonly List<FlyItem> flyItems = new List<FlyItem>();

    private Transform introScrollRoot;
    private Transform introPreLevelRoot;
    private VideoPlayer introVideoPlayer;
    private RenderTexture introVideoTexture;

    private float stageTimer;
    // 反馈#1:手动往前走——推摇杆(或PC的W/↑)漂浮元素才朝相机跑来,松开停;远方静止远山不动。
    // introWalkTimer 取代过场段对 stageTimer 的直接读取;想回到自动按时间推进把 introManualWalk 设 false。
    [SerializeField] private bool introManualWalk = true;

    // 需求第 3 条:三段前进的卡点,按整段行进的比例(0~1)。走到卡点必须画完当前图案才放行。
    // 三段各自的长度还没和队友定死(需求「开发前需确认」第 1 条),先给等分默认值,可在 Inspector 里直接调。
    [SerializeField] private float[] traceGates = { 0.34f, 0.67f, 1f };

    // 勾上 = 每次绘制都双手各描一半(右手右半、左手左半,进度独立);取消 = 单手描整条。
    [SerializeField] private bool twoHandTrace = true;

    // 需求第 5 条:视频播到指定位置就切打击界面,不播多余片段。单位秒,0 = 完整播到片尾。
    // 准确的结束时间还没定(需求「开发前需确认」第 3 条),定了直接在 Inspector 里填。
    [SerializeField] private float preLevelVideoEndTime;

    // 参考视频「点击图标出现的提示demo」:走到卡点后左上角浮现一个纹样(呼吸缩放),
    // 点它才进对应的绘制界面 —— 不是走到就自动弹描绘台。位置/大小可在 Inspector 里调。
    [SerializeField] private Vector3 gateGlyphPosition = new Vector3(-1.32f, 0.58f, -0.55f);
    [SerializeField] private float gateGlyphHeight = 0.46f;
    [SerializeField] private float gateGlyphHitPadding = 0.16f;   // 点击判定比图形稍放宽,VR 里好点

    private float introWalkTimer;
    private float introVideoStartTime;               // 视频段起始的 stageTimer(手动走完时刻不定,视频计时以此为基准)
    private const float IntroWalkSpeed = 2.4f;        // 摇杆满推时的推进倍率
    private bool preLevelStarted;
    private bool preLevelFinished;
    private int selectedLevel;
    private bool done;

    private TraceStageController traceModule;         // 本场景里的描绘模块,反复 Begin/Teardown 三次
    private int traceIndex;                           // 下一次要画第几个图案(0/1/2);==traceGates.Length 表示三次都画完
    private bool tracing;                             // 正在描绘:行进段暂停

    // 卡点上的浮动纹样:等玩家点它才进描绘
    private GameObject gateGlyph;
    private readonly List<GameObject> gateGlyphObjects = new List<GameObject>();
    private readonly List<LijiangEchoStageKit.MotionItem> gateGlyphMotions = new List<LijiangEchoStageKit.MotionItem>();
    private bool awaitingGlyphClick;
    private bool glyphPointerHeldPrev;                // 上升沿检测,避免一直按住反复触发

    private IEnumerator Start()
    {
        while (LijiangEchoGameFlow.Instance == null)
        {
            yield return null;
        }

        selectedLevel = LijiangEchoGameFlow.Instance.SelectedLevel;
        stageRoot = LijiangEchoStageKit.PrepareStageRoot("漓江回声_过场舞台");
        LijiangEchoStageKit.HideControllerPointers(); // 行进段无交互:隐藏上一阶段(选关)留下的残留手柄射线
        LijiangEchoStageKit.PlayStageLoop("water", 0.3f);   // 整段(含三次绘制)保持同一条环境音,不来回切

        // 描绘模块挂在自己身上,运行时创建 —— Unity 那边不用额外拖引用,建好场景挂上本控制器即可。
        traceModule = gameObject.GetComponent<TraceStageController>();
        if (traceModule == null)
        {
            traceModule = gameObject.AddComponent<TraceStageController>();
        }

        if (traceGates == null || traceGates.Length == 0)
        {
            traceGates = new[] { 1f };                 // 兜底:Inspector 里清空了就退化成"走完画一次"
        }

        BuildIntroWalkStage();
    }

    private void Update()
    {
        if (stageRoot == null || done)
        {
            return;
        }

        stageTimer += Time.deltaTime;

        if (tracing)
        {
            return;      // 描绘进行中:行进段冻结,画面交给 TraceStageController 自己的 Update
        }

        if (!preLevelStarted)
        {
            if (awaitingGlyphClick)
            {
                UpdateGateGlyph();   // 停在卡点,纹样浮在左上角等着被点
                return;
            }

            // 本段前进的终点:走到当前卡点就停住,必须画完这一次才放行(需求第 3 条验收项)。
            float walkLimit = CurrentWalkLimit();

            // 手动往前走:推摇杆(或 W/↑)才推进,松开停;后拉可略微倒退(不低于 0)。
            if (introManualWalk)
            {
                float forward = LijiangEchoStageKit.ReadForwardAxis();
                introWalkTimer = Mathf.Clamp(
                    introWalkTimer + forward * IntroWalkSpeed * Time.deltaTime,
                    0f, walkLimit);
            }
            else
            {
                // 自动按时间推进,同样卡在卡点。用自增而不是读 stageTimer:
                // stageTimer 在描绘期间照常走,直接赋值会让画完那一刻瞬间弹到下一个卡点。
                introWalkTimer = Mathf.Min(introWalkTimer + Time.deltaTime, walkLimit);
            }

            UpdateIntroWalkStage();

            if (introWalkTimer >= walkLimit - 0.0001f)
            {
                if (traceIndex < traceGates.Length)
                {
                    ShowGateGlyph();              // 到卡点 → 左上角浮现纹样,等点击(不直接弹描绘台)
                }
                else
                {
                    StartIntroPreLevelVideo();    // 三次都画完 → 才播过渡视频(需求第 5 条)
                }
            }

            return;
        }

        UpdateIntroPreLevelStage();

        float videoElapsed = stageTimer - introVideoStartTime;
        bool videoPlaying = introVideoPlayer != null && introVideoPlayer.isPlaying;

        // 需求第 5 条:填了结束时间就播到那儿为止,不播多余片段;没填(0)则完整播完不砍断。
        bool trimmed = preLevelVideoEndTime > 0.01f
            && introVideoPlayer != null
            && introVideoPlayer.time >= preLevelVideoEndTime;

        if (preLevelFinished || trimmed)
        {
            EnterBattle();                                                   // 播到位 → 进打击关卡
        }
        else if (!videoPlaying && videoElapsed > PreLevelNoVideoSkip)
        {
            EnterBattle();                                                   // 视频没能开始播(坏/无资源)→ 短暂黑屏后跳过
        }
        else if (videoElapsed > PreLevelSafetyCap)
        {
            EnterBattle();                                                   // 极端兜底
        }
    }

    // ————————————————————————————— 三段行进 × 三次绘制 —————————————————————————————

    // 当前这一段前进能走到哪:第 traceIndex 个卡点;三次都画完了就是整段终点。
    private float CurrentWalkLimit()
    {
        if (traceIndex >= traceGates.Length)
        {
            return introWalkDuration;
        }

        return Mathf.Clamp01(traceGates[traceIndex]) * introWalkDuration;
    }

    // ————————————————————— 卡点浮动纹样(点它才进描绘) —————————————————————

    // 走到卡点 → 左上角浮现「这一次要描的那个纹样」,呼吸缩放,等玩家点。
    // 参考视频:点击图标出现的提示demo。走不过去 + 必须点 = 需求第 3 条的「完成才能继续」。
    private void ShowGateGlyph()
    {
        if (awaitingGlyphClick || !TraceStageController.TryGetPatternArt(traceIndex, out string artPath, out RectInt artCrop))
        {
            return;
        }

        gateGlyph = LijiangEchoStageKit.AddCroppedSprite(
            stageRoot, gateGlyphObjects, artPath, "卡点浮动纹样_" + traceIndex,
            artCrop, gateGlyphPosition, gateGlyphHeight, 60, 0.95f, false);
        LijiangEchoStageKit.RegisterMotion(
            gateGlyphMotions, gateGlyph, LijiangEchoStageKit.MotionKind.Pulse, 0.045f, 2.1f, 0f);

        LijiangEchoStageKit.PlaySfx("swipe", 0.4f);   // 浮现提示音
        awaitingGlyphClick = true;
        glyphPointerHeldPrev = true;                  // 首帧不算点击:防止上一段残留的按住被当成点它
    }

    // 纹样浮着的时候:刷新手柄射线,命中纹样且扳机/鼠标按下的上升沿 → 进这一次绘制。
    private void UpdateGateGlyph()
    {
        LijiangEchoStageKit.UpdateControllerInput(stageRoot);   // 这一段要交互,射线得可见可用
        LijiangEchoStageKit.UpdateMotions(gateGlyphMotions);

        bool pressedNow = false;
        if (LijiangEchoStageKit.TryGetActivePointer(stageRoot, out Vector3 point, out bool held))
        {
            float halfW = gateGlyphHeight * 0.5f + gateGlyphHitPadding;
            float halfH = gateGlyphHeight * 0.5f + gateGlyphHitPadding;
            bool inside = Mathf.Abs(point.x - gateGlyphPosition.x) <= halfW
                       && Mathf.Abs(point.y - gateGlyphPosition.y) <= halfH;
            pressedNow = inside && held && !glyphPointerHeldPrev;
            glyphPointerHeldPrev = held;
        }
        else
        {
            glyphPointerHeldPrev = false;
        }

        if (pressedNow)
        {
            HideGateGlyph();
            BeginTraceSegment();
        }
    }

    private void HideGateGlyph()
    {
        awaitingGlyphClick = false;
        gateGlyphMotions.Clear();
        for (int i = 0; i < gateGlyphObjects.Count; i++)
        {
            if (gateGlyphObjects[i] != null)
            {
                Destroy(gateGlyphObjects[i]);
            }
        }

        gateGlyphObjects.Clear();
        gateGlyph = null;
    }

    // 点了纹样 → 拉起这一次绘制。行进画面收起,手柄射线交给描绘模块。
    private void BeginTraceSegment()
    {
        tracing = true;
        if (introScrollRoot != null)
        {
            introScrollRoot.gameObject.SetActive(false);
        }

        traceModule.Begin(traceIndex, twoHandTrace, OnTraceSegmentDone);
    }

    // 这一次画完 → 收起描绘台,行进画面回来,继续走下一段(第三次画完则由 Update 去播视频)。
    private void OnTraceSegmentDone()
    {
        traceModule.Teardown();
        traceIndex++;
        tracing = false;

        if (introScrollRoot != null)
        {
            introScrollRoot.gameObject.SetActive(true);
        }

        LijiangEchoStageKit.HideControllerPointers();   // 行进段无交互,收掉描绘留下的射线
    }

    // 整段结束 → 进旧版流程,并让旧主场景从战斗阶段开始(战斗尚未拆出去时的桥接)。
    private void EnterBattle()
    {
        done = true;
        ReleaseIntroVideo();
        LijiangEchoGameController.ExternalStartStage = BattleStartStage;
        LijiangEchoGameFlow.Instance.EnterLegacyFlow(selectedLevel);
    }

    // ————————————————————————————— 悬浮过场 —————————————————————————————

    private void BuildIntroWalkStage()
    {
        GameObject scrollRootObject = new GameObject("过场漂浮素材");
        introScrollRoot = scrollRootObject.transform;
        introScrollRoot.SetParent(stageRoot, false);
        introScrollRoot.localPosition = Vector3.zero;
        introScrollRoot.localRotation = Quaternion.identity;
        introScrollRoot.localScale = Vector3.one;
        spawnedObjects.Add(scrollRootObject);

        // 远方地平线一排小远山(静止,不随漂浮素材横移)。
        const float horizonY = 0.42f;    // 0.30→0.42:静止远山这排再稍微往上一点(与旧控制器同步)
        const float mtnHeight = 0.025f;
        float mtnCenterY = horizonY + mtnHeight * 0.5f;
        string[] horizonMtnArt =
        {
            "start/back_mountain_1", "start/back_mountain_2", "start/back_mountain_3",
            "start/front_mountain_left", "start/front_mountain_right"
        };
        const float horizonHalfSpan = 2.1f;
        const float horizonStep = 0.14f;
        const float horizonRowZ = 3.0f;  // 静止远山这一排的深度(越大越远,约放到 4 米开外)。想更远/更近改这个(原 0.44)
        int horizonCount = Mathf.CeilToInt((horizonHalfSpan * 2f) / horizonStep) + 1;
        for (int m = 0; m < horizonCount; m++)
        {
            float hx = -horizonHalfSpan + m * horizonStep;
            LijiangEchoStageKit.AddIcon(stageRoot, spawnedObjects, horizonMtnArt[m % horizonMtnArt.Length],
                "地平线小远山_" + m, new Vector3(hx, mtnCenterY, horizonRowZ), mtnHeight, -50 + (m % 5), 0.85f);
        }

        LijiangEchoStageKit.AddLayer(stageRoot, spawnedObjects, "ui/mountain_background", "地平线天幕",
            new Vector3(0f, horizonY - 0.04f, horizonRowZ + 0.2f), LijiangEchoStageKit.WideStripWidth, -52, 0.45f);

        AddFly("transition/mountain_1", "近景山一", new RectInt(127, 197, 490, 260), new Vector3(-3.25f, -0.18f, -0.16f), new Vector3(3.15f, -0.05f, -0.16f), 0.42f, 0.78f, 0.0f, 5.8f, 12, 0.88f);
        AddFly("transition/mountain_4", "近景山二", new RectInt(1390, 219, 373, 197), new Vector3(3.20f, -0.34f, -0.18f), new Vector3(-3.10f, -0.20f, -0.18f), 0.38f, 0.74f, 0.3f, 6.1f, 13, 0.84f);
        AddFly("transition/terrace", "漂浮梯田", new RectInt(507, 314, 451, 139), new Vector3(-3.0f, -0.60f, -0.22f), new Vector3(3.1f, -0.46f, -0.22f), 0.24f, 0.46f, 0.8f, 6.9f, 16, 0.92f);
        AddFly("transition/house_1", "漂浮房屋一", new RectInt(749, 289, 217, 162), new Vector3(3.05f, 0.20f, -0.24f), new Vector3(-3.05f, 0.02f, -0.24f), 0.28f, 0.56f, 1.1f, 7.0f, 20, 0.94f);
        AddFly("transition/house_3", "漂浮房屋二", new RectInt(1416, 274, 217, 162), new Vector3(-3.15f, 0.34f, -0.25f), new Vector3(3.0f, 0.18f, -0.25f), 0.25f, 0.52f, 1.7f, 7.5f, 21, 0.92f);
        AddFly("transition/moon", "漂浮月亮", new RectInt(796, 177, 73, 59), new Vector3(-2.8f, 0.74f, -0.27f), new Vector3(2.9f, 0.57f, -0.27f), 0.16f, 0.32f, 1.8f, 7.8f, 22, 0.95f);
        AddFly("transition/animal_1", "漂浮动物一", new RectInt(600, 344, 198, 110), new Vector3(3.15f, -0.10f, -0.30f), new Vector3(-3.0f, 0.12f, -0.30f), 0.25f, 0.48f, 2.2f, 8.1f, 28, 0.95f);
        AddFly("transition/animal_3", "漂浮动物二", new RectInt(1101, 375, 213, 71), new Vector3(-3.1f, 0.08f, -0.31f), new Vector3(3.15f, -0.02f, -0.31f), 0.18f, 0.37f, 2.8f, 8.5f, 29, 0.96f);
        AddFly("transition/animal_4", "漂浮动物三", new RectInt(1420, 346, 164, 90), new Vector3(3.2f, 0.42f, -0.32f), new Vector3(-3.05f, 0.24f, -0.32f), 0.20f, 0.40f, 3.4f, 8.7f, 30, 0.94f);
        AddFly("transition/person_1", "漂浮人物一", new RectInt(941, 321, 61, 111), new Vector3(-2.9f, -0.20f, -0.33f), new Vector3(3.05f, -0.05f, -0.33f), 0.21f, 0.43f, 3.6f, 8.9f, 31, 0.92f);

        AddFly("transition/water", "漂浮水纹", new RectInt(1696, 375, 1333, 74), new Vector3(-3.5f, -0.58f, -0.20f), new Vector3(3.4f, -0.43f, -0.20f), 0.14f, 0.30f, 13.2f, 18.7f, 15, 0.78f);
        AddFly("transition/house_2", "漂浮房屋三", new RectInt(1281, 310, 135, 125), new Vector3(3.0f, 0.16f, -0.25f), new Vector3(-3.05f, 0.30f, -0.25f), 0.24f, 0.50f, 13.4f, 18.9f, 23, 0.93f);
        AddFly("transition/house_4", "漂浮房屋四", new RectInt(1948, 295, 135, 125), new Vector3(-3.05f, 0.38f, -0.26f), new Vector3(3.0f, 0.17f, -0.26f), 0.23f, 0.48f, 13.7f, 19.2f, 24, 0.92f);
        AddFly("transition/animal_2", "漂浮动物四", new RectInt(912, 388, 127, 62), new Vector3(3.1f, -0.08f, -0.31f), new Vector3(-3.05f, 0.04f, -0.31f), 0.18f, 0.37f, 14.0f, 19.0f, 30, 0.94f);
        AddFly("transition/animal_5", "漂浮动物五", new RectInt(1718, 328, 164, 98), new Vector3(-3.1f, 0.32f, -0.32f), new Vector3(3.0f, 0.14f, -0.32f), 0.21f, 0.44f, 14.4f, 19.4f, 31, 0.95f);
        AddFly("transition/person_2", "漂浮人物二", new RectInt(1009, 338, 72, 95), new Vector3(3.0f, -0.18f, -0.33f), new Vector3(-3.0f, -0.02f, -0.33f), 0.21f, 0.43f, 14.8f, 19.6f, 32, 0.92f);
        AddFly("transition/person_3", "漂浮人物三", new RectInt(1580, 332, 83, 98), new Vector3(-3.0f, 0.10f, -0.34f), new Vector3(3.0f, -0.05f, -0.34f), 0.20f, 0.42f, 15.2f, 19.8f, 33, 0.93f);

        AddFly("transition/mountain_2", "远山一", new RectInt(444, 234, 387, 202), new Vector3(-3.15f, -0.12f, -0.16f), new Vector3(3.05f, -0.26f, -0.16f), 0.35f, 0.72f, 23.4f, 30.2f, 12, 0.86f);
        AddFly("transition/mountain_3", "远山二", new RectInt(906, 195, 460, 248), new Vector3(3.2f, -0.30f, -0.18f), new Vector3(-3.05f, -0.10f, -0.18f), 0.42f, 0.82f, 23.8f, 30.7f, 13, 0.88f);
        AddFly("transition/mountain_5", "远山三", new RectInt(1890, 213, 428, 208), new Vector3(-3.25f, 0.02f, -0.20f), new Vector3(3.1f, -0.20f, -0.20f), 0.36f, 0.72f, 24.5f, 31.4f, 14, 0.84f);
        AddFly("transition/mountain_6", "远山四", new RectInt(2297, 296, 394, 130), new Vector3(3.15f, 0.28f, -0.22f), new Vector3(-3.1f, 0.08f, -0.22f), 0.24f, 0.49f, 25.2f, 32.0f, 15, 0.86f);
        AddFly("transition/mountain_7", "远山五", new RectInt(2676, 206, 348, 219), new Vector3(-3.1f, -0.34f, -0.24f), new Vector3(3.1f, -0.12f, -0.24f), 0.38f, 0.78f, 26.0f, 32.8f, 16, 0.90f);
        AddFly("transition/animal_6", "漂浮鱼群", new RectInt(2210, 367, 220, 38), new Vector3(3.2f, 0.48f, -0.31f), new Vector3(-3.1f, 0.20f, -0.31f), 0.13f, 0.27f, 26.4f, 33.1f, 30, 0.95f);
        AddFly("transition/person_4", "漂浮人物四", new RectInt(1935, 355, 54, 69), new Vector3(-3.0f, -0.18f, -0.32f), new Vector3(3.0f, 0.04f, -0.32f), 0.17f, 0.35f, 27.0f, 33.4f, 31, 0.92f);
        AddFly("transition/beast", "迎面兽纹", new RectInt(2642, 45, 475, 391), new Vector3(3.25f, 0.08f, -0.36f), new Vector3(-3.1f, -0.02f, -0.36f), 0.42f, 1.05f, 27.3f, 34.0f, 40, 0.98f);

        // 过场边框(静态,保持各自 alpha —— 旧版这两项虽登记了淡入表但并未被动画驱动)。
        LijiangEchoStageKit.AddLayer(stageRoot, spawnedObjects, "transition/hollow_frame", "过场镂空边框",
            Vector3.zero, LijiangEchoStageKit.MainCanvasWidth, 90, 0.76f, introScrollRoot);
        LijiangEchoStageKit.AddLayer(stageRoot, spawnedObjects, "transition/purple_frame", "过场紫色边框",
            new Vector3(0f, 0f, -0.46f), LijiangEchoStageKit.MainCanvasWidth, 91, 0.34f, introScrollRoot);

        UpdateIntroWalkStage();
    }

    // 用户反馈:三批漂浮素材之间原本各有 4.3 / 3.6 的空窗,末尾还有 4.85 什么都不飘,
    // 走很久看不到东西会让人以为走错了。把第二、三批整体前移,压掉批次之间和末尾的空白。
    // 下面这两个偏移只挪批次起点,批内各素材的相对错落原样保留(美术节奏不变)。
    private const float FlyWave2Shift = 4.0f;    // 第二批(原 13.2 起)前移 → 9.2 起,紧接第一批
    private const float FlyWave3Shift = 7.5f;    // 第三批(原 23.4 起)前移 → 15.9 起,紧接第二批

    private static float CompressFlyTime(float time)
    {
        if (time >= 23.0f)
        {
            return time - FlyWave3Shift;
        }

        if (time >= 13.0f)
        {
            return time - FlyWave2Shift;
        }

        return time;                              // 第一批(0 ~ 8.9)原样
    }

    private void AddFly(string resourcePath, string objectName, RectInt topLeftCrop,
        Vector3 startCenter, Vector3 endCenter, float startHeight, float endHeight,
        float startTime, float endTime, int order, float alpha)
    {
        startTime = CompressFlyTime(startTime);
        endTime = CompressFlyTime(endTime);

        int spatialIndex = flyItems.Count;
        float startDepth = 5.6f + (spatialIndex % 4) * 0.85f;
        float endDepth = -4.1f - (spatialIndex % 3) * 0.55f;
        Vector3 spatialStart = new Vector3(startCenter.x * 0.12f, startCenter.y * 0.42f, startDepth);
        Vector3 spatialEnd = new Vector3(endCenter.x * 0.74f, endCenter.y * 1.08f, endDepth);
        float direction = Mathf.Sign(endCenter.x - startCenter.x);
        if (Mathf.Approximately(direction, 0f))
        {
            direction = spatialIndex % 2 == 0 ? 1f : -1f;
        }

        GameObject itemObject = LijiangEchoStageKit.AddCroppedSprite(stageRoot, spawnedObjects,
            resourcePath, objectName, topLeftCrop, spatialStart, startHeight, order, 0f, false, introScrollRoot);

        flyItems.Add(new FlyItem
        {
            Renderer = itemObject.GetComponent<SpriteRenderer>(),
            StartCenter = spatialStart,
            EndCenter = spatialEnd,
            StartHeight = startHeight,
            EndHeight = endHeight,
            StartTime = startTime,
            EndTime = endTime,
            TargetAlpha = alpha,
            FloatPhase = spatialIndex * 0.73f,
            StartRotation = new Vector3(0f, -direction * 3f, -direction * 2f),
            EndRotation = new Vector3(0f, direction * 18f, direction * (10f + spatialIndex % 3 * 4f))
        });
    }

    private void UpdateIntroWalkStage()
    {
        foreach (FlyItem item in flyItems)
        {
            if (item.Renderer == null)
            {
                continue;
            }

            float progress = Mathf.Clamp01(Mathf.InverseLerp(item.StartTime, item.EndTime, introWalkTimer));
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(item.StartTime - 0.18f, item.StartTime + 0.48f, introWalkTimer));
            float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(item.EndTime - 0.55f, item.EndTime + 0.18f, introWalkTimer));
            float alpha = item.TargetAlpha * Mathf.Min(fadeIn, fadeOut);

            Vector3 center = Vector3.Lerp(item.StartCenter, item.EndCenter, eased);
            center.y += Mathf.Sin(Time.time * 1.55f + item.FloatPhase) * 0.035f;
            float height = Mathf.Lerp(item.StartHeight, item.EndHeight, eased);
            LijiangEchoStageKit.SetCroppedSpritePose(item.Renderer, center, height, alpha, false);
            item.Renderer.transform.localRotation = Quaternion.Euler(Vector3.Lerp(item.StartRotation, item.EndRotation, eased));
        }
    }

    // ————————————————————————————— 入关视频 —————————————————————————————

    private void StartIntroPreLevelVideo()
    {
        preLevelStarted = true;
        preLevelFinished = false;
        introVideoStartTime = stageTimer;

        if (introScrollRoot != null)
        {
            introScrollRoot.gameObject.SetActive(false);
        }

        introPreLevelRoot = new GameObject("关卡前播放动画").transform;
        introPreLevelRoot.SetParent(stageRoot, false);
        introPreLevelRoot.localPosition = Vector3.zero;
        introPreLevelRoot.localRotation = Quaternion.identity;
        introPreLevelRoot.localScale = Vector3.one;
        spawnedObjects.Add(introPreLevelRoot.gameObject);

        GameObject blackBackdrop = LijiangEchoStageKit.AddSolidRect(stageRoot, spawnedObjects, "关卡前动画黑底",
            new Vector3(0f, 0f, -0.68f), LijiangEchoStageKit.MainCanvasWidth, 1.55f, Color.black, 98);
        blackBackdrop.transform.SetParent(introPreLevelRoot, false);
        blackBackdrop.transform.localPosition = new Vector3(0f, 0f, -0.68f);

        AddVideoLayer("关卡前播放动画视频", IntroPreLevelVideoPath, new Vector3(0f, 0f, -0.72f), 3.82f, 100, introPreLevelRoot);
    }

    private void UpdateIntroPreLevelStage()
    {
        if (introScrollRoot != null && introScrollRoot.gameObject.activeSelf)
        {
            introScrollRoot.gameObject.SetActive(false);
        }
    }

    private void AddVideoLayer(string objectName, string videoPath, Vector3 localPosition, float targetWidth, int order, Transform parent)
    {
        GameObject videoObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
        videoObject.name = objectName;
        videoObject.transform.SetParent(parent != null ? parent : stageRoot, false);
        videoObject.transform.localPosition = localPosition;
        videoObject.transform.localRotation = Quaternion.identity;
        videoObject.transform.localScale = new Vector3(targetWidth, targetWidth * 256f / 1260f, 1f);

        Collider collider = videoObject.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }

        introVideoTexture = new RenderTexture(1260, 256, 0, RenderTextureFormat.ARGB32);
        introVideoTexture.Create();

        Shader shader = Shader.Find("Unlit/Texture");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        MeshRenderer renderer = videoObject.GetComponent<MeshRenderer>();
        renderer.sortingOrder = order;
        renderer.sharedMaterial = new Material(shader);
        renderer.sharedMaterial.mainTexture = introVideoTexture;

        introVideoPlayer = videoObject.AddComponent<VideoPlayer>();
        introVideoPlayer.playOnAwake = false;
        introVideoPlayer.renderMode = VideoRenderMode.RenderTexture;
        introVideoPlayer.targetTexture = introVideoTexture;
        introVideoPlayer.audioOutputMode = VideoAudioOutputMode.Direct;
        introVideoPlayer.isLooping = false;
        introVideoPlayer.waitForFirstFrame = true;
        introVideoPlayer.loopPointReached += HandleIntroVideoEnded;

        string url = Application.streamingAssetsPath + "/" + videoPath;
        introVideoPlayer.url = url.Replace("\\", "/");
        introVideoPlayer.Stop();
        introVideoPlayer.Play();

        spawnedObjects.Add(videoObject);
    }

    private void HandleIntroVideoEnded(VideoPlayer player)
    {
        preLevelFinished = true;
    }

    private void ReleaseIntroVideo()
    {
        if (introVideoPlayer != null)
        {
            introVideoPlayer.loopPointReached -= HandleIntroVideoEnded;
            introVideoPlayer.Stop();
        }

        if (introVideoTexture != null)
        {
            introVideoTexture.Release();
            Destroy(introVideoTexture);
            introVideoTexture = null;
        }

        introVideoPlayer = null;
    }
}
