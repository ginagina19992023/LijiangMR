using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.Video;
using UnityEngine.XR;

/// <summary>
/// 漓江回声 MR 的运行时关卡控制器。
/// 不需要手动拖拽到场景里，播放后会自动在玩家前方搭出开始、选关、过场、打击和卡片流程。
/// </summary>
public class LijiangEchoGameController : MonoBehaviour
{
    private enum Stage
    {
        Start,
        Select,
        Intro,
        Trace,
        Battle,
        Card,
        Result
    }

    private enum MotionKind
    {
        FloatY,
        FloatX,
        Pulse,
        Flame,
        Monster,
        Wing,
        Hand
    }

    private enum NoteKind
    {
        Strike,
        Hold,
        Swipe,
        Double
    }

    private sealed class MotionItem
    {
        public Transform Transform;
        public SpriteRenderer Renderer;
        public Vector3 BasePosition;
        public Vector3 BaseScale;
        public Quaternion BaseRotation;
        public Color BaseColor;
        public MotionKind Kind;
        public float Speed;
        public float Amplitude;
        public float Phase;
    }

    private sealed class RhythmNote
    {
        public int ChartIndex;
        public float HitTime;
        public float StartX;
        public float TargetX;
        public float Side;
        public float TargetHeight;
        public NoteKind Kind;
        public SpriteRenderer Renderer;
        public SpriteRenderer[] GlowLayers;   // 多层加色柔光,越外层越淡
        public float[] GlowBaseAlpha;         // 每层基础亮度
        public bool Judged;
        public bool Cued;                     // 是否已在判定点播过类型音效(避免重复)

        // —— Prefab 音符(可编辑纹样 Prefab):视觉完全由 Prefab 决定,运行时只驱动根位置 + 淡入 ——
        public Transform PrefabRoot;              // 非空 = 这是 Prefab 音符
        public SpriteRenderer[] AllRenderers;     // Prefab 里所有精灵(本体+光晕),用于统一淡入
        public float[] AllRenderersBaseAlpha;     // 各精灵在 Prefab 里的基础透明度(保持相对关系)

        // —— 双击「镜像汇合」分身(仅 doubleNoteMirrorConverge=true 时存在):原体(鸟纹=右翼)从右飞入,
        //    另生成一只水平镜像(=左翼)从左飞入,两只对称汇合到圆心。分身纯视觉、不参与判定,
        //    随本体一起淡入、一起销毁。 ——
        public Transform MirrorTwin;
        public SpriteRenderer[] MirrorTwinRenderers;
        public float[] MirrorTwinBaseAlpha;
    }

    private sealed class IntroFadeItem
    {
        public SpriteRenderer Renderer;
        public float TargetAlpha;
    }

    private sealed class IntroFlyItem
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

    private sealed class IntroFocusItem
    {
        public SpriteRenderer PanelRenderer;
        public TextMesh Caption;
        public float StartTime;
        public float EndTime;
    }

    private const string ArtRoot = "LijiangEchoArt/";
    private const float PixelsPerUnit = 520f;
    private const float MainCanvasWidth = 5.65f;
    private const float WideStripWidth = 6.05f;
    private const float IntroWalkDuration = 38.85f;
    private const float IntroTotalDuration = 57f;
    private const float NoteApproachTime = 1.22f;
    // 用户反馈:纹样音符整体做小一些的缩放系数。
    private const float NoteSizeScale = 0.72f;

    // 打击纹样材质:白色剪影 + 加色柔光。运行时按 shader 名创建、全体音符共享(颜色靠 renderer.color 传)。
    private Material noteWhiteMaterial;
    private Material noteGlowMaterial;

    private void EnsureNoteMaterials()
    {
        if (noteWhiteMaterial == null)
        {
            Shader s = Shader.Find("LijiangEcho/WhiteSilhouette");
            if (s != null)
            {
                noteWhiteMaterial = new Material(s) { name = "白色纹样(运行时)" };
            }
        }

        if (noteGlowMaterial == null)
        {
            Shader g = Shader.Find("LijiangEcho/SoftGlowAdd");
            if (g != null)
            {
                noteGlowMaterial = new Material(g) { name = "柔光光晕(运行时)" };
            }
        }
    }
    private const float HitRingVisibleHeight = 0.62f;
    private const float HitBlockVisibleHeight = 0.50f; // 单击鱼纹方框边长(fitByMaxDimension:较大边=此值)
    private const float HitRingTargetX = HitRingVisibleHeight * 0.5f;
    private const float StageDistance = 2.35f;
    private const float StageWorldScale = 0.78f;
    private const float TracePlaneZ = -0.72f;
    private const float TracePointTolerance = 0.105f;
    private static readonly RectInt HitBlockCrop = new RectInt(833, 728, 585, 1120);
    private static readonly RectInt FrogSwipeCrop = new RectInt(469, 133, 724, 625);
    private static readonly RectInt SnakeDoneCrop = new RectInt(1258, 2332, 948, 2510);
    private static readonly RectInt BirdDoneCrop = new RectInt(3289, 2141, 1488, 2151);
    private static readonly RectInt CoinDoneCrop = new RectInt(1629, 836, 701, 1359);

    private static LijiangEchoGameController instance;

    /// <summary>
    /// 由 LijiangEchoGameFlow 在桥接进入本场景前设置：跳过开始/选关（已迁移到独立场景），
    /// 直接从过场动画开始。为 null 时保持旧行为，方便独立打开本场景做调试。
    /// </summary>
    public static int? ExternalSelectedLevel;

    // 双手镜像绘制开关:null/true = 左右对称双手画(把描绘镜像到对侧);false = 单手画全程。
    public static bool? ExternalTraceMirror;

    // 战斗背景场景化(Path B 第 1 步):进战斗时若某个已加载场景里存在烘焙的战斗背景根
    // (以子节点「怪物分层」为标志),就采用它(挂到 stageRoot 下 + 驱动其 LijiangEchoMotion 动效),
    // 跳过运行时构建;其余玩法照旧在其上构建。ExternalBattleSceneName 指定要附加加载的战斗场景
    // (需加入 Build Settings);留空则用默认名。
    public static string ExternalBattleSceneName;

    // 供谱面编辑器时间轴"跟随游戏进度":战斗进行中=当前 beatTime(秒),不在战斗=-1。
    public static float EditorBattleTime = -1f;
    private const string DefaultBattleSceneName = "Battle_level1";
    private const string BattleBackgroundMarkerName = "怪物分层";
    private Transform adoptedBattleRoot; // 采用的烘焙背景根;不加入 spawnedObjects,跨重入保留

    private readonly List<GameObject> spawnedObjects = new List<GameObject>();
    private readonly List<GameObject> menuObjects = new List<GameObject>();
    private readonly List<MotionItem> motionItems = new List<MotionItem>();
    private readonly List<RhythmNote> activeNotes = new List<RhythmNote>();
    private readonly List<IntroFadeItem> introWalkItems = new List<IntroFadeItem>();
    private readonly List<IntroFadeItem> introPreLevelItems = new List<IntroFadeItem>();
    private readonly List<IntroFlyItem> introFlyItems = new List<IntroFlyItem>();
    private readonly List<IntroFocusItem> introFocusItems = new List<IntroFocusItem>();
    private readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    private readonly Dictionary<string, Texture2D> solidTextureCache = new Dictionary<string, Texture2D>();
    // 每个精灵"不透明像素真实中心"相对 pivot 的偏移(局部单位),缓存;贴图不可读时回退到 bounds.center。
    private readonly Dictionary<Sprite, Vector3> visibleCenterCache = new Dictionary<Sprite, Vector3>();
    private readonly Dictionary<string, AudioClip> audioCache = new Dictionary<string, AudioClip>();

    private Transform cameraAnchor;
    private Transform stageRoot;
    private Transform leftControllerAnchor;
    private Transform rightControllerAnchor;
    private LineRenderer leftControllerRay;
    private LineRenderer rightControllerRay;
    private Transform leftControllerReticle;
    private Transform rightControllerReticle;
    private bool leftControllerTracked;
    private bool rightControllerTracked;
    private float leftTriggerValue;
    private float rightTriggerValue;
    private float previousLeftTriggerValue;
    private float previousRightTriggerValue;
    private bool leftTriggerDown;
    private bool rightTriggerDown;
    private bool previousLeftGripPressed;
    private bool previousRightGripPressed;
    private bool previousLeftFacePressed;
    private bool previousRightFacePressed;
    private bool battleControllerButtonDown;
    private bool battleControllerButtonHeld;
    private bool headPoseWasTracked;
    private Font uiFont;
    private bool experienceReady;
    private bool stageAnchored;

    private Stage currentStage;
    private int selectedLevel;
    private float stageTimer;
    private float selectMoveCooldown;
    private float hitFlashTimer;
    private bool introPreLevelStarted;
    private bool introPreLevelFinished;
    private bool traceCompleted;
    private float traceCompleteTimer;
    private int tracePointIndex;
    private Vector3[] tracePoints;
    private LineRenderer traceMirrorDrawRenderer;
    private Transform traceMirrorPointer;
    private Transform leftHandPivot;
    private Transform rightHandPivot;
    private SpriteRenderer leftHandRenderer;
    private SpriteRenderer rightHandRenderer;
    private float leftHandStrikeTimer;
    private float rightHandStrikeTimer;
    private LineRenderer traceDrawRenderer;
    private Transform tracePointer;
    private TextMesh traceFeedbackText;
    private Vector3 previousTracePointer;
    private bool hasPreviousTracePointer;
    // 真·双手独立描绘:左手描左半,右手描右半,各自进度、各自判定,两半都完成才算成功。
    private bool traceTwoHands;
    private Vector3[] traceLeftPoints;
    private int traceLeftIndex;
    private Vector3 previousTraceLeftPointer;
    private bool hasPreviousTraceLeftPointer;

    private Vector3 lastLeftControllerPosition;
    private Vector3 lastRightControllerPosition;
    private Vector3 leftControllerVelocity;
    private Vector3 rightControllerVelocity;
    private bool controllerMotionReady;
    private float swipeCooldown;

    private TextMesh hintText;
    private TextMesh scoreText;
    private TextMesh feedbackText;
    private SpriteRenderer startButtonPanelRenderer;
    private SpriteRenderer startButtonRenderer;

    private SpriteRenderer[] selectCards;
    private SpriteRenderer[] selectNumbers;
    private SpriteRenderer patternRenderer;
    private SpriteRenderer ringRenderer;
    private SpriteRenderer progressFillRenderer;
    private SpriteRenderer countdownRenderer;
    private SpriteRenderer cardPageRenderer;
    private Transform ringTransform;
    private LijiangEchoRingFeedback ringFeedback; // 圆环反馈脚本(挂在圆环上;没挂则补默认,观感同旧版)
    private Transform monsterRoot;
    private Transform introScrollRoot;
    private Transform introPreLevelRoot;
    private VideoPlayer introVideoPlayer;
    private RenderTexture introVideoTexture;
    private AudioSource ambienceSource;
    private AudioSource battleMusicSource;
    private AudioSource sfxSource;

    // 延时音效队列(用于"双击=两声"这类需要间隔发声的情况)
    private struct ScheduledSfx { public float Due; public string Clip; public float Volume; }
    private readonly List<ScheduledSfx> scheduledSfx = new List<ScheduledSfx>();
    private bool battleMusicStarted;
    private float battleMusicTime;
    private float battleSeekTime = -1f; // >=0 时战斗从该秒起播(编辑器"从播放头试玩");跳过倒计时
    private float battleEndingTimer;
    private bool holdActive;
    private float holdProgress;
    private RhythmNote heldNote;
    private Vector3 ringBaseScale = Vector3.one;

    private readonly string[] levelNames = { "蛙纹", "鸟纹", "鱼纹" };
    private readonly string[] levelCardPaths =
    {
        "select/frog_card",
        "select/bird_card",
        "select/fish_card"
    };

    private readonly string[] levelSymbolPaths =
    {
        "select/frog_symbol",
        "select/bird_symbol",
        "select/fish_symbol"
    };

    private readonly string[] levelNumberPaths =
    {
        "ui/number_1",
        "ui/number_2",
        "ui/number_3"
    };

    private readonly string[] tracePaths =
    {
        "pattern/snake_trace",
        "pattern/bird_trace",
        "pattern/coin_trace"
    };

    private readonly string[] donePaths =
    {
        "pattern/snake_done",
        "pattern/bird_done",
        "pattern/coin_done"
    };

    private readonly string[] infoCardPaths =
    {
        "cards/frog_info",
        "cards/bird_info",
        "cards/fish_info"
    };

    private readonly string[] cardPagePaths =
    {
        "cards/frog_info",
        "cards/bird_info",
        "cards/fish_info",
        "cards/snake_info",
        "cards/coin_info",
        "cards/boss_info",
        "cards/boss_info_1",
        "cards/animal_info",
        "cards/flying_head_info",
        "cards/worker_info",
        "cards/dragon_info",
        "cards/horse_info",
        "cards/pig_info",
        "cards/forehead_info",
        "cards/fang_info"
    };

    private readonly string[] introFocusPaths =
    {
        "transition/snake",
        "transition/beast",
        "transition/coin"
    };

    private const string IntroPreLevelVideoPath = "LijiangEchoVideos/pre_level.mp4";

    private readonly Vector3[] selectNumberPositions =
    {
        new Vector3(-1.33f, -0.46f, -0.18f),
        new Vector3(0f, -0.46f, -0.18f),
        new Vector3(1.33f, -0.46f, -0.18f)
    };

    // 依据指定战斗音乐的瞬态峰值生成，覆盖整首 105.3 秒音轨。
    private float[] noteTimes =
    {
        8.336f, 20.898f, 21.478f, 21.873f, 22.454f, 23.034f, 23.615f,
        24.009f, 24.381f, 25.542f, 27.098f, 27.492f, 27.887f, 28.259f,
        28.653f, 29.025f, 29.420f, 30.186f, 30.581f, 30.975f, 31.742f,
        32.136f, 33.297f, 33.669f, 34.458f, 34.830f, 35.225f, 35.619f,
        36.386f, 36.780f, 38.708f, 39.474f, 40.263f, 41.030f, 41.424f,
        41.796f, 42.585f, 43.352f, 43.746f, 44.118f, 44.513f, 44.907f,
        45.302f, 47.229f, 47.601f, 47.996f, 48.390f, 50.318f, 50.712f,
        51.107f, 51.479f, 51.873f, 53.429f, 53.801f, 54.195f, 54.590f,
        56.517f, 56.889f, 58.654f, 59.606f, 60.767f, 61.161f, 61.556f,
        61.951f, 62.322f, 62.717f, 64.250f, 64.644f, 65.039f, 65.411f,
        65.805f, 67.361f, 67.733f, 68.151f, 68.894f, 70.449f, 70.844f,
        71.239f, 72.005f, 72.377f, 72.771f, 73.538f, 73.932f, 74.699f,
        75.093f, 76.649f, 77.044f, 77.415f, 77.810f, 78.182f, 78.576f,
        78.971f, 79.737f, 80.132f, 81.293f, 81.688f, 82.059f, 83.615f,
        85.496f, 87.052f, 87.423f, 87.818f, 88.213f, 90.906f, 91.696f,
        94.018f, 98.267f, 102.911f
    };

    private HashSet<int> holdNoteIndices = new HashSet<int>
    {
        0, 9, 29, 42, 51, 57, 65, 74, 84, 97, 102, 105
    };

    // P4/P6：双击音符的谱面 index。
    // 来源：早期需求文件《刘三姐音乐游戏内容需求》的音符编排——双击(蛙类)出现在
    // 19/41/42/45/49/50/53/64/82/85/89/90/96 秒。把这些秒数映射到本代码 noteTimes
    // 里最接近的音符 index 得到下表。⚠️ 注意：本代码的 noteTimes 是按音频峰值自动生成的
    // 108 个音符，与需求文件手工编排的 ~33 个音符并非一一对应，故为近似映射：中段偏差
    // <0.6s，首尾个别偏 1~2.3s（末尾音符稀疏所致）。若要与需求完全一致，需重建整张谱面。
    // 双击音符当前用 pattern/bird_done(鸟纹)与单击区分；需求原意为蛙纹，可在 SpawnDueNotes
    // 的 Double 分支一行替换。输入仍按单击命中处理，未加"快速点两下"判定。
    private HashSet<int> doubleNoteIndices = new HashSet<int>
    {
        1, 33, 35, 41, 46, 47, 52, 66, 96, 98, 101, 103, 106
    };

    // 编辑器谱面显式标注的挥划(swipe)音符 index。仅当谱面带「# types:explicit」头时启用,
    // 此时关掉下面 GetNoteKind 的取模自动 swipe,做到"编辑器所见即所得"。
    private HashSet<int> swipeNoteIndices = new HashSet<int>();
    // 谱面是否用「显式类型」(编辑器保存的谱面带 # types:explicit 头)。true 时只认显式类型、不再取模生成 swipe。
    private bool chartTypesExplicit;

    private int nextSpawnIndex;
    private int nextNoteIndex;
    private int cardPageIndex;
    private int score;
    private int combo;
    private float feedbackTimer;

    // 连击≥5 的黄色荡漾光环
    private sealed class ComboRipple { public Transform tr; public SpriteRenderer sr; public float age; public float life; }
    private readonly List<ComboRipple> comboRipples = new List<ComboRipple>();
    private float comboRippleTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureController()
    {
        // 进程启动只触发一次:订阅场景加载事件,保证旧主场景【每次】被(附加)加载后都会尝试创建控制器。
        // 关键修复:旧主场景是运行时【附加加载】的,而本方法只在进程启动那一次触发、彼时主场景还没加载
        // → 之前会直接 return,控制器永远建不出来 → 进旧主场景后无人驱动、画面卡死(不崩、仍 72fps)。
        SceneManager.sceneLoaded -= HandleSceneLoadedForController;
        SceneManager.sceneLoaded += HandleSceneLoadedForController;
        TryCreateRuntimeController(); // 若启动时旧主场景已加载(单独打开该场景测试),立即创建
    }

    private static void HandleSceneLoadedForController(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == "LijiangEchoMR_Main")
        {
            TryCreateRuntimeController();
        }
    }

    private static void TryCreateRuntimeController()
    {
        // 开始/选关已拆到独立场景（Stage_Start/Stage_Select），本控制器只在旧主场景加载后才自动生成，
        // 避免在 Bootstrap/新阶段场景里重复搭建一套内容。
        if (!SceneManager.GetSceneByName("LijiangEchoMR_Main").isLoaded)
        {
            return;
        }

        if (FindFirstObjectByType<LijiangEchoGameController>() != null)
        {
            return;
        }

        GameObject controllerObject = new GameObject("漓江回声_运行时关卡控制器");
        controllerObject.AddComponent<LijiangEchoGameController>();
        Debug.Log("[漓江回声] 已创建运行时关卡控制器");
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        EnsureAudioSources();

        // 读战斗选项资源(Resources/LijiangEchoBattleSettings)。审核组员在该资源上勾选即可生效,
        // 无需改代码;资源不存在时保持代码默认值。
        LijiangEchoBattleSettings settings = LijiangEchoBattleSettings.Load();
        if (settings != null)
        {
            doubleNoteMirrorConverge = settings.doubleNoteMirrorConverge;
        }
    }

    private IEnumerator Start()
    {
        HidePrototypeObjects();

        // 等待头显位姿生效，避免误用场景里的普通 Main Camera 高度。
        float trackingWaitDeadline = Time.realtimeSinceStartup + 2f;
        while (Time.realtimeSinceStartup < trackingWaitDeadline)
        {
            Camera candidate = FindGameplayCamera();
            bool hasXrCamera = candidate != null && candidate.name.Contains("CenterEye");
            headPoseWasTracked = IsHeadPoseTracked();
            if (candidate != null && (!hasXrCamera || headPoseWasTracked))
            {
                break;
            }

            yield return null;
        }

        PrepareStageRoot(true);
        yield return PreloadBattleSceneIfConfigured();
        int debugStage = ReadDebugStartStage();
        if (debugStage >= 0)
        {
            JumpToStageForDebug(debugStage);
        }
        else if (ExternalSelectedLevel.HasValue)
        {
            selectedLevel = ExternalSelectedLevel.Value;
            ShowIntro();
        }
        else
        {
            ShowStart();
        }

        experienceReady = true;
    }

    private void Update()
    {
        if (!experienceReady)
        {
            return;
        }

        if (stageRoot == null || cameraAnchor == null)
        {
            PrepareStageRoot();
        }

        bool headPoseTracked = IsHeadPoseTracked();
        Camera preferredCamera = FindGameplayCamera();
        bool cameraChanged = preferredCamera != null && preferredCamera.transform != cameraAnchor;
        if (cameraChanged || (headPoseTracked && !headPoseWasTracked))
        {
            PrepareStageRoot(true);
        }

        headPoseWasTracked = headPoseTracked;

        stageTimer += Time.deltaTime;
        selectMoveCooldown -= Time.deltaTime;
        swipeCooldown -= Time.deltaTime;
        UpdateControllerInput();

#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.f8Key.wasPressedThisFrame)
        {
            ShowBattle();
            return;
        }
#endif

        if (MenuPressed())
        {
            ToggleMenuOverlay();
        }

        UpdateMotions();

        switch (currentStage)
        {
            case Stage.Start:
                UpdateStart();
                break;
            case Stage.Select:
                UpdateSelect();
                break;
            case Stage.Intro:
                UpdateIntro();
                break;
            case Stage.Trace:
                UpdateTrace();
                break;
            case Stage.Battle:
                UpdateBattle();
                break;
            case Stage.Card:
                UpdateCard();
                break;
            case Stage.Result:
                UpdateResult();
                break;
        }
    }

    private void HidePrototypeObjects()
    {
        Scene scene = SceneManager.GetActiveScene();
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            HidePrototypeRecursive(rootObject.transform);
        }
    }

    private void HidePrototypeRecursive(Transform item)
    {
        string objectName = item.name;
        if (objectName == "shanjingshen" ||
            objectName == "Teleport Hotspot" ||
            objectName.Contains("[BuildingBlock] Cube"))
        {
            item.gameObject.SetActive(false);
            return;
        }

        for (int i = 0; i < item.childCount; i++)
        {
            HidePrototypeRecursive(item.GetChild(i));
        }
    }

    private void PrepareStageRoot(bool forceReanchor = false)
    {
        Camera mainCamera = FindGameplayCamera();

        if (mainCamera == null)
        {
            GameObject cameraObject = new GameObject("漓江回声_预览相机");
            mainCamera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = Vector3.zero;
            cameraObject.transform.rotation = Quaternion.identity;
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0.04f, 0.03f, 0.055f);
        }

        if (cameraAnchor != mainCamera.transform)
        {
            Debug.Log("[漓江回声] 关卡画面挂载到相机：" + mainCamera.name);
        }

        cameraAnchor = mainCamera.transform;

        if (stageRoot == null)
        {
            GameObject rootObject = new GameObject("漓江回声_关卡画面");
            stageRoot = rootObject.transform;
        }

        if (!stageAnchored || forceReanchor)
        {
            Vector3 forward = Vector3.ProjectOnPlane(cameraAnchor.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.01f)
            {
                forward = Vector3.forward;
            }

            forward.Normalize();
            stageRoot.SetParent(null, false);
            stageRoot.position = cameraAnchor.position + forward * StageDistance + Vector3.down * 0.02f;
            stageRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);
            stageRoot.localScale = Vector3.one * StageWorldScale;
            stageAnchored = true;
            Debug.Log("[漓江回声] MR 内容已锚定到真实空间，不再跟随头部转动");
        }

        CacheControllerAnchors();
    }

    private Camera FindGameplayCamera()
    {
        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Camera taggedMainCamera = null;
        Camera firstEnabledCamera = null;

        foreach (Camera camera in cameras)
        {
            if (camera == null || camera.targetTexture != null)
            {
                continue;
            }

            if (camera.name == "CenterEyeAnchor" || camera.name.Contains("CenterEye"))
            {
                return camera;
            }

            if (!camera.isActiveAndEnabled)
            {
                continue;
            }

            if (taggedMainCamera == null && camera.CompareTag("MainCamera"))
            {
                taggedMainCamera = camera;
            }

            if (firstEnabledCamera == null)
            {
                firstEnabledCamera = camera;
            }
        }

        return taggedMainCamera != null ? taggedMainCamera : firstEnabledCamera;
    }

    private static bool IsHeadPoseTracked()
    {
        UnityEngine.XR.InputDevice headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!headDevice.isValid)
        {
            return false;
        }

        if (headDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked))
        {
            return tracked;
        }

        return headDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out _);
    }

    private void ResetStage(Stage nextStage)
    {
        ReleaseIntroVideo();
        currentStage = nextStage;
        stageTimer = 0f;
        feedbackTimer = 0f;
        hitFlashTimer = 0f;
        introPreLevelStarted = false;
        introPreLevelFinished = false;
        traceCompleted = false;
        traceCompleteTimer = 0f;
        tracePointIndex = 0;
        tracePoints = null;
        traceDrawRenderer = null;
        tracePointer = null;
        traceMirrorDrawRenderer = null;
        traceMirrorPointer = null;
        traceTwoHands = false;
        traceLeftPoints = null;
        traceLeftIndex = 0;
        hasPreviousTraceLeftPointer = false;
        leftHandPivot = null;
        rightHandPivot = null;
        leftHandRenderer = null;
        rightHandRenderer = null;
        leftHandStrikeTimer = 0f;
        rightHandStrikeTimer = 0f;
        traceFeedbackText = null;
        startButtonPanelRenderer = null;
        startButtonRenderer = null;
        hasPreviousTracePointer = false;
        controllerMotionReady = false;
        swipeCooldown = 0f;
        hintText = null;
        scoreText = null;
        feedbackText = null;
        patternRenderer = null;
        ringRenderer = null;
        progressFillRenderer = null;
        countdownRenderer = null;
        cardPageRenderer = null;
        ringTransform = null;
        ringFeedback = null;
        monsterRoot = null;
        introScrollRoot = null;
        introPreLevelRoot = null;
        introVideoPlayer = null;
        introVideoTexture = null;
        ringBaseScale = Vector3.one;
        battleMusicStarted = false;
        battleMusicTime = 0f;
        battleEndingTimer = 0f;
        holdActive = false;
        holdProgress = 0f;
        heldNote = null;
        if (battleMusicSource != null)
        {
            battleMusicSource.Stop();
            battleMusicSource.clip = null;
        }
        selectCards = null;
        selectNumbers = null;
        menuObjects.Clear();
        motionItems.Clear();
        activeNotes.Clear();
        scheduledSfx.Clear();
        comboRipples.Clear();
        comboRippleTimer = 0f;
        EditorBattleTime = -1f; // 离开战斗:编辑器不再跟随
        introWalkItems.Clear();
        introPreLevelItems.Clear();
        introFlyItems.Clear();
        introFocusItems.Clear();

        foreach (GameObject item in spawnedObjects)
        {
            if (item != null)
            {
                Destroy(item);
            }
        }

        spawnedObjects.Clear();
    }

    /// <summary>
    /// 调试用:读取"下次 Play 直接进哪一段"的标记(由 漓江回声/调试 菜单写入 PlayerPrefs)。
    /// 返回阶段序号(见 JumpToStageForDebug),无标记返回 -1。仅编辑器生效,发布版永远 -1。
    /// </summary>
    private static int ReadDebugStartStage()
    {
#if UNITY_EDITOR
        return PlayerPrefs.GetInt("LJ_DebugStartStage", -1);
#else
        return -1;
#endif
    }

    /// <summary>
    /// 调试用:直接进入指定阶段,跳过前面的流程,便于单独跑测每一段。
    /// 0=开始 1=选关 2=过场 3=描绘 4=战斗 5=结算。用一次即清除标记。
    /// </summary>
    private void JumpToStageForDebug(int stageIndex)
    {
#if UNITY_EDITOR
        PlayerPrefs.DeleteKey("LJ_DebugStartStage");
        selectedLevel = Mathf.Clamp(PlayerPrefs.GetInt("LJ_DebugLevel", 0), 0, levelNames.Length - 1);
#endif
        Debug.Log($"[漓江回声] 调试:直接进入阶段 {stageIndex}(关卡 {selectedLevel}）");
        switch (stageIndex)
        {
            case 1: ShowSelect(); break;
            case 2: ShowIntro(); break;
            case 3: ShowTrace(); break;
            case 4: ShowBattle(); break;
            case 5: ShowCard(); break;
            default: ShowStart(); break;
        }
    }

    private void ShowStart()
    {
        ResetStage(Stage.Start);
        PlayStageLoop("ambience_water", 0.32f);
        PlaySfx("birds", 0.22f);

        AddLayer("start/frame_16_9", "开始界面底框", Vector3.zero, MainCanvasWidth, -20, 0.04f);
        AddLayer("start/back_mountain_1", "开始远山一", new Vector3(0f, -0.02f, 0.34f), WideStripWidth, -16, 0.9f);
        AddLayer("start/back_mountain_2", "开始远山二", new Vector3(0f, -0.02f, 0.25f), WideStripWidth, -15, 0.82f);
        AddLayer("start/back_mountain_3", "开始远山三", new Vector3(0f, -0.02f, 0.16f), WideStripWidth, -14, 0.78f);
        AddLayer("start/back_building", "开始建筑", new Vector3(0f, -0.02f, 0.07f), WideStripWidth, -13, 0.88f);

        GameObject cloudOne = AddLayer("start/back_cloud_1", "开始后云一", new Vector3(-0.02f, -0.02f, -0.04f), WideStripWidth, -10, 0.76f);
        GameObject cloudTwo = AddLayer("start/back_cloud_2", "开始后云二", new Vector3(0.02f, -0.02f, -0.12f), WideStripWidth, -9, 0.62f);
        RegisterMotion(cloudOne, MotionKind.FloatX, 0.045f, 0.55f, 0f);
        RegisterMotion(cloudTwo, MotionKind.FloatX, 0.032f, 0.42f, 1.4f);

        AddLayer("start/front_mountain_left", "开始前山左", new Vector3(0f, -0.02f, -0.25f), WideStripWidth, -6);
        AddLayer("start/front_mountain_right", "开始前山右", new Vector3(0f, -0.02f, -0.32f), WideStripWidth, -5);

        GameObject frontCloudLeft = AddLayer("start/front_cloud_left", "开始前云左", new Vector3(0f, -0.02f, -0.40f), WideStripWidth, -3, 0.9f);
        GameObject frontCloudRight = AddLayer("start/front_cloud_right", "开始前云右", new Vector3(0f, -0.02f, -0.46f), WideStripWidth, -2, 0.9f);
        RegisterMotion(frontCloudLeft, MotionKind.FloatX, 0.038f, 0.5f, 2f);
        RegisterMotion(frontCloudRight, MotionKind.FloatX, 0.036f, 0.48f, 4f);

        GameObject buttonPanel = AddIcon("start/start_ui", "进入游戏主按钮", new Vector3(0f, -0.38f, -0.53f), 0.52f, 5, 0.98f);
        GameObject button = AddIcon("start/start_button", "开始按钮高光", new Vector3(0f, -0.48f, -0.55f), 0.095f, 6, 0.88f);
        startButtonPanelRenderer = buttonPanel.GetComponent<SpriteRenderer>();
        startButtonRenderer = button.GetComponent<SpriteRenderer>();
        RegisterMotion(buttonPanel, MotionKind.Pulse, 0.01f, 2.1f, 0.7f);
        RegisterMotion(button, MotionKind.Pulse, 0.022f, 2.4f, 0f);

        GameObject ball = AddIcon("start/embroidered_ball", "绣球", new Vector3(0f, 0.23f, -0.66f), 0.72f, 7, 0.96f);
        GameObject birdBig = AddIcon("start/bird_big", "大鸟", new Vector3(1.28f, 0.68f, -0.61f), 0.19f, 8, 0.92f);
        GameObject birdSmall = AddIcon("start/bird_small", "小鸟", new Vector3(1.74f, 0.52f, -0.63f), 0.16f, 8, 0.78f);
        RegisterMotion(ball, MotionKind.FloatY, 0.035f, 1.4f, 0f);
        RegisterMotion(birdBig, MotionKind.FloatY, 0.025f, 2.1f, 1.2f);
        RegisterMotion(birdSmall, MotionKind.FloatY, 0.022f, 1.8f, 2.8f);

        AddIcon("start/progress_bar", "开始进度底条", new Vector3(0f, -0.74f, -0.2f), 0.12f, 9, 0.82f);
        GameObject pattern = AddIcon("start/progress_pattern", "开始进度纹样", new Vector3(-0.72f, -0.74f, -0.21f), 0.08f, 10, 0.95f);
        RegisterMotion(pattern, MotionKind.FloatX, 0.34f, 0.72f, 1.7f);

        AddLayer("start/start_border", "开始外框纹样", new Vector3(0f, -0.02f, -0.23f), WideStripWidth, 24, 0.95f);

        AddIcon("ui/settings", "左上设置入口", new Vector3(-2.42f, 1.05f, -0.28f), 0.24f, 30, 0.88f);
    }

    private void UpdateStart()
    {
        Rect startButtonBounds = new Rect(-0.72f, -0.72f, 1.44f, 0.58f);
        bool hovered = TryGetControllerHover(startButtonBounds, out bool pointerPressed);
        if (startButtonPanelRenderer != null)
        {
            startButtonPanelRenderer.color = hovered
                ? Color.white
                : new Color(1f, 1f, 1f, 0.92f);
        }

        if (startButtonRenderer != null)
        {
            startButtonRenderer.color = hovered
                ? new Color(1f, 0.9f, 0.42f, 1f)
                : new Color(1f, 1f, 1f, 0.88f);
        }

        if (pointerPressed || NonPointerConfirmPressed())
        {
            PlaySfx("button", 0.62f);
            ShowSelect();
        }
    }

    private void ShowSelect()
    {
        ResetStage(Stage.Select);
        PlayStageLoop("ambience", 0.34f);

        AddLayer("select/select_frame", "选关紫色暗幕", Vector3.zero, MainCanvasWidth, -18, 0.025f);
        AddLayer("select/select_line", "选关连接线", new Vector3(0f, -0.02f, -0.03f), WideStripWidth, -6, 0.92f);
        AddLayer("select/select_edge", "选关两侧色块", new Vector3(0f, -0.02f, -0.04f), WideStripWidth, -5, 0.72f);

        selectCards = new SpriteRenderer[levelCardPaths.Length];
        selectNumbers = new SpriteRenderer[levelNumberPaths.Length];
        for (int i = 0; i < levelCardPaths.Length; i++)
        {
            GameObject card = AddLayer(levelCardPaths[i], "选关卡片_" + levelNames[i], new Vector3(0f, -0.02f, -0.08f - i * 0.01f), WideStripWidth, 2 + i);
            selectCards[i] = card.GetComponent<SpriteRenderer>();

            GameObject symbol = AddLayer(levelSymbolPaths[i], "选关纹样_" + levelNames[i], new Vector3(0f, -0.02f, -0.13f - i * 0.01f), WideStripWidth, 8 + i, 0.92f);
            RegisterMotion(symbol, MotionKind.FloatY, 0.018f, 1.6f, i * 1.3f);

            GameObject number = AddIcon(levelNumberPaths[i], "关卡数字_" + (i + 1), selectNumberPositions[i], 0.18f, 18);
            selectNumbers[i] = number.GetComponent<SpriteRenderer>();
        }

        AddLayer("select/bird_left_symbol", "左侧鸟纹装饰", new Vector3(0f, -0.02f, -0.16f), WideStripWidth, 13, 0.78f);
        AddLayer("select/frog_right_symbol", "右侧蛙纹装饰", new Vector3(0f, -0.02f, -0.17f), WideStripWidth, 13, 0.78f);
        AddLayer("select/bird_left_card", "左侧鸟纹白底卡", new Vector3(0f, -0.02f, -0.18f), WideStripWidth, 14, 0.82f);
        AddLayer("select/frog_right_card", "右侧蛙纹白底卡", new Vector3(0f, -0.02f, -0.19f), WideStripWidth, 14, 0.82f);
        AddLayer("select/select_border", "选关外框", new Vector3(0f, -0.02f, -0.2f), WideStripWidth, 20, 0.92f);
        AddIcon("ui/settings", "左上设置入口", new Vector3(-2.42f, 1.05f, -0.25f), 0.24f, 30, 0.88f);

        UpdateSelectedCardVisual();
    }

    private void UpdateSelect()
    {
        for (int i = 0; i < selectNumberPositions.Length; i++)
        {
            Rect cardBounds = new Rect(selectNumberPositions[i].x - 0.58f, -0.82f, 1.16f, 1.48f);
            if (!TryGetControllerHover(cardBounds, out bool pointerPressed))
            {
                continue;
            }

            if (selectedLevel != i)
            {
                selectedLevel = i;
                UpdateSelectedCardVisual();
            }

            if (pointerPressed)
            {
                PlaySfx("button", 0.62f);
                ShowIntro();
                return;
            }
        }

        int direction = ReadHorizontalStep();
        if (direction != 0 && selectMoveCooldown <= 0f)
        {
            selectedLevel = Mathf.Clamp(selectedLevel + direction, 0, levelNames.Length - 1);
            selectMoveCooldown = 0.25f;
            PlaySfx("swipe", 0.34f);
            UpdateSelectedCardVisual();
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                selectedLevel = 0;
                UpdateSelectedCardVisual();
            }
            else if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                selectedLevel = 1;
                UpdateSelectedCardVisual();
            }
            else if (Keyboard.current.digit3Key.wasPressedThisFrame)
            {
                selectedLevel = 2;
                UpdateSelectedCardVisual();
            }
        }

        if (NonPointerConfirmPressed())
        {
            PlaySfx("button", 0.62f);
            ShowIntro();
        }
    }

    private void UpdateSelectedCardVisual()
    {
        if (selectCards == null)
        {
            return;
        }

        for (int i = 0; i < selectCards.Length; i++)
        {
            bool selected = i == selectedLevel;
            selectCards[i].color = selected ? Color.white : new Color(1f, 1f, 1f, 0.52f);

            if (selectNumbers != null && i < selectNumbers.Length && selectNumbers[i] != null)
            {
                selectNumbers[i].color = selected ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                selectNumbers[i].transform.localScale = Vector3.one * (selected ? 0.23f : 0.18f);
            }
        }

        _ = selectedLevel;
    }

    private void ShowIntro()
    {
        ResetStage(Stage.Intro);
        PlayStageLoop("water", 0.3f);
        PlayAuxiliaryLoop("footsteps", 0.25f);
        BuildIntroWalkStage();
    }

    private void UpdateIntro()
    {
        if (!introPreLevelStarted)
        {
            UpdateIntroWalkStage();
            if (stageTimer >= IntroWalkDuration)
            {
                StartIntroPreLevelVideo();
            }

            return;
        }

        UpdateIntroPreLevelStage();
        if (introPreLevelFinished || stageTimer > IntroTotalDuration)
        {
            ShowTrace();
        }
    }

    private void BuildIntroWalkStage()
    {
        GameObject scrollRootObject = new GameObject("过场漂浮素材");
        introScrollRoot = scrollRootObject.transform;
        introScrollRoot.SetParent(stageRoot, false);
        introScrollRoot.localPosition = Vector3.zero;
        introScrollRoot.localRotation = Quaternion.identity;
        introScrollRoot.localScale = Vector3.one;
        spawnedObjects.Add(scrollRootObject);

        // 远方地平线一排小远山:每座缩到约原来 1/5,底面落在地平线上,横向排成一排(静止,不随
        // 漂浮素材横移)。参数:horizonY=地平线高度、mtnHeight=山高、xs=各山横坐标。可自行增删调整。
        const float horizonY = 0.30f;    // 地平线再往上抬
        const float mtnHeight = 0.025f;  // 再缩到上一版的 1/4,很小
        float mtnCenterY = horizonY + mtnHeight * 0.5f; // 让山底贴地平线
        string[] horizonMtnArt =
        {
            "start/back_mountain_1", "start/back_mountain_2", "start/back_mountain_3",
            "start/front_mountain_left", "start/front_mountain_right"
        };
        // 山更小 → 用更密的间距把整条地平线排满(从左到右铺满一排)。
        const float horizonHalfSpan = 2.1f;   // 排布横向半宽
        const float horizonStep = 0.14f;      // 相邻两山间距(越小越密)
        int horizonCount = Mathf.CeilToInt((horizonHalfSpan * 2f) / horizonStep) + 1;
        for (int m = 0; m < horizonCount; m++)
        {
            float hx = -horizonHalfSpan + m * horizonStep;
            AddIcon(
                horizonMtnArt[m % horizonMtnArt.Length],
                "地平线小远山_" + m,
                new Vector3(hx, mtnCenterY, 0.44f),
                mtnHeight,
                -50 + (m % 5),
                0.85f);
        }
        AddLayer("ui/mountain_background", "地平线天幕", new Vector3(0f, horizonY - 0.04f, 0.5f), WideStripWidth, -52, 0.45f);

        AddIntroFlyItem("transition/mountain_1", "近景山一", new RectInt(127, 197, 490, 260), new Vector3(-3.25f, -0.18f, -0.16f), new Vector3(3.15f, -0.05f, -0.16f), 0.42f, 0.78f, 0.0f, 5.8f, 12, 0.88f);
        AddIntroFlyItem("transition/mountain_4", "近景山二", new RectInt(1390, 219, 373, 197), new Vector3(3.20f, -0.34f, -0.18f), new Vector3(-3.10f, -0.20f, -0.18f), 0.38f, 0.74f, 0.3f, 6.1f, 13, 0.84f);
        AddIntroFlyItem("transition/terrace", "漂浮梯田", new RectInt(507, 314, 451, 139), new Vector3(-3.0f, -0.60f, -0.22f), new Vector3(3.1f, -0.46f, -0.22f), 0.24f, 0.46f, 0.8f, 6.9f, 16, 0.92f);
        AddIntroFlyItem("transition/house_1", "漂浮房屋一", new RectInt(749, 289, 217, 162), new Vector3(3.05f, 0.20f, -0.24f), new Vector3(-3.05f, 0.02f, -0.24f), 0.28f, 0.56f, 1.1f, 7.0f, 20, 0.94f);
        AddIntroFlyItem("transition/house_3", "漂浮房屋二", new RectInt(1416, 274, 217, 162), new Vector3(-3.15f, 0.34f, -0.25f), new Vector3(3.0f, 0.18f, -0.25f), 0.25f, 0.52f, 1.7f, 7.5f, 21, 0.92f);
        AddIntroFlyItem("transition/moon", "漂浮月亮", new RectInt(796, 177, 73, 59), new Vector3(-2.8f, 0.74f, -0.27f), new Vector3(2.9f, 0.57f, -0.27f), 0.16f, 0.32f, 1.8f, 7.8f, 22, 0.95f);
        AddIntroFlyItem("transition/animal_1", "漂浮动物一", new RectInt(600, 344, 198, 110), new Vector3(3.15f, -0.10f, -0.30f), new Vector3(-3.0f, 0.12f, -0.30f), 0.25f, 0.48f, 2.2f, 8.1f, 28, 0.95f);
        AddIntroFlyItem("transition/animal_3", "漂浮动物二", new RectInt(1101, 375, 213, 71), new Vector3(-3.1f, 0.08f, -0.31f), new Vector3(3.15f, -0.02f, -0.31f), 0.18f, 0.37f, 2.8f, 8.5f, 29, 0.96f);
        AddIntroFlyItem("transition/animal_4", "漂浮动物三", new RectInt(1420, 346, 164, 90), new Vector3(3.2f, 0.42f, -0.32f), new Vector3(-3.05f, 0.24f, -0.32f), 0.20f, 0.40f, 3.4f, 8.7f, 30, 0.94f);
        AddIntroFlyItem("transition/person_1", "漂浮人物一", new RectInt(941, 321, 61, 111), new Vector3(-2.9f, -0.20f, -0.33f), new Vector3(3.05f, -0.05f, -0.33f), 0.21f, 0.43f, 3.6f, 8.9f, 31, 0.92f);

        AddIntroFlyItem("transition/water", "漂浮水纹", new RectInt(1696, 375, 1333, 74), new Vector3(-3.5f, -0.58f, -0.20f), new Vector3(3.4f, -0.43f, -0.20f), 0.14f, 0.30f, 13.2f, 18.7f, 15, 0.78f);
        AddIntroFlyItem("transition/house_2", "漂浮房屋三", new RectInt(1281, 310, 135, 125), new Vector3(3.0f, 0.16f, -0.25f), new Vector3(-3.05f, 0.30f, -0.25f), 0.24f, 0.50f, 13.4f, 18.9f, 23, 0.93f);
        AddIntroFlyItem("transition/house_4", "漂浮房屋四", new RectInt(1948, 295, 135, 125), new Vector3(-3.05f, 0.38f, -0.26f), new Vector3(3.0f, 0.17f, -0.26f), 0.23f, 0.48f, 13.7f, 19.2f, 24, 0.92f);
        AddIntroFlyItem("transition/animal_2", "漂浮动物四", new RectInt(912, 388, 127, 62), new Vector3(3.1f, -0.08f, -0.31f), new Vector3(-3.05f, 0.04f, -0.31f), 0.18f, 0.37f, 14.0f, 19.0f, 30, 0.94f);
        AddIntroFlyItem("transition/animal_5", "漂浮动物五", new RectInt(1718, 328, 164, 98), new Vector3(-3.1f, 0.32f, -0.32f), new Vector3(3.0f, 0.14f, -0.32f), 0.21f, 0.44f, 14.4f, 19.4f, 31, 0.95f);
        AddIntroFlyItem("transition/person_2", "漂浮人物二", new RectInt(1009, 338, 72, 95), new Vector3(3.0f, -0.18f, -0.33f), new Vector3(-3.0f, -0.02f, -0.33f), 0.21f, 0.43f, 14.8f, 19.6f, 32, 0.92f);
        AddIntroFlyItem("transition/person_3", "漂浮人物三", new RectInt(1580, 332, 83, 98), new Vector3(-3.0f, 0.10f, -0.34f), new Vector3(3.0f, -0.05f, -0.34f), 0.20f, 0.42f, 15.2f, 19.8f, 33, 0.93f);

        AddIntroFlyItem("transition/mountain_2", "远山一", new RectInt(444, 234, 387, 202), new Vector3(-3.15f, -0.12f, -0.16f), new Vector3(3.05f, -0.26f, -0.16f), 0.35f, 0.72f, 23.4f, 30.2f, 12, 0.86f);
        AddIntroFlyItem("transition/mountain_3", "远山二", new RectInt(906, 195, 460, 248), new Vector3(3.2f, -0.30f, -0.18f), new Vector3(-3.05f, -0.10f, -0.18f), 0.42f, 0.82f, 23.8f, 30.7f, 13, 0.88f);
        AddIntroFlyItem("transition/mountain_5", "远山三", new RectInt(1890, 213, 428, 208), new Vector3(-3.25f, 0.02f, -0.20f), new Vector3(3.1f, -0.20f, -0.20f), 0.36f, 0.72f, 24.5f, 31.4f, 14, 0.84f);
        AddIntroFlyItem("transition/mountain_6", "远山四", new RectInt(2297, 296, 394, 130), new Vector3(3.15f, 0.28f, -0.22f), new Vector3(-3.1f, 0.08f, -0.22f), 0.24f, 0.49f, 25.2f, 32.0f, 15, 0.86f);
        AddIntroFlyItem("transition/mountain_7", "远山五", new RectInt(2676, 206, 348, 219), new Vector3(-3.1f, -0.34f, -0.24f), new Vector3(3.1f, -0.12f, -0.24f), 0.38f, 0.78f, 26.0f, 32.8f, 16, 0.90f);
        AddIntroFlyItem("transition/animal_6", "漂浮鱼群", new RectInt(2210, 367, 220, 38), new Vector3(3.2f, 0.48f, -0.31f), new Vector3(-3.1f, 0.20f, -0.31f), 0.13f, 0.27f, 26.4f, 33.1f, 30, 0.95f);
        AddIntroFlyItem("transition/person_4", "漂浮人物四", new RectInt(1935, 355, 54, 69), new Vector3(-3.0f, -0.18f, -0.32f), new Vector3(3.0f, 0.04f, -0.32f), 0.17f, 0.35f, 27.0f, 33.4f, 31, 0.92f);
        AddIntroFlyItem("transition/beast", "迎面兽纹", new RectInt(2642, 45, 475, 391), new Vector3(3.25f, 0.08f, -0.36f), new Vector3(-3.1f, -0.02f, -0.36f), 0.42f, 1.05f, 27.3f, 34.0f, 40, 0.98f);

        GameObject hollowFrame = AddLayer("transition/hollow_frame", "过场镂空边框", Vector3.zero, MainCanvasWidth, 90, 0.76f, introScrollRoot);
        AddIntroFadeItem(hollowFrame.GetComponent<SpriteRenderer>(), 0.76f, true);
        GameObject purpleFrame = AddLayer("transition/purple_frame", "过场紫色边框", new Vector3(0f, 0f, -0.46f), MainCanvasWidth, 91, 0.34f, introScrollRoot);
        AddIntroFadeItem(purpleFrame.GetComponent<SpriteRenderer>(), 0.34f, true);

        UpdateIntroWalkStage();
    }

    private void AddIntroFlyItem(
        string resourcePath,
        string objectName,
        RectInt topLeftCrop,
        Vector3 startCenter,
        Vector3 endCenter,
        float startHeight,
        float endHeight,
        float startTime,
        float endTime,
        int order,
        float alpha)
    {
        int spatialIndex = introFlyItems.Count;
        float startDepth = 5.6f + (spatialIndex % 4) * 0.85f;
        float endDepth = -4.1f - (spatialIndex % 3) * 0.55f;
        Vector3 spatialStart = new Vector3(startCenter.x * 0.12f, startCenter.y * 0.42f, startDepth);
        Vector3 spatialEnd = new Vector3(endCenter.x * 0.74f, endCenter.y * 1.08f, endDepth);
        float direction = Mathf.Sign(endCenter.x - startCenter.x);
        if (Mathf.Approximately(direction, 0f))
        {
            direction = spatialIndex % 2 == 0 ? 1f : -1f;
        }

        GameObject itemObject = AddCroppedSprite(
            resourcePath,
            objectName,
            topLeftCrop,
            spatialStart,
            startHeight,
            order,
            0f,
            false,
            introScrollRoot);
        introFlyItems.Add(new IntroFlyItem
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

    private void AddIntroFocusItem(
        string resourcePath,
        string objectName,
        RectInt topLeftCrop,
        float startTime,
        float endTime,
        float targetHeight)
    {
        GameObject panelObject = AddSolidRect(
            objectName + "底板",
            new Vector3(0f, 0f, -0.37f),
            4.45f,
            1.34f,
            new Color(0.12f, 0.035f, 0.17f, 0f),
            68);
        panelObject.transform.SetParent(introScrollRoot, false);
        panelObject.transform.localPosition = new Vector3(0f, 0f, -0.37f);

        TextMesh caption = AddText("绘制纹样", new Vector3(-1.25f, 0f, -0.41f), 0.032f, new Color(1f, 0.95f, 1f, 0f), 76);
        caption.transform.SetParent(introScrollRoot, false);
        caption.transform.localPosition = new Vector3(-1.25f, 0f, -0.41f);

        AddIntroFlyItem(
            resourcePath,
            objectName,
            topLeftCrop,
            new Vector3(0.62f, 0f, -0.43f),
            new Vector3(0.68f, 0.02f, -0.43f),
            targetHeight * 0.84f,
            targetHeight,
            startTime,
            endTime,
            78,
            0.98f);

        introFocusItems.Add(new IntroFocusItem
        {
            PanelRenderer = panelObject.GetComponent<SpriteRenderer>(),
            Caption = caption,
            StartTime = startTime,
            EndTime = endTime
        });
    }

    private void AddIntroFadeItem(SpriteRenderer renderer, float targetAlpha, bool walkItem)
    {
        if (renderer == null)
        {
            return;
        }

        IntroFadeItem item = new IntroFadeItem
        {
            Renderer = renderer,
            TargetAlpha = targetAlpha
        };

        if (walkItem)
        {
            introWalkItems.Add(item);
        }
        else
        {
            introPreLevelItems.Add(item);
        }
    }

    private void UpdateIntroWalkStage()
    {
        foreach (IntroFlyItem item in introFlyItems)
        {
            if (item.Renderer == null)
            {
                continue;
            }

            float progress = Mathf.Clamp01(Mathf.InverseLerp(item.StartTime, item.EndTime, stageTimer));
            float eased = Mathf.SmoothStep(0f, 1f, progress);
            float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(item.StartTime - 0.18f, item.StartTime + 0.48f, stageTimer));
            float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(item.EndTime - 0.55f, item.EndTime + 0.18f, stageTimer));
            float alpha = item.TargetAlpha * Mathf.Min(fadeIn, fadeOut);

            Vector3 center = Vector3.Lerp(item.StartCenter, item.EndCenter, eased);
            center.y += Mathf.Sin(Time.time * 1.55f + item.FloatPhase) * 0.035f;
            float height = Mathf.Lerp(item.StartHeight, item.EndHeight, eased);
            SetCroppedSpritePose(item.Renderer, center, height, alpha, false);
            item.Renderer.transform.localRotation = Quaternion.Euler(
                Vector3.Lerp(item.StartRotation, item.EndRotation, eased));
        }

        foreach (IntroFocusItem focus in introFocusItems)
        {
            float fadeIn = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(focus.StartTime - 0.2f, focus.StartTime + 0.45f, stageTimer));
            float fadeOut = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(focus.EndTime - 0.45f, focus.EndTime + 0.15f, stageTimer));
            float alpha = Mathf.Min(fadeIn, fadeOut);
            if (focus.PanelRenderer != null)
            {
                focus.PanelRenderer.color = new Color(0.12f, 0.035f, 0.17f, alpha * 0.88f);
            }

            if (focus.Caption != null)
            {
                focus.Caption.color = new Color(1f, 0.95f, 1f, alpha * 0.96f);
            }
        }
    }

    private void StartIntroPreLevelVideo()
    {
        introPreLevelStarted = true;
        introPreLevelFinished = false;
        StopAuxiliaryLoop();

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

        GameObject blackBackdrop = AddSolidRect(
            "关卡前动画黑底",
            new Vector3(0f, 0f, -0.68f),
            MainCanvasWidth,
            1.55f,
            Color.black,
            98);
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

    private void ShowTrace()
    {
        ResetStage(Stage.Trace);
        PlayStageLoop("ambience", 0.26f);

        AddLayer("transition/purple_frame", "描绘阶段淡紫边框", Vector3.zero, MainCanvasWidth, -20, 0.14f);
        AddLayer("pattern/drawing_card", "纹样描绘台", new Vector3(0f, 0f, -0.22f), 4.25f, -4, 0.72f);

        RectInt[] traceCrops =
        {
            new RectInt(273, 2314, 1951, 2547),
            new RectInt(1822, 2125, 2973, 2185),
            new RectInt(995, 836, 1335, 1359)
        };
        GameObject sourcePattern = AddCroppedSprite(
            tracePaths[selectedLevel],
            "描绘参考纹样",
            traceCrops[selectedLevel],
            new Vector3(0f, 0.02f, -0.48f),
            0.88f,
            18,
            0.74f,
            false,
            centerOnVisual: true); // 按不透明像素真实中心对齐,纹样居中(不再偏到左下角)
        RegisterMotion(sourcePattern, MotionKind.Pulse, 0.01f, 1.7f, 0f);

        // 双手拆分:开镜像时,右手描【右半】纹样、左手描【左半】纹样,各自进度、各自判定,两半都描完才成功。
        // 关镜像时单手描整条。tracePoints=右半(单手时=整条),traceLeftPoints=左半(=右半的水平镜像)。
        bool splitHands = ExternalTraceMirror ?? true;
        traceTwoHands = splitHands;
        tracePoints = BuildTracePath(selectedLevel, splitHands);

        // P1 / 描绘增强：全程「淡淡指引线」——沿纹样形状铺满整条路径，给玩家指引方向。
        // 线本身即对齐纹样基本形状；如需虚线观感，可给此 LineRenderer 换一张虚线纹理材质。
        LineRenderer traceGuideRenderer = AddLineRenderer(
            "纹样描绘指引",
            0.03f,
            new Color(1f, 0.9f, 0.55f, 0.16f),
            30);
        traceGuideRenderer.positionCount = tracePoints.Length;
        for (int gi = 0; gi < tracePoints.Length; gi++)
        {
            traceGuideRenderer.SetPosition(gi, tracePoints[gi] + new Vector3(0f, 0f, -0.018f));
        }

        traceDrawRenderer = AddLineRenderer(
            "已描绘轨迹",
            0.072f,
            new Color(1f, 0.86f, 0.28f, 0.98f),
            34);

        // 描绘增强：已描绘的线沿绘制方向从暗金渐变到亮发光（头→尾逐渐点亮），
        // 相当于纹样随绘制顺序逐渐亮起。colorGradient 按线长归一化，线增长时描绘头始终最亮。
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

        GameObject pointerObject = AddIcon(
            "battle/hit_ring_center",
            "手柄描绘光标",
            new Vector3(0f, 0f, TracePlaneZ - 0.04f),
            0.105f,
            42,
            0.92f);
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

            LineRenderer mirrorGuide = AddLineRenderer("纹样描绘指引(左手)", 0.03f, new Color(1f, 0.9f, 0.55f, 0.16f), 30);
            mirrorGuide.positionCount = traceLeftPoints.Length;
            for (int gi = 0; gi < traceLeftPoints.Length; gi++)
            {
                mirrorGuide.SetPosition(gi, traceLeftPoints[gi] + new Vector3(0f, 0f, -0.018f));
            }

            traceMirrorDrawRenderer = AddLineRenderer("已描绘轨迹(左手)", 0.072f, new Color(1f, 0.86f, 0.28f, 0.98f), 34);
            traceMirrorDrawRenderer.colorGradient = traceGlowGradient;

            GameObject mirrorPointerObject = AddIcon("battle/hit_ring_center", "手柄描绘光标(左手)", new Vector3(0f, 0f, TracePlaneZ - 0.04f), 0.105f, 42, 0.92f);
            traceMirrorPointer = mirrorPointerObject.transform;
            traceMirrorPointer.gameObject.SetActive(false);
        }
        else
        {
            traceLeftPoints = null;
            traceMirrorDrawRenderer = null;
            traceMirrorPointer = null;
        }

        traceFeedbackText = AddText(
            "绘制纹样",
            new Vector3(0f, 0.78f, -0.56f),
            0.027f,
            new Color(1f, 0.93f, 0.72f, 0.94f),
            44);
    }

    private void UpdateTrace()
    {
        if (traceCompleted)
        {
            traceCompleteTimer += Time.deltaTime;
            if (traceCompleteTimer >= 1.05f)
            {
                ShowBattle();
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
        if (!TryGetTracePointer(out Vector3 localPoint, out bool drawing))
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
            tracePointer.localPosition = new Vector3(localPoint.x, localPoint.y, TracePlaneZ - 0.04f);
        }

        if (!drawing || tracePoints == null || tracePointIndex >= tracePoints.Length)
        {
            hasPreviousTracePointer = false;
            if (traceFeedbackText != null)
            {
                traceFeedbackText.text = tracePointIndex == 0
                    ? "按住扳机，从亮起的起点沿纹样描画"
                    : $"描画进度 {Mathf.RoundToInt(tracePointIndex * 100f / tracePoints.Length)}%";
            }
            return;
        }

        Vector3 pointerOnPlane = new Vector3(localPoint.x, localPoint.y, TracePlaneZ);
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
        CacheControllerAnchors();

        // 编辑器鼠标兜底:一支鼠标默认画右手;【按住 Shift 时改画左手】,这样单鼠标也能把左右两半都描完。
        bool mouseHas = TryGetMousePointer(out Vector3 mousePoint, out bool mouseDraw);
        bool mouseToLeft = Keyboard.current != null &&
                           (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);

        // —— 右手 → 右半 ——
        bool rightHas = TryGetHandPointer(true, out Vector3 rPoint, out bool rDraw);
        if (!rightHas && mouseHas && !mouseToLeft)
        {
            rightHas = true;
            rPoint = mousePoint;
            rDraw = mouseDraw;
        }

        UpdateTraceCursor(tracePointer, rightHas, rPoint);
        if (rightHas && rDraw && tracePoints != null && tracePointIndex < tracePoints.Length)
        {
            tracePointIndex = AdvanceTraceHand(tracePoints, tracePointIndex, new Vector3(rPoint.x, rPoint.y, TracePlaneZ), ref previousTracePointer, ref hasPreviousTracePointer);
        }
        else
        {
            hasPreviousTracePointer = false;
        }

        // —— 左手 → 左半(编辑器按住 Shift 时鼠标画这半)——
        bool leftHas = TryGetHandPointer(false, out Vector3 lPoint, out bool lDraw);
        if (!leftHas && mouseHas && mouseToLeft)
        {
            leftHas = true;
            lPoint = mousePoint;
            lDraw = mouseDraw;
        }
        UpdateTraceCursor(traceMirrorPointer, leftHas, lPoint);
        if (leftHas && lDraw && traceLeftPoints != null && traceLeftIndex < traceLeftPoints.Length)
        {
            traceLeftIndex = AdvanceTraceHand(traceLeftPoints, traceLeftIndex, new Vector3(lPoint.x, lPoint.y, TracePlaneZ), ref previousTraceLeftPointer, ref hasPreviousTraceLeftPointer);
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
                traceFeedbackText.text = "双手各按住扳机，左右手分别沿两侧描画";
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

    // 某只手的射线落点 + 该手扳机是否按下(仅该手,互不干扰)。
    private bool TryGetHandPointer(bool right, out Vector3 localPoint, out bool drawing)
    {
        Transform controller = right ? rightControllerAnchor : leftControllerAnchor;
        bool tracked = right ? rightControllerTracked : leftControllerTracked;
        float trigger = right ? rightTriggerValue : leftTriggerValue;
        drawing = trigger > 0.35f;

        if (tracked && controller != null && TryProjectControllerRay(controller, out localPoint))
        {
            return true;
        }

        localPoint = Vector3.zero;
        drawing = false;
        return false;
    }

    // 编辑器鼠标落点 + 左键是否按下(供无手柄时兜底描绘)。
    private bool TryGetMousePointer(out Vector3 localPoint, out bool drawing)
    {
        if (Mouse.current != null && cameraAnchor != null)
        {
            Camera cam = cameraAnchor.GetComponent<Camera>();
            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (TryProjectRay(ray, out localPoint))
                {
                    drawing = Mouse.current.leftButton.isPressed;
                    return true;
                }
            }
        }

        localPoint = Vector3.zero;
        drawing = false;
        return false;
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
            cursor.localPosition = new Vector3(localPoint.x, localPoint.y, TracePlaneZ - 0.04f);
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

        RectInt[] doneCrops = { SnakeDoneCrop, BirdDoneCrop, CoinDoneCrop };
        GameObject completedPattern = AddCroppedSprite(
            donePaths[selectedLevel],
            "完成纹样光效",
            doneCrops[selectedLevel],
            new Vector3(0f, 0.02f, -0.68f),
            0.92f,
            48,
            0.94f,
            false);
        RegisterMotion(completedPattern, MotionKind.Pulse, 0.035f, 3.2f, 0f);
        string[] completionSounds = { "snake", "swipe", "coin" };
        PlaySfx(completionSounds[selectedLevel], 0.68f);
        OVRInput.SetControllerVibration(0.45f, 0.65f, OVRInput.Controller.LTouch | OVRInput.Controller.RTouch);
        Invoke(nameof(StopControllerVibration), 0.16f);
    }

    /// <summary>
    /// 每关纹样的「关键点」(而非细分后的全部路径点),供编辑器"自动摆放打击点"工具定位使用:
    /// 在这些点上各摆一个打击点,连起来就是该关纹样的大致形状。0=蛙纹 1=鸟纹 2=铜钱纹(圆)。
    /// </summary>
    public static Vector2[] GetPatternControlPoints(int level)
    {
        if (level == 2)
        {
            const int ringPoints = 12;
            Vector2[] ring = new Vector2[ringPoints];
            for (int i = 0; i < ringPoints; i++)
            {
                float angle = Mathf.PI * 0.5f - i / (float)ringPoints * Mathf.PI * 2f;
                ring[i] = new Vector2(Mathf.Cos(angle) * 0.43f, Mathf.Sin(angle) * 0.43f + 0.02f);
            }

            return ring;
        }

        return level == 0
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

    private Vector3[] BuildTracePath(int level, bool rightHalfOnly)
    {
        List<Vector3> points = new List<Vector3>();
        if (level == 2)
        {
            if (rightHalfOnly)
            {
                // 右半圆:顶(π/2)→右(0)→底(-π/2);镜像手补出左半圆,合起来是整圈铜钱纹。
                const int halfPoints = 36;
                for (int i = 0; i < halfPoints; i++)
                {
                    float angle = Mathf.PI * 0.5f - i / (float)(halfPoints - 1) * Mathf.PI;
                    points.Add(new Vector3(Mathf.Cos(angle) * 0.43f, Mathf.Sin(angle) * 0.43f + 0.02f, TracePlaneZ));
                }

                return points.ToArray();
            }

            const int circlePoints = 72;
            for (int i = 0; i < circlePoints; i++)
            {
                float angle = Mathf.PI * 0.5f - i / (float)(circlePoints - 1) * Mathf.PI * 2f;
                points.Add(new Vector3(Mathf.Cos(angle) * 0.43f, Mathf.Sin(angle) * 0.43f + 0.02f, TracePlaneZ));
            }

            return points.ToArray();
        }

        Vector2[] controls;
        if (rightHalfOnly)
        {
            // 纹样关于 x=0 对称:主手只描右半(x≥0)控制点,镜像手把它 -x 翻出左半,两半拼成整只纹样。
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
                points.Add(new Vector3(point.x, point.y + 0.02f, TracePlaneZ));
            }
        }

        Vector2 last = controls[^1];
        points.Add(new Vector3(last.x, last.y + 0.02f, TracePlaneZ));
        return points.ToArray();
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

        // 双手镜像:把已描绘线镜像到对侧
        if (traceMirrorDrawRenderer != null)
        {
            traceMirrorDrawRenderer.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                Vector3 mp = tracePoints[i] + new Vector3(0f, 0f, -0.025f);
                traceMirrorDrawRenderer.SetPosition(i, new Vector3(-mp.x, mp.y, mp.z));
            }
        }
    }

    private bool TryGetTracePointer(out Vector3 localPoint, out bool drawing)
    {
        CacheControllerAnchors();
        bool useRight = rightTriggerValue > leftTriggerValue + 0.04f ||
                        (!leftControllerTracked && rightControllerTracked);
        Transform controller = useRight ? rightControllerAnchor : leftControllerAnchor;
        drawing = Mathf.Max(leftTriggerValue, rightTriggerValue) > 0.35f;

        if (controller != null && TryProjectControllerRay(controller, out localPoint))
        {
            return true;
        }

        if (Mouse.current != null && cameraAnchor != null)
        {
            Camera cameraComponent = cameraAnchor.GetComponent<Camera>();
            if (cameraComponent != null)
            {
                Ray mouseRay = cameraComponent.ScreenPointToRay(Mouse.current.position.ReadValue());
                drawing = Mouse.current.leftButton.isPressed;
                return TryProjectRay(mouseRay, out localPoint);
            }
        }

        localPoint = Vector3.zero;
        return false;
    }

    private bool TryProjectControllerRay(Transform controller, out Vector3 localPoint)
    {
        return TryProjectRay(new Ray(controller.position, GetControllerRayDirection(controller)), out localPoint);
    }

    private bool TryProjectRay(Ray ray, out Vector3 localPoint)
    {
        Vector3 planePoint = stageRoot.TransformPoint(new Vector3(0f, 0f, TracePlaneZ));
        Plane plane = new Plane(stageRoot.forward, planePoint);
        if (plane.Raycast(ray, out float distance) && distance > 0f && distance < 8f)
        {
            localPoint = stageRoot.InverseTransformPoint(ray.GetPoint(distance));
            return Mathf.Abs(localPoint.x) <= 2.25f && Mathf.Abs(localPoint.y) <= 1.2f;
        }

        localPoint = Vector3.zero;
        return false;
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

    private void StopControllerVibration()
    {
        OVRInput.SetControllerVibration(0f, 0f, OVRInput.Controller.LTouch | OVRInput.Controller.RTouch);
    }

    /// <summary>
    /// 战斗开始时读谱面表格驱动音符:优先 chart_generated(从音乐生成),否则 chart_liusanjie
    /// (需求表),都没有则保留代码里的默认谱面。文件每行"时间(秒),类型";类型 single/double/hold;
    /// # 开头为注释、空行忽略;会按时间升序排序。
    /// </summary>
    private void LoadChartIfAvailable()
    {
        // 谱面按优先级挑选:先本关卡专属谱(编辑器"应用到该战斗场景"写出的 chart_level{N}),
        // 再全局生成谱 chart_generated,最后需求谱 chart_liusanjie。三关(蛙/鸟/鱼)可各配一张谱。
        string[] candidates =
        {
            "LijiangEchoCharts/chart_level" + selectedLevel,
            "LijiangEchoCharts/chart_generated",
            "LijiangEchoCharts/chart_liusanjie"
        };
        TextAsset chart = null;
        string chartName = null;
        foreach (string path in candidates)
        {
            chart = Resources.Load<TextAsset>(path);
            if (chart != null && !string.IsNullOrEmpty(chart.text))
            {
                chartName = path;
                break;
            }
        }

        if (chart == null || string.IsNullOrEmpty(chart.text))
        {
            return;
        }

        bool explicitTypes = false;
        List<KeyValuePair<float, string>> rows = new List<KeyValuePair<float, string>>();
        foreach (string rawLine in chart.text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
            {
                // 编辑器保存的谱面带此头 → 只认显式类型,不再取模自动生成 swipe。
                if (line.Replace(" ", string.Empty).ToLowerInvariant().Contains("types:explicit"))
                {
                    explicitTypes = true;
                }

                continue;
            }

            string[] parts = line.Split(',');
            if (parts.Length < 1 || !float.TryParse(parts[0].Trim(), out float t))
            {
                continue;
            }

            string type = parts.Length >= 2 ? parts[1].Trim().ToLowerInvariant() : "single";
            rows.Add(new KeyValuePair<float, string>(t, type));
        }

        if (rows.Count == 0)
        {
            return;
        }

        rows.Sort((a, b) => a.Key.CompareTo(b.Key));

        float[] times = new float[rows.Count];
        HashSet<int> holds = new HashSet<int>();
        HashSet<int> doubles = new HashSet<int>();
        HashSet<int> swipes = new HashSet<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            times[i] = rows[i].Key;
            if (rows[i].Value == "hold")
            {
                holds.Add(i);
            }
            else if (rows[i].Value == "double")
            {
                doubles.Add(i);
            }
            else if (rows[i].Value == "swipe")
            {
                swipes.Add(i);
            }
        }

        noteTimes = times;
        holdNoteIndices = holds;
        doubleNoteIndices = doubles;
        swipeNoteIndices = swipes;
        chartTypesExplicit = explicitTypes;
        Debug.Log($"[漓江回声] 关卡 {selectedLevel} 采用谱面 {chartName}:{noteTimes.Length} 个音符(长按 {holds.Count}、双击 {doubles.Count}、挥划 {swipes.Count}、显式类型 {explicitTypes})。");
    }

    // ===== 左右手击打(对应 VR 手柄左/右手) =====
    private const float HandStrikeDuration = 0.36f; // 击打持续时间:加长,手停留更久更看得清
    private const float HandRestAngle = 45f;     // 平时:手臂朝各自外下方甩出(藏在画面下侧,靠透明隐藏)
    private const float HandStrikeAngle = 8f;    // 击打终点:接近竖直略向内,手落到中心圆环上(不越过)
    private const float HandArmLength = 1.0f;     // 臂长:缩短,配合抬高的轴心让击打终点仍落在圆环
    private const float HandPivotSide = 0.45f;    // 左右轴心离中线的横向距离
    private const float HandPivotY = -0.85f;      // 轴心高度:抬进画面中下部(之前 -1.3 太低,Game 画面里出框/被前景挡)
    private const float HandPivotZ = -0.88f;      // 轴心深度:放到玩法平面(≈圆环/音符),不再"非常前面"被近裁剪切掉
    private const float HandVisualHeight = 3.3f;  // 手的显示高度(再放大些,击打时更醒目)
    // 临时诊断:true = 左右手一直可见(不再只有击打瞬间显形),方便截图确认它们停在哪、大小对不对。
    // 定好位置后改回 false 恢复"平时隐藏、击打才现"。
    private const bool HandDebugAlwaysVisible = false;

    // 双击(鸟纹)飞入样式:
    //   false = 沿用单侧飞入(默认,和其它音符一致:整只鸟从左或右一侧飞到圆心)
    //   true  = 「双翼汇合」:右翼从右飞入、左翼从左飞入,在圆心叠合拼成整只鸟(仍一次判定)
    // 审核组员【不用改代码】:在 Resources/LijiangEchoBattleSettings 资源上勾选即可(见 LijiangEchoBattleSettings)。
    // 找不到该资源时用这里的默认值兜底。
    private const bool DoubleNoteMirrorConvergeDefault = false;
    private bool doubleNoteMirrorConverge = DoubleNoteMirrorConvergeDefault;

    /// <summary>创建左右手:轴心在画面偏下两侧,手臂朝下藏起;打击时向上旋转击中心圆环。</summary>
    private void BuildBattleHands()
    {
        leftHandPivot = CreateBattleHand("battle/7左手", "左手", -1f, out leftHandRenderer);
        rightHandPivot = CreateBattleHand("battle/7右手", "右手", 1f, out rightHandRenderer);
        leftHandStrikeTimer = 0f;
        rightHandStrikeTimer = 0f;
    }

    private Transform CreateBattleHand(string art, string handName, float sideSign, out SpriteRenderer handRenderer)
    {
        GameObject pivotObject = new GameObject(handName + "轴");
        pivotObject.transform.SetParent(stageRoot, false);
        pivotObject.transform.localPosition = new Vector3(sideSign * HandPivotSide, HandPivotY, HandPivotZ); // 偏下两侧的轴心(玩法平面深度)
        pivotObject.transform.localRotation = Quaternion.Euler(0f, 0f, -sideSign * HandRestAngle); // 平时朝各自外下方甩出
        spawnedObjects.Add(pivotObject);

        // 优先用可编辑手部 Prefab:Resources/LijiangEchoNotes/Hand_Left / Hand_Right。
        // 手的位置/大小/离镜头深度全由你在 Prefab 里摆(轴心只负责旋转甩击);运行时只驱动旋转+淡入。
        string handPrefabName = sideSign < 0f ? "Hand_Left" : "Hand_Right";
        GameObject handPrefab = Resources.Load<GameObject>("LijiangEchoNotes/" + handPrefabName);
        if (handPrefab != null)
        {
            GameObject inst = Instantiate(handPrefab, pivotObject.transform, false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = Quaternion.identity;
            handRenderer = inst.GetComponentInChildren<SpriteRenderer>();
            if (handRenderer != null)
            {
                Color c0 = handRenderer.color;
                handRenderer.color = new Color(c0.r, c0.g, c0.b, 0f); // 初始透明,击打时淡入
            }

            return pivotObject.transform;
        }

        GameObject hand = AddIcon(art, handName, Vector3.zero, HandVisualHeight, 240, 0f); // 初始全透明,放大
        hand.transform.SetParent(pivotObject.transform, false);
        hand.transform.localRotation = Quaternion.identity;
        handRenderer = hand.GetComponent<SpriteRenderer>();
        // 关键:把手的"可见部分中心"放到手臂末端。手图不透明内容常偏在一角,
        // 直接把 pivot 摆到臂端会让可见的手甩到画面外(之前"太偏下看不到"的原因)。
        Vector3 handVisible = GetSpriteVisibleCenter(handRenderer.sprite);
        Vector3 scaledVisible = Vector3.Scale(handVisible, hand.transform.localScale);
        hand.transform.localPosition = new Vector3(0f, HandArmLength, 0f) - scaledVisible;
        return pivotObject.transform;
    }

    private void UpdateBattleHands()
    {
        // 长按期间,对应一侧的手要"停留在击打顶点"直到松手/时长到 —— 由当前 holdActive + 该音符所在侧
        // 派生(松手/完成后 holdActive 变 false,手自然落回),无需在各处手动清标志。
        bool holdingLeft = holdActive && heldNote != null && heldNote.Side <= 0f;
        bool holdingRight = holdActive && heldNote != null && heldNote.Side > 0f;
        UpdateBattleHand(leftHandPivot, leftHandRenderer, ref leftHandStrikeTimer, holdingLeft, -1f);
        UpdateBattleHand(rightHandPivot, rightHandRenderer, ref rightHandStrikeTimer, holdingRight, 1f);
    }

    private void UpdateBattleHand(Transform pivot, SpriteRenderer hand, ref float timer, bool holding, float sideSign)
    {
        if (pivot == null)
        {
            return;
        }

        float rest = -sideSign * HandRestAngle;     // 各自外下方甩出、藏起
        float strike = sideSign * HandStrikeAngle;  // 向上旋转、手落到中心圆环
        float angle = rest;
        float alpha = 0f; // 平时全透明(VR 里镜头外也看得见,靠透明来隐藏)
        if (timer > 0f)
        {
            // 普通击打:progress 0→1,swing = sin(progress·π) = 0→1→0(升起→落回)。
            // 长按:升到顶点(progress=0.5,swing=1)后把 timer 冻结在半程,让手停在圆环上;
            //       松手/时长到 → holding=false → timer 继续走完后半程 → 手落回并淡出。
            float progress = 1f - Mathf.Clamp01(timer / HandStrikeDuration);
            if (holding && progress >= 0.5f)
            {
                timer = HandStrikeDuration * 0.5f; // 钉在顶点保持
                progress = 0.5f;
            }
            else
            {
                timer -= Time.deltaTime;
            }

            float swing = Mathf.Sin(progress * Mathf.PI);
            angle = Mathf.Lerp(rest, strike, swing);
            alpha = Mathf.Clamp01(swing * 2.2f); // 挥起时更早显现、看得更清,落回时淡出
        }

        if (HandDebugAlwaysVisible)
        {
            alpha = Mathf.Max(alpha, 0.7f); // 诊断:一直可见,方便看清手停在哪、多大
        }

        pivot.localRotation = Quaternion.Euler(0f, 0f, angle);
        if (hand != null)
        {
            Color c = hand.color;
            hand.color = new Color(c.r, c.g, c.b, alpha);
        }
    }

    /// <summary>触发击打挥手:side&lt;0 左手,side&gt;0 右手,side==0 双手(双击)。</summary>
    private void TriggerHandStrike(float side)
    {
        if (side <= 0f)
        {
            leftHandStrikeTimer = HandStrikeDuration;
        }

        if (side >= 0f)
        {
            rightHandStrikeTimer = HandStrikeDuration;
        }
    }

    private void ShowBattle()
    {
        ResetStage(Stage.Battle);
        StopStageLoop();
        LoadChartIfAvailable();
        score = 0;
        combo = 0;
        nextSpawnIndex = 0;
        nextNoteIndex = 0;

        // 编辑器"从播放头试玩":从指定秒起播、跳过倒计时(用一次即清)。
        battleSeekTime = PlayerPrefs.GetFloat("LJ_DebugBattleStartTime", -1f);
        PlayerPrefs.DeleteKey("LJ_DebugBattleStartTime");

        // Path B 第 1 步:优先采用已烘焙的可编辑战斗背景;没有则运行时构建(视觉不变)。
        if (!TryAdoptBakedBattleBackground())
        {
            BuildBattleBackground();
        }

        AddIcon("ui/settings", "左上设置入口", new Vector3(-2.42f, 1.05f, -0.38f), 0.24f, 70, 0.9f);

        // 中间圆环:和音符同一套"prefab 优先"逻辑 —— 本关 Ring_level{关} → 全局 Ring_Center → 贴图兜底。
        // 用 Prefab 你在编辑器里怎么摆就怎么显示(更稳,和音符迁移一样);没有 Prefab 就用原来的贴图,观感不变。
        GameObject ringPrefab = Resources.Load<GameObject>("LijiangEchoNotes/Ring_level" + selectedLevel)
            ?? Resources.Load<GameObject>("LijiangEchoNotes/Ring_Center");
        GameObject centerRingObject;
        if (ringPrefab != null)
        {
            centerRingObject = Instantiate(ringPrefab, stageRoot, false);
            centerRingObject.name = "中央节奏判定双圆环(Prefab)";
            centerRingObject.transform.localPosition = new Vector3(0f, 0f, -0.82f);
            centerRingObject.transform.localRotation = Quaternion.identity;
            spawnedObjects.Add(centerRingObject);
            ringRenderer = centerRingObject.GetComponentInChildren<SpriteRenderer>();
            ringTransform = centerRingObject.transform;
        }
        else
        {
            centerRingObject = AddIcon(
                "battle/hit_ring_center",
                "中央节奏判定双圆环",
                new Vector3(0f, 0f, -0.82f),
                HitRingVisibleHeight,
                190,
                1f);
            ringRenderer = centerRingObject.GetComponent<SpriteRenderer>();
            ringTransform = centerRingObject.transform;
        }
        ringBaseScale = ringTransform.localScale;

        // 圆环反馈脚本:Prefab 上挂了(或其子物体上有)就用它;没挂就补一个默认(OnBeat 脉动=旧版观感,命中不额外闪)。
        ringFeedback = centerRingObject.GetComponentInChildren<LijiangEchoRingFeedback>();
        if (ringFeedback == null)
        {
            ringFeedback = centerRingObject.AddComponent<LijiangEchoRingFeedback>();
        }
        ringFeedback.Init(ringRenderer, ringTransform, ringBaseScale);

        RectInt[] traceCrops =
        {
            new RectInt(273, 2314, 1951, 2547),
            new RectInt(1822, 2125, 2973, 2185),
            new RectInt(995, 836, 1335, 1359)
        };
        // 右下角"待描绘纹样":本关有 Trace_level{关卡} Prefab 就用你摆的静态 Prefab(跳过进度动画);
        // 没有则保持原动态纹样(会随描绘进度换图/变亮)。
        GameObject tracePrefab = Resources.Load<GameObject>("LijiangEchoNotes/Trace_level" + selectedLevel);
        if (tracePrefab != null)
        {
            GameObject traceInst = Instantiate(tracePrefab, stageRoot, false);
            traceInst.name = "待描绘纹样(Prefab)";
            traceInst.transform.localPosition = new Vector3(1.84f, -0.82f, -0.42f);
            traceInst.transform.localRotation = Quaternion.identity;
            spawnedObjects.Add(traceInst);
            patternRenderer = null; // 用 Prefab 时不再由 UpdatePatternProgress 动态改图
        }
        else
        {
            GameObject patternObject = AddCroppedSprite(
                tracePaths[selectedLevel],
                "待描绘纹样",
                traceCrops[selectedLevel],
                new Vector3(1.84f, -0.82f, -0.42f),
                0.34f,
                62,
                0.72f,
                false);
            patternRenderer = patternObject.GetComponent<SpriteRenderer>();
        }

        AddSolidRect("顶部进度底线", new Vector3(0f, 1.04f, -0.44f), 2.94f, 0.026f, new Color(1f, 1f, 1f, 0.42f), 64);
        GameObject progressFill = AddSolidRect("战斗进度填充", new Vector3(-1.47f, 1.04f, -0.45f), 0.02f, 0.034f, new Color(1f, 0.86f, 0.35f, 0.82f), 65);
        progressFillRenderer = progressFill.GetComponent<SpriteRenderer>();
        AddIcon("battle/progress_bar", "战斗顶部进度条美术", new Vector3(0f, 1.04f, -0.46f), 0.11f, 66, 0.96f);

        GameObject countdownObject = AddIcon("ui/number_3", "倒计时数字", new Vector3(0f, -0.02f, -0.48f), 0.72f, 80, 0.96f);
        countdownRenderer = countdownObject.GetComponent<SpriteRenderer>();

        BuildBattleHands();
        RegisterMotion(countdownObject, MotionKind.Pulse, 0.028f, 6.5f, 0f);
        scoreText = AddText("分数 0    连击 0", new Vector3(-1.68f, 0.87f, -0.48f), 0.017f, new Color(1f, 0.93f, 0.72f), 68);
        feedbackText = AddText("", new Vector3(0f, -1.05f, -0.48f), 0.022f, new Color(1f, 0.93f, 0.7f), 60);
    }

    /// <summary>
    /// 启动时若配置了战斗背景场景(ExternalBattleSceneName 或默认名),且已加入 Build Settings,
    /// 就附加加载它,进入战斗时会被 TryAdoptBakedBattleBackground 自动采用。找不到则静默跳过
    /// (战斗改用运行时构建背景)。
    /// </summary>
    private IEnumerator PreloadBattleSceneIfConfigured()
    {
        string sceneName = string.IsNullOrEmpty(ExternalBattleSceneName) ? DefaultBattleSceneName : ExternalBattleSceneName;
        if (string.IsNullOrEmpty(sceneName) || SceneManager.GetSceneByName(sceneName).isLoaded)
        {
            yield break;
        }

        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.Log($"[漓江回声] 未在 Build Settings 找到战斗背景场景「{sceneName}」;战斗将用运行时构建背景(如需用烘焙场景,把它加入 Build Settings)。");
            yield break;
        }

        yield return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        DisablePreviewCamerasInScene(SceneManager.GetSceneByName(sceneName)); // 立即关预览相机,免得抢 XR 相机
        Debug.Log($"[漓江回声] 已附加加载战斗背景场景「{sceneName}」,进入战斗时将自动采用。");
    }

    /// <summary>
    /// 双模式采用:在已加载的任意场景里查找烘焙的战斗背景根(以子节点「怪物分层」为标志),
    /// 采用则挂到 stageRoot 下、驱动其 LijiangEchoMotion 动效、把怪物抖动锚到它;返回 true。
    /// 采用的根不加入 spawnedObjects,故 ResetStage 不会销毁它,可跨重入复用。找不到返回 false。
    /// </summary>
    private bool TryAdoptBakedBattleBackground()
    {
        if (adoptedBattleRoot == null)
        {
            adoptedBattleRoot = FindBakedBattleRoot();
            if (adoptedBattleRoot == null)
            {
                return false;
            }

            DisablePreviewCamerasInScene(adoptedBattleRoot.gameObject.scene);
        }

        // 烘焙时各层 localPosition/scale 是相对 stageRoot 记录后在根下重建的,
        // 故把根挂到 stageRoot 下并置为单位变换,子层即回到原本相对位置。
        if (adoptedBattleRoot.parent != stageRoot)
        {
            adoptedBattleRoot.SetParent(stageRoot, false);
            adoptedBattleRoot.localPosition = Vector3.zero;
            adoptedBattleRoot.localRotation = Quaternion.identity;
            adoptedBattleRoot.localScale = Vector3.one;
        }

        int motionCount = 0;
        foreach (LijiangEchoMotion motion in adoptedBattleRoot.GetComponentsInChildren<LijiangEchoMotion>(true))
        {
            RegisterMotion(motion.gameObject, MapStageKitMotionKind(motion.kind), motion.amplitude, motion.speed, motion.phase);
            motionCount++;
        }

        Transform monster = FindDeepChildByName(adoptedBattleRoot, BattleBackgroundMarkerName);
        if (monster != null)
        {
            monsterRoot = monster;
        }

        Debug.Log($"[漓江回声] 战斗采用已烘焙背景「{adoptedBattleRoot.name}」,动效层 {motionCount} 个" +
                  (motionCount == 0 ? "(未挂 LijiangEchoMotion → 背景静止;跑菜单「为战斗背景补挂动效组件」即可让它动)。" : "。"));
        return true;
    }

    /// <summary>在所有已加载场景的根物体里,找出含「怪物分层」子节点的烘焙战斗背景根;排除本控制器 stageRoot 自身子树。</summary>
    private Transform FindBakedBattleRoot()
    {
        for (int s = 0; s < SceneManager.sceneCount; s++)
        {
            Scene scene = SceneManager.GetSceneAt(s);
            if (!scene.isLoaded)
            {
                continue;
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (stageRoot != null && (root.transform == stageRoot || root.transform.IsChildOf(stageRoot)))
                {
                    continue; // 跳过运行时自建内容
                }

                if (FindDeepChildByName(root.transform, BattleBackgroundMarkerName) != null)
                {
                    return root.transform;
                }
            }
        }

        return null;
    }

    private Transform FindDeepChildByName(Transform current, string targetName)
    {
        if (current.name == targetName)
        {
            return current;
        }

        for (int i = 0; i < current.childCount; i++)
        {
            Transform found = FindDeepChildByName(current.GetChild(i), targetName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>关掉烘焙场景里带的预览相机,避免和 XR 相机冲突。</summary>
    private void DisablePreviewCamerasInScene(Scene scene)
    {
        if (!scene.IsValid())
        {
            return;
        }

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Camera cam in root.GetComponentsInChildren<Camera>(true))
            {
                cam.gameObject.SetActive(false);
            }
        }
    }

    /// <summary>把 StageKit 的 MotionKind 映射到本控制器的 MotionKind(两枚举同序同名)。</summary>
    private MotionKind MapStageKitMotionKind(LijiangEchoStageKit.MotionKind kind)
    {
        switch (kind)
        {
            case LijiangEchoStageKit.MotionKind.FloatY: return MotionKind.FloatY;
            case LijiangEchoStageKit.MotionKind.FloatX: return MotionKind.FloatX;
            case LijiangEchoStageKit.MotionKind.Pulse: return MotionKind.Pulse;
            case LijiangEchoStageKit.MotionKind.Flame: return MotionKind.Flame;
            case LijiangEchoStageKit.MotionKind.Monster: return MotionKind.Monster;
            case LijiangEchoStageKit.MotionKind.Wing: return MotionKind.Wing;
            case LijiangEchoStageKit.MotionKind.Hand: return MotionKind.Hand;
            default: return MotionKind.FloatY;
        }
    }

    /// <summary>
    /// 战斗静态舞台背景(远山/人群/怪物/火焰/祭坛/装饰手/边框)。抽成独立方法,
    /// 为后续"战斗场景化"(把这块烘焙成可在场景里直接摆位的物体)做准备。
    /// 未采用烘焙背景时(TryAdoptBakedBattleBackground 返回 false)运行时构建,视觉与之前完全一致。
    /// </summary>
    private void BuildBattleBackground()
    {
        AddLayer("ui/mountain_background", "战斗远景底", new Vector3(0f, 0f, 0.08f), WideStripWidth, -30, 0.08f);
        AddLayer("battle/mountain_left_1", "左山一", new Vector3(0f, 0.03f, 0.04f), WideStripWidth, -22, 0.96f);
        AddLayer("battle/mountain_left_2", "左山二", new Vector3(0f, 0.03f, 0.03f), WideStripWidth, -21, 0.9f);
        AddLayer("battle/mountain_right_1", "右山一", new Vector3(0f, 0.03f, 0.02f), WideStripWidth, -20, 0.96f);
        AddLayer("battle/mountain_right_2", "右山二", new Vector3(0f, 0.03f, 0.01f), WideStripWidth, -19, 0.9f);
        AddLayer("battle/front_mountain_left_1", "左前山一", new Vector3(0f, 0.03f, -0.01f), WideStripWidth, -16);
        AddLayer("battle/front_mountain_left_2", "左前山二", new Vector3(0f, 0.03f, -0.02f), WideStripWidth, -15);
        AddLayer("battle/front_mountain_right_1", "右前山一", new Vector3(0f, 0.03f, -0.03f), WideStripWidth, -14);
        AddLayer("battle/front_mountain_right_2", "右前山二", new Vector3(0f, 0.03f, -0.04f), WideStripWidth, -13);

        AddLayer("battle/people_left_back", "左侧人群后", new Vector3(0f, 0.03f, -0.06f), WideStripWidth, -8, 0.78f);
        AddLayer("battle/people_left_mid", "左侧人群中", new Vector3(0f, 0.03f, -0.07f), WideStripWidth, -7, 0.84f);
        AddLayer("battle/people_left_front", "左侧人群前", new Vector3(0f, 0.03f, -0.08f), WideStripWidth, -6, 0.94f);
        AddLayer("battle/people_right_back", "右侧人群后", new Vector3(0f, 0.03f, -0.09f), WideStripWidth, -8, 0.78f);
        AddLayer("battle/people_right_mid", "右侧人群中", new Vector3(0f, 0.03f, -0.1f), WideStripWidth, -7, 0.84f);
        AddLayer("battle/people_right_front", "右侧人群前", new Vector3(0f, 0.03f, -0.11f), WideStripWidth, -6, 0.94f);

        monsterRoot = new GameObject("怪物分层").transform;
        monsterRoot.SetParent(stageRoot, false);
        monsterRoot.localPosition = new Vector3(0f, -0.04f, -0.18f);
        monsterRoot.localRotation = Quaternion.identity;
        monsterRoot.localScale = Vector3.one * 0.66f;
        spawnedObjects.Add(monsterRoot.gameObject);

        GameObject fullBoss = AddLayer("battle/monster_full", "怪物完整底层", new Vector3(0f, 0f, -0.005f), WideStripWidth, 24, 0.18f, monsterRoot);
        GameObject leftWing = AddLayer("battle/monster_wing_left", "怪物左翼", new Vector3(0f, 0f, -0.01f), WideStripWidth, 26, 0.98f, monsterRoot);
        GameObject rightWing = AddLayer("battle/monster_wing_right", "怪物右翼", new Vector3(0f, 0f, -0.02f), WideStripWidth, 26, 0.98f, monsterRoot);
        GameObject body = AddLayer("battle/monster_body", "怪物身体", new Vector3(0f, 0f, -0.03f), WideStripWidth, 30, 1f, monsterRoot);
        GameObject arms = AddLayer("battle/monster_arms", "怪物手臂合层", new Vector3(0f, 0f, -0.04f), WideStripWidth, 31, 0.24f, monsterRoot);
        GameObject leftTopUpper = AddLayer("battle/monster_left_top_upper_arm", "怪物左上大臂", new Vector3(0f, 0f, -0.05f), WideStripWidth, 34, 1f, monsterRoot);
        GameObject leftTopFore = AddLayer("battle/monster_left_top_forearm", "怪物左上小臂", new Vector3(0f, 0f, -0.06f), WideStripWidth, 36, 1f, monsterRoot);
        GameObject rightTopUpper = AddLayer("battle/monster_right_top_upper_arm", "怪物右上大臂", new Vector3(0f, 0f, -0.07f), WideStripWidth, 34, 1f, monsterRoot);
        GameObject rightTopFore = AddLayer("battle/monster_right_top_forearm", "怪物右上小臂", new Vector3(0f, 0f, -0.08f), WideStripWidth, 36, 1f, monsterRoot);
        GameObject leftBottomUpper = AddLayer("battle/monster_left_bottom_upper_arm", "怪物左下大臂", new Vector3(0f, 0f, -0.09f), WideStripWidth, 33, 1f, monsterRoot);
        GameObject leftBottomFore = AddLayer("battle/monster_left_bottom_forearm", "怪物左下小臂", new Vector3(0f, 0f, -0.1f), WideStripWidth, 35, 1f, monsterRoot);
        GameObject rightBottomUpper = AddLayer("battle/monster_right_bottom_upper_arm", "怪物右下大臂", new Vector3(0f, 0f, -0.11f), WideStripWidth, 33, 1f, monsterRoot);
        GameObject rightBottomFore = AddLayer("battle/monster_right_bottom_forearm", "怪物右下小臂", new Vector3(0f, 0f, -0.12f), WideStripWidth, 35, 1f, monsterRoot);
        RegisterMotion(fullBoss, MotionKind.Monster, 0.012f, 1.2f, 0.2f);
        RegisterMotion(leftWing, MotionKind.Wing, 0.026f, 2.8f, 0f);
        RegisterMotion(rightWing, MotionKind.Wing, 0.026f, 2.8f, 1.4f);
        RegisterMotion(body, MotionKind.Monster, 0.018f, 1.4f, 0.6f);
        RegisterMotion(arms, MotionKind.Hand, 0.018f, 4.2f, 1.1f);
        // 关节修复:大臂与小臂是各自独立的整帧图层,若给不同的振幅/频率/相位,
        // 两段每帧位移/旋转量不同 → 肘关节"分开又合上"。这里让同一条手臂的
        // 小臂与大臂共用完全相同的运动参数,两段作为刚体一起动,关节始终贴合。
        // 各条手臂之间仍用不同相位,保持整体的错落生动。
        RegisterMotion(leftTopUpper, MotionKind.Hand, 0.022f, 4.6f, 0.2f);
        RegisterMotion(leftTopFore, MotionKind.Hand, 0.022f, 4.6f, 0.2f);
        RegisterMotion(rightTopUpper, MotionKind.Hand, 0.022f, 4.7f, 1.1f);
        RegisterMotion(rightTopFore, MotionKind.Hand, 0.022f, 4.7f, 1.1f);
        RegisterMotion(leftBottomUpper, MotionKind.Hand, 0.02f, 4.1f, 2.4f);
        RegisterMotion(leftBottomFore, MotionKind.Hand, 0.02f, 4.1f, 2.4f);
        RegisterMotion(rightBottomUpper, MotionKind.Hand, 0.02f, 4.2f, 3.2f);
        RegisterMotion(rightBottomFore, MotionKind.Hand, 0.02f, 4.2f, 3.2f);

        GameObject fireWide = AddLayer("battle/fire_wide", "宽火焰", new Vector3(0f, -0.42f, -0.22f), WideStripWidth, 16, 0.72f);
        GameObject fireNarrow = AddLayer("battle/fire_narrow", "火焰光", new Vector3(0f, -0.36f, -0.24f), 2.15f, 17, 0.48f);
        RegisterMotion(fireWide, MotionKind.Flame, 0.035f, 5.8f, 0f);
        RegisterMotion(fireNarrow, MotionKind.Flame, 0.05f, 7.2f, 1.3f);

        AddLayer("battle/altar_circle", "祭坛圆盘", new Vector3(0f, 0.03f, -0.26f), WideStripWidth, 22, 0.38f);
        AddLayer("battle/hand_left", "左手", new Vector3(0f, 0.03f, -0.28f), WideStripWidth, 18, 0.66f);
        AddLayer("battle/hand_right", "右手", new Vector3(0f, 0.03f, -0.29f), WideStripWidth, 18, 0.66f);
        AddLayer("battle/foreground_hand_left", "左前景手", new Vector3(0f, 0.03f, -0.31f), WideStripWidth, 40, 0.52f);
        AddLayer("battle/foreground_hand_right", "右前景手", new Vector3(0f, 0.03f, -0.32f), WideStripWidth, 40, 0.52f);
        AddLayer("battle/battle_border", "战斗边框", new Vector3(0f, 0.03f, -0.35f), WideStripWidth, 12, 0.08f);

    }

    private void UpdateBattle()
    {
        UpdateControllerMotion();
        float countdownTime = stageTimer - 3f;
        if (countdownTime < 0f && battleSeekTime < 0f) // 从播放头试玩时跳过倒计时,直接起播
        {
            if (countdownRenderer != null)
            {
                int count = Mathf.Clamp(Mathf.CeilToInt(-countdownTime), 1, 3);
                countdownRenderer.sprite = GetSprite("ui/number_" + count, true);
                countdownRenderer.enabled = true;
                countdownRenderer.color = new Color(1f, 1f, 1f, 0.95f);
            }

            UpdateRingVisual(0f);
            return;
        }

        if (!battleMusicStarted)
        {
            StartBattleMusic();
        }

        float beatTime = GetBattleMusicTime();
        EditorBattleTime = beatTime; // 供编辑器时间轴跟随

        if (countdownRenderer != null)
        {
            countdownRenderer.enabled = false;
        }

        SpawnDueNotes(beatTime);
        UpdateNotes(beatTime);

        ProcessHoldNote(beatTime);

        while (!holdActive && nextNoteIndex < noteTimes.Length && beatTime - noteTimes[nextNoteIndex] > 0.34f)
        {
            combo = 0;
            MarkPassedNote(nextNoteIndex);
            nextNoteIndex++;
            SetFeedback("错过", new Color(0.95f, 0.55f, 0.9f));
        }

        if (!holdActive && nextNoteIndex < noteTimes.Length)
        {
            NoteKind kind = GetNoteKind(nextNoteIndex);
            float diff = Mathf.Abs(beatTime - noteTimes[nextNoteIndex]);
            if (kind == NoteKind.Hold)
            {
                if (diff <= 0.3f && BattleHoldHeld())
                {
                    BeginHoldNote();
                }
            }
            else
            {
                bool performed = kind == NoteKind.Swipe
                    ? BattleSwipePerformed()
                    : BattleStrikePressed();
                if (performed)
                {
                    if (diff <= 0.16f)
                    {
                        score += kind == NoteKind.Swipe ? 150 : 120;
                        combo++;
                        HitCurrentNote(kind == NoteKind.Swipe ? "挥划完美" : "完美", new Color(1f, 0.96f, 0.45f));
                    }
                    else if (diff <= 0.31f)
                    {
                        score += kind == NoteKind.Swipe ? 95 : 70;
                        combo++;
                        HitCurrentNote(kind == NoteKind.Swipe ? "挥划命中" : "命中", new Color(0.7f, 1f, 0.9f));
                    }
                    else
                    {
                        combo = 0;
                        SetFeedback(beatTime < noteTimes[nextNoteIndex] ? "太早" : "太晚", new Color(0.95f, 0.58f, 0.88f));
                    }
                }
            }
        }

        if (hitFlashTimer > 0f)
        {
            hitFlashTimer -= Time.deltaTime;
            if (monsterRoot != null)
            {
                float shake = Mathf.Sin(Time.time * 65f) * hitFlashTimer * 0.035f;
                monsterRoot.localPosition = new Vector3(shake, -0.04f, -0.18f);
            }
        }
        else if (monsterRoot != null)
        {
            monsterRoot.localPosition = new Vector3(0f, -0.04f, -0.18f);
        }

        UpdateBattleHands();
        UpdateScheduledSfx(); // 处理延时音效(如双击的第二声)
        UpdateComboRipples(); // 连击≥5 的黄色荡漾光环
        UpdateRingVisual(beatTime);
        UpdatePatternProgress();
        UpdateProgressFill();
        UpdateScoreText();
        UpdateFeedbackFade();

        if (IsBattleMusicFinished())
        {
            battleEndingTimer += Time.deltaTime;
            if (battleEndingTimer >= 0.72f)
            {
                ShowCard();
            }
        }
    }

    private void SpawnDueNotes(float beatTime)
    {
        while (nextSpawnIndex < noteTimes.Length && noteTimes[nextSpawnIndex] - beatTime <= NoteApproachTime)
        {
            float side = nextSpawnIndex % 2 == 0 ? -1f : 1f;
            float startX = side * 2.26f;
            float targetX = 0f; // 用户反馈:音符最终飞到中心原点圆点(不再停在一侧)
            NoteKind kind = GetNoteKind(nextSpawnIndex);

            // 双击「镜像汇合」:用同一只鸟纹 Prefab —— 原体(=右翼)固定从右侧飞入,再生成一只水平镜像
            // 分身(=左翼)从左侧飞入,两只对称汇合到圆心拼成整鸟。原体永远走右、分身永远走左。
            bool mirrorConverge = doubleNoteMirrorConverge && kind == NoteKind.Double;
            if (mirrorConverge)
            {
                side = 1f;       // 原体固定走右侧
                startX = 2.26f;  // 从右进入(分身取 -startX,从左进入)
            }

            // 可编辑纹样 Prefab 优先:Resources/LijiangEchoNotes/Note_鱼/鸟/蛇/蛙 存在就用它。
            // 视觉(贴图/裁剪/大小/居中/光晕)完全由 Prefab 决定 —— 你在编辑器里怎么摆,游戏里就怎么显示;
            // 运行时只驱动根节点飞入位置 + 整体淡入,不再有任何运行时裁剪/居中/读像素。
            GameObject notePrefab = LoadNotePrefab(kind);
            if (notePrefab != null)
            {
                GameObject inst = Instantiate(notePrefab, stageRoot, false);
                inst.name = NotePrefabName(kind) + "_" + nextSpawnIndex;
                inst.transform.localPosition = new Vector3(startX, 0f, -0.94f);
                inst.transform.localRotation = Quaternion.identity;
                spawnedObjects.Add(inst); // 交给 ResetStage 统一清理

                SpriteRenderer[] rends = inst.GetComponentsInChildren<SpriteRenderer>(true);
                float[] baseA = new float[rends.Length];
                for (int r = 0; r < rends.Length; r++)
                {
                    baseA[r] = rends[r] != null ? rends[r].color.a : 1f;
                }

                RhythmNote prefabNote = new RhythmNote
                {
                    ChartIndex = nextSpawnIndex,
                    HitTime = noteTimes[nextSpawnIndex],
                    StartX = startX,
                    TargetX = targetX,
                    Side = side,
                    Kind = kind,
                    PrefabRoot = inst.transform,
                    AllRenderers = rends,
                    AllRenderersBaseAlpha = baseA,
                    Renderer = rends.Length > 0 ? rends[0] : null,
                    Judged = false
                };

                // 双击「镜像汇合」:生成一只对侧、水平镜像的纯视觉分身(=左翼),从左飞入。
                // 只是视觉,不进 activeNotes、不参与判定;随本体一起淡入、一起销毁。
                if (mirrorConverge)
                {
                    GameObject twin = Instantiate(notePrefab, stageRoot, false);
                    twin.name = inst.name + "_镜像分身";
                    twin.transform.localPosition = new Vector3(-startX, 0f, -0.94f); // 从左侧起步
                    twin.transform.localRotation = Quaternion.identity;
                    Vector3 ts = twin.transform.localScale;
                    twin.transform.localScale = new Vector3(-Mathf.Abs(ts.x), ts.y, ts.z); // 水平镜像=左翼
                    spawnedObjects.Add(twin);

                    SpriteRenderer[] twinRends = twin.GetComponentsInChildren<SpriteRenderer>(true);
                    float[] twinBaseA = new float[twinRends.Length];
                    for (int r = 0; r < twinRends.Length; r++)
                    {
                        twinBaseA[r] = twinRends[r] != null ? twinRends[r].color.a : 1f;
                    }

                    prefabNote.MirrorTwin = twin.transform;
                    prefabNote.MirrorTwinRenderers = twinRends;
                    prefabNote.MirrorTwinBaseAlpha = twinBaseA;
                }

                activeNotes.Add(prefabNote);
                nextSpawnIndex++;
                continue; // 用 Prefab,跳过下面的运行时代码生成
            }

            // 用户反馈:单击用鱼纹。select/fish_symbol 是独立整图,传超范围矩形 → GetCroppedSprite
            // 会夹到整图,即用整张鱼纹(无需知道其像素尺寸)。
            string resourcePath = "select/fish_symbol";
            RectInt crop = new RectInt(0, 0, 100000, 100000);
            // 各类音符占圆环的大小(fitByMaxDimension:较大边=targetHeight)。用户反馈:
            // 蛙纹偏大、其余偏小 → 鱼/蛇/鸟放大、蛙缩小,整体更均衡。要再调就改这几个 targetHeight。
            float startHeight = 0.40f;
            float targetHeight = HitBlockVisibleHeight; // 鱼纹 0.50
            string objectName = "鱼纹单击_" + nextSpawnIndex;
            if (kind == NoteKind.Hold)
            {
                resourcePath = "pattern/snake_done";
                crop = SnakeDoneCrop;
                startHeight = 0.44f;
                targetHeight = 0.60f; // 蛇纹放大
                objectName = "蛇纹长按_" + nextSpawnIndex;
            }
            else if (kind == NoteKind.Swipe)
            {
                resourcePath = "battle/frog_swipe";
                crop = FrogSwipeCrop;
                startHeight = 0.22f;
                targetHeight = 0.30f; // 蛙纹缩小(之前偏大)
                objectName = "蛙纹滑动_" + nextSpawnIndex;
            }
            else if (kind == NoteKind.Double)
            {
                // P4/P6：双击音符——用不同纹样(鸟纹)与单击(hit_block)在视觉上区分。
                // 仅当 doubleNoteIndices 里填了 index 才会出现；输入仍按单击命中处理。
                resourcePath = "pattern/bird_done";
                crop = BirdDoneCrop;
                startHeight = 0.42f;
                targetHeight = 0.56f; // 鸟纹放大
                objectName = "双击纹样_" + nextSpawnIndex;
            }

            // (落点居中改由 SetCroppedSpritePose 的 centerOnVisual 统一处理,不再单独给鱼纹加偏移)

            // 用户反馈:纹样整体做小一些
            startHeight *= NoteSizeScale;
            targetHeight *= NoteSizeScale;

            GameObject noteObject = AddCroppedSprite(
                resourcePath,
                objectName,
                crop,
                new Vector3(startX, 0f, -0.94f),
                startHeight,
                230,
                0.42f,
                false); // 不镜像:所有音符都朝正中心飞、不翻转,避免落点偏移
            SpriteRenderer noteRenderer = noteObject.GetComponent<SpriteRenderer>();

            // 所有打击纹样统一换白色剪影材质:用原图 alpha 当形状、输出纯白(renderer.color 控淡入淡出)。
            EnsureNoteMaterials();
            if (noteWhiteMaterial != null)
            {
                noteRenderer.sharedMaterial = noteWhiteMaterial;
            }

            // 加色柔光光晕:1 层金色、加色混合、柔和外扩。
            // 精灵已收紧到紧包围盒、pivot 即内容几何中心,光晕以本体原点(0)为放大中心即天然同心;
            // 不再用 alpha 质心(那会把光晕推离本体,造成"散射")。
            Vector3 spriteCenter = Vector3.zero;
            float[] glowScales = { 1.5f };
            float[] glowBase = { 0.42f };
            SpriteRenderer[] glowLayers = new SpriteRenderer[glowScales.Length];
            for (int gi = 0; gi < glowScales.Length; gi++)
            {
                GameObject glowObject = new GameObject("柔光光晕_" + gi);
                glowObject.transform.SetParent(noteObject.transform, false);
                // 以纹样可见中心为放大中心:偏移 = center*(1-scale),保证与本体同心。
                glowObject.transform.localPosition = new Vector3(
                    spriteCenter.x * (1f - glowScales[gi]),
                    spriteCenter.y * (1f - glowScales[gi]),
                    0.01f + gi * 0.01f);
                glowObject.transform.localScale = Vector3.one * glowScales[gi];
                SpriteRenderer glowRenderer = glowObject.AddComponent<SpriteRenderer>();
                glowRenderer.sprite = noteRenderer.sprite;
                glowRenderer.sortingOrder = noteRenderer.sortingOrder - (gi + 1);
                if (noteGlowMaterial != null)
                {
                    glowRenderer.sharedMaterial = noteGlowMaterial;
                }

                glowRenderer.color = new Color(1f, 0.86f, 0.42f, glowBase[gi]);
                glowLayers[gi] = glowRenderer;
            }

            RhythmNote note = new RhythmNote
            {
                ChartIndex = nextSpawnIndex,
                HitTime = noteTimes[nextSpawnIndex],
                StartX = startX,
                TargetX = targetX,
                Side = side,
                TargetHeight = targetHeight,
                Kind = kind,
                Renderer = noteRenderer,
                GlowLayers = glowLayers,
                GlowBaseAlpha = glowBase,
                Judged = false
            };
            activeNotes.Add(note);
            nextSpawnIndex++;
        }
    }

    private void UpdateNotes(float beatTime)
    {
        for (int i = activeNotes.Count - 1; i >= 0; i--)
        {
            RhythmNote note = activeNotes[i];
            if (note.PrefabRoot == null && note.Renderer == null)
            {
                activeNotes.RemoveAt(i);
                continue;
            }

            // 到达判定点自动播该类型音效(即使没打中也响),方便"听着谱面"调试节奏对齐。
            if (!note.Cued && beatTime >= note.HitTime)
            {
                PlayNoteCue(note.Kind);
                note.Cued = true;
            }

            float normalized = Mathf.Clamp01(1f - (note.HitTime - beatTime) / NoteApproachTime);
            float eased = Mathf.SmoothStep(0f, 1f, normalized);
            if (holdActive && heldNote == note)
            {
                normalized = 1f;
                eased = 1f;
            }
            Vector3 visibleCenter = new Vector3(
                Mathf.Lerp(note.StartX, note.TargetX, eased),
                Mathf.Sin((normalized + note.HitTime) * Mathf.PI * 2f) * 0.022f * (1f - eased),
                -0.94f);

            // 蛙纹(挥划)=「上跳」:飞入时从下方蓄势升到圆心;过判定点后「跳着离开」——沿抛物线向上跃出、
            // 略带斜向,越飞越高直到消失,呼应青蛙向上蹬地跳走的真实轨迹。
            if (note.Kind == NoteKind.Swipe)
            {
                float rise = Mathf.Lerp(-0.30f, 0f, eased);        // 起跳前从下方蓄势升到圆心
                visibleCenter.y += rise;

                float leave = Mathf.Clamp01((beatTime - note.HitTime) / 0.55f); // 判定点之后的离场进度
                if (leave > 0f)
                {
                    float arc = Mathf.Sin(leave * Mathf.PI * 0.5f);        // 前段快、末段缓的上跃
                    visibleCenter.y += arc * 0.62f;                        // 向上跃出的高度
                    visibleCenter.x += (note.Side == 0f ? 1f : note.Side) * leave * 0.14f; // 略带斜向更像跳走
                }
            }

            if (note.PrefabRoot != null)
            {
                // Prefab 音符:视觉全由 Prefab 决定,只驱动根位置 + 整体淡入。
                UpdatePrefabNote(note, visibleCenter, eased);
                if (!holdActive && beatTime - note.HitTime > 0.55f)
                {
                    DestroyNoteObject(note);
                    activeNotes.RemoveAt(i);
                }

                continue;
            }

            SetCroppedSpritePose(
                note.Renderer,
                visibleCenter,
                Mathf.Lerp(note.TargetHeight * 0.76f, note.TargetHeight, eased) *
                (holdActive && heldNote == note ? 1f + Mathf.Sin(Time.time * 8f) * 0.035f : 1f),
                Mathf.Lerp(0.42f, 1f, eased),
                false,   // 不镜像,朝正中心飞
                false,   // 质心二次居中已停用(见下方 prefab 化改造:改为编辑器可视化摆位)
                false);  // 暂时关闭"按较大边缩放"——它套在未收紧的整张画布上会把纹样缩到极小

            // 纹样纯白:渐显到全亮;进环略淡但仍清晰可见(不再淡到不可见)。
            // 长按音符另有 P3 视觉,此处不处理,避免叠加冲突。
            if (!(holdActive && heldNote == note))
            {
                float appear = Mathf.Clamp01(eased / 0.6f);
                float ringFade = Mathf.InverseLerp(0.80f, 1f, eased);
                float noteAlpha = Mathf.Lerp(appear, 0.6f, ringFade);
                note.Renderer.color = new Color(1f, 1f, 1f, noteAlpha); // 纯白
            }

            // 柔光光晕脉动:越接近判定越亮 + 呼吸脉动。多层各按自己的基础亮度一起脉动,
            // 加色叠出外扩、明亮、柔和的金色发光。
            if (note.GlowLayers != null)
            {
                float glowAppear = Mathf.Clamp01(eased / 0.55f);
                float glowPulse = 0.80f + 0.20f * Mathf.Abs(Mathf.Sin(Time.time * 6f));
                for (int gi = 0; gi < note.GlowLayers.Length; gi++)
                {
                    SpriteRenderer gr = note.GlowLayers[gi];
                    if (gr == null)
                    {
                        continue;
                    }

                    float baseA = (note.GlowBaseAlpha != null && gi < note.GlowBaseAlpha.Length) ? note.GlowBaseAlpha[gi] : 0.4f;
                    gr.color = new Color(1f, 0.86f, 0.42f, baseA * glowAppear * glowPulse);
                }
            }

            // P3/P6：长按音符「往上划出」消失。按住期间整体向上平移 + 渐隐 + 轻微缩短,
            // 做成"纹样向上飘走消失",而不是原地压扁(之前的纯高度收缩看着像被挤扁)。
            if (holdActive && heldNote == note && note.Renderer.sprite != null)
            {
                float holdRequired = GetHoldDuration(nextNoteIndex);
                float wipe = holdRequired > 0f ? Mathf.Clamp01(holdProgress / holdRequired) : 0f;
                Transform noteTransform = note.Renderer.transform;
                float up = wipe * note.TargetHeight * 1.6f; // 上移距离随进度增大
                noteTransform.localPosition += new Vector3(0f, up, 0f);
                Vector3 poseScale = noteTransform.localScale;
                float shrink = Mathf.Lerp(1f, 0.85f, wipe); // 轻微缩短,不做成压扁
                noteTransform.localScale = new Vector3(poseScale.x, poseScale.y * shrink, poseScale.z);
                note.Renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(1f - wipe)); // 渐隐
            }

            if (!holdActive && beatTime - note.HitTime > 0.55f)
            {
                DestroyNoteObject(note);
                activeNotes.RemoveAt(i);
            }
        }
    }

    /// <summary>按音符类型播放音效:单击=hit×1,双击=hit×2(隔一小段),长按=snake,挥划=swipe。</summary>
    private void PlayNoteCue(NoteKind kind)
    {
        switch (kind)
        {
            case NoteKind.Double:
                PlaySfx("hit", 0.7f);
                ScheduleSfx("hit", 0.7f, 0.11f); // 第二声,凑成"双击"
                break;
            case NoteKind.Hold:
                PlaySfx("snake", 0.7f);
                break;
            case NoteKind.Swipe:
                PlaySfx("swipe", 0.6f);
                break;
            default:
                PlaySfx("hit", 0.7f); // 单击
                break;
        }
    }

    private void ScheduleSfx(string clip, float volume, float delay)
    {
        scheduledSfx.Add(new ScheduledSfx { Due = Time.time + delay, Clip = clip, Volume = volume });
    }

    private void UpdateScheduledSfx()
    {
        for (int i = scheduledSfx.Count - 1; i >= 0; i--)
        {
            if (Time.time >= scheduledSfx[i].Due)
            {
                PlaySfx(scheduledSfx[i].Clip, scheduledSfx[i].Volume);
                scheduledSfx.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// 连击 ≥5 时,从圆环中心周期性放出一圈黄色光环,向外扩散并渐隐;连击断了就停止再放
    /// (已生成的自然消失完)。做出"越连越燃"的荡漾叠加感。
    /// </summary>
    private void UpdateComboRipples()
    {
        if (combo >= 5 && ringTransform != null)
        {
            comboRippleTimer -= Time.deltaTime;
            if (comboRippleTimer <= 0f)
            {
                SpawnComboRipple();
                comboRippleTimer = 0.45f;
            }
        }

        for (int i = comboRipples.Count - 1; i >= 0; i--)
        {
            ComboRipple r = comboRipples[i];
            if (r.tr == null)
            {
                comboRipples.RemoveAt(i);
                continue;
            }

            r.age += Time.deltaTime;
            float p = Mathf.Clamp01(r.age / r.life);
            r.tr.localScale = ringBaseScale * Mathf.Lerp(0.9f, 2.6f, p); // 向外扩散
            if (r.sr != null)
            {
                r.sr.color = new Color(1f, 0.86f, 0.3f, (1f - p) * 0.5f); // 渐隐
            }

            if (p >= 1f)
            {
                Destroy(r.tr.gameObject);
                comboRipples.RemoveAt(i);
            }
        }
    }

    private void SpawnComboRipple()
    {
        GameObject go = new GameObject("连击光环");
        go.transform.SetParent(stageRoot, false);
        go.transform.localPosition = new Vector3(0f, 0f, -0.86f); // 圆环稍后
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = ringBaseScale;
        SpriteRenderer sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetSprite("battle/hit_ring_center", true);
        sr.sortingOrder = 185; // 圆环(190)之后
        EnsureNoteMaterials();
        if (noteGlowMaterial != null)
        {
            sr.sharedMaterial = noteGlowMaterial; // 加色柔光,荡漾发光感
        }

        sr.color = new Color(1f, 0.86f, 0.3f, 0.5f);
        spawnedObjects.Add(go);
        comboRipples.Add(new ComboRipple { tr = go.transform, sr = sr, age = 0f, life = 0.85f });
    }

    /// <summary>音符类型 → 全局纹样 Prefab 名(Resources/LijiangEchoNotes/ 下)。</summary>
    private string NotePrefabName(NoteKind kind)
    {
        switch (kind)
        {
            case NoteKind.Hold: return "Note_Snake";
            case NoteKind.Swipe: return "Note_Frog";
            case NoteKind.Double: return "Note_Bird";
            default: return "Note_Fish";
        }
    }

    private string NoteTypeKey(NoteKind kind)
    {
        switch (kind)
        {
            case NoteKind.Hold: return "hold";
            case NoteKind.Swipe: return "swipe";
            case NoteKind.Double: return "double";
            default: return "single";
        }
    }

    /// <summary>
    /// 加载音符纹样 Prefab:优先本关专属 Note_level{关卡}_{类型}(可在单个关卡里把某类型统一换成别的纹样),
    /// 没有则用全局 Note_鱼/鸟/蛇/蛙,再没有则回退代码生成。
    /// </summary>
    private GameObject LoadNotePrefab(NoteKind kind)
    {
        GameObject perLevel = Resources.Load<GameObject>("LijiangEchoNotes/Note_level" + selectedLevel + "_" + NoteTypeKey(kind));
        if (perLevel != null)
        {
            return perLevel;
        }

        return Resources.Load<GameObject>("LijiangEchoNotes/" + NotePrefabName(kind));
    }

    /// <summary>Prefab 音符每帧驱动:根节点到飞入位置 + 整体淡入(保持各精灵在 Prefab 里的相对透明)。</summary>
    private void UpdatePrefabNote(RhythmNote note, Vector3 pos, float eased)
    {
        if (note.PrefabRoot == null)
        {
            return;
        }

        note.PrefabRoot.localPosition = pos;
        float appear = Mathf.Clamp01(eased / 0.6f);
        float ringFade = Mathf.InverseLerp(0.80f, 1f, eased);
        float mul = Mathf.Lerp(appear, 0.9f, ringFade); // 出现→进环略淡但清晰
        if (note.AllRenderers != null)
        {
            for (int k = 0; k < note.AllRenderers.Length; k++)
            {
                SpriteRenderer sr = note.AllRenderers[k];
                if (sr == null)
                {
                    continue;
                }

                float baseA = (note.AllRenderersBaseAlpha != null && k < note.AllRenderersBaseAlpha.Length)
                    ? note.AllRenderersBaseAlpha[k]
                    : 1f;
                Color c = sr.color;
                c.a = Mathf.Clamp01(baseA * mul);
                sr.color = c;
            }
        }

        // 双击「镜像汇合」分身(=左翼):对称位置(x 取反),同步淡入;和本体一起飞向圆心汇合。
        if (note.MirrorTwin != null)
        {
            note.MirrorTwin.localPosition = new Vector3(-pos.x, pos.y, pos.z);
            if (note.MirrorTwinRenderers != null)
            {
                for (int k = 0; k < note.MirrorTwinRenderers.Length; k++)
                {
                    SpriteRenderer sr = note.MirrorTwinRenderers[k];
                    if (sr == null)
                    {
                        continue;
                    }

                    float baseA = (note.MirrorTwinBaseAlpha != null && k < note.MirrorTwinBaseAlpha.Length)
                        ? note.MirrorTwinBaseAlpha[k]
                        : 1f;
                    Color c = sr.color;
                    c.a = Mathf.Clamp01(baseA * mul);
                    sr.color = c;
                }
            }
        }
    }

    /// <summary>销毁一个音符对象:Prefab 音符销毁整棵根,代码生成音符销毁其渲染物。</summary>
    private void DestroyNoteObject(RhythmNote note)
    {
        if (note == null)
        {
            return;
        }

        if (note.MirrorTwin != null)
        {
            Destroy(note.MirrorTwin.gameObject); // 双击镜像分身随本体一起清理
        }

        if (note.PrefabRoot != null)
        {
            Destroy(note.PrefabRoot.gameObject);
            return;
        }

        if (note.Renderer != null)
        {
            Destroy(note.Renderer.gameObject);
        }
    }

    private void MarkPassedNote(int noteIndex)
    {
        foreach (RhythmNote note in activeNotes)
        {
            if (!note.Judged && note.ChartIndex == noteIndex)
            {
                note.Judged = true;
                if (note.Renderer != null)
                {
                    note.Renderer.color = new Color(1f, 0.45f, 0.65f, 0.35f);
                }
                return;
            }
        }
    }

    private void HitCurrentNote(string message, Color color)
    {
        float hitSide = nextNoteIndex % 2 == 0 ? -1f : 1f;
        NoteKind hitKind = GetNoteKind(nextNoteIndex);
        bool playCue = true; // 该音符若已在判定点响过(Cued),命中就不再重复响
        foreach (RhythmNote note in activeNotes)
        {
            if (!note.Judged && note.ChartIndex == nextNoteIndex)
            {
                note.Judged = true;
                playCue = !note.Cued;
                note.Cued = true;
                if (note.Renderer != null)
                {
                    DestroyNoteObject(note);
                }
                break;
            }
        }

        // 触发左右手挥击:双击两手一起,否则按该音符的一侧。
        // 长按完成不再重挥 —— 让长按期间钉在顶点的手自然落回(见 UpdateBattleHand),
        // 避免"顶点→重新起挥"的跳变。
        if (hitKind != NoteKind.Hold)
        {
            TriggerHandStrike(hitKind == NoteKind.Double ? 0f : hitSide);
        }

        // 通知圆环反馈脚本"命中了"(默认脚本不做额外反馈;挂了自定义脚本才会响应)。
        if (ringFeedback != null)
        {
            ringFeedback.OnHit((int)hitKind, true);
        }

        nextNoteIndex++;
        holdActive = false;
        holdProgress = 0f;
        heldNote = null;
        hitFlashTimer = 0.18f;
        SetFeedback(message, color);
        if (playCue)
        {
            PlayNoteCue(hitKind);
        }
        Debug.Log($"[漓江回声] 打击判定成功：{message}，分数 {score}，连击 {combo}");
        OVRInput.Controller controller = hitSide < 0f ? OVRInput.Controller.LTouch : OVRInput.Controller.RTouch;
        OVRInput.SetControllerVibration(0.55f, 0.75f, controller);
        CancelInvoke(nameof(StopControllerVibration));
        Invoke(nameof(StopControllerVibration), 0.11f);
    }

    private NoteKind GetNoteKind(int index)
    {
        if (holdNoteIndices.Contains(index))
        {
            return NoteKind.Hold;
        }

        if (doubleNoteIndices.Contains(index))
        {
            return NoteKind.Double;
        }

        if (swipeNoteIndices.Contains(index))
        {
            return NoteKind.Swipe;
        }

        // 显式类型谱面(编辑器保存):没被标注的一律单击,不再取模自动 swipe → 所见即所得。
        if (chartTypesExplicit)
        {
            return NoteKind.Strike;
        }

        // 旧的检测/需求谱面:保留原有取模自动挥划,行为不变。
        return index % 8 == 3 || index % 11 == 6 ? NoteKind.Swipe : NoteKind.Strike;
    }

    private float GetHoldDuration(int index)
    {
        if (index + 1 >= noteTimes.Length)
        {
            return 1f;
        }

        return Mathf.Clamp(noteTimes[index + 1] - noteTimes[index] - 0.48f, 0.72f, 1.35f);
    }

    private RhythmNote FindActiveNote(int chartIndex)
    {
        foreach (RhythmNote note in activeNotes)
        {
            if (!note.Judged && note.ChartIndex == chartIndex)
            {
                return note;
            }
        }

        return null;
    }

    private void BeginHoldNote()
    {
        heldNote = FindActiveNote(nextNoteIndex);
        if (heldNote == null)
        {
            return;
        }

        holdActive = true;
        holdProgress = 0f;
        TriggerHandStrike(heldNote.Side); // 长按:对应一侧手挥上击环
        SetFeedback("长按", new Color(0.88f, 0.68f, 1f));
        OVRInput.SetControllerVibration(0.18f, 0.28f, OVRInput.Controller.LTouch | OVRInput.Controller.RTouch);
    }

    private void ProcessHoldNote(float beatTime)
    {
        if (!holdActive || heldNote == null)
        {
            return;
        }

        if (!BattleHoldHeld())
        {
            combo = 0;
            MarkPassedNote(nextNoteIndex);
            nextNoteIndex++;
            holdActive = false;
            heldNote = null;
            holdProgress = 0f;
            SetFeedback("长按中断", new Color(0.95f, 0.55f, 0.9f));
            StopControllerVibration();
            return;
        }

        holdProgress += Time.deltaTime;
        float required = GetHoldDuration(nextNoteIndex);
        if (heldNote.Renderer != null)
        {
            float progress = Mathf.Clamp01(holdProgress / required);
            heldNote.Renderer.color = Color.Lerp(new Color(0.78f, 0.48f, 1f), new Color(1f, 0.9f, 0.35f), progress);
        }

        if (holdProgress >= required)
        {
            score += 180;
            combo++;
            HitCurrentNote("长按完成", new Color(1f, 0.9f, 0.38f));
        }
    }

    private void UpdateControllerInput()
    {
        CacheControllerAnchors();

        UnityEngine.XR.InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        UnityEngine.XR.InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        OVRInput.Controller connected = OVRInput.GetConnectedControllers();

        leftControllerTracked = IsTracked(leftDevice) || (connected & OVRInput.Controller.LTouch) != 0;
        rightControllerTracked = IsTracked(rightDevice) || (connected & OVRInput.Controller.RTouch) != 0;

        leftTriggerValue = ReadTrigger(leftDevice, OVRInput.Controller.LTouch);
        rightTriggerValue = ReadTrigger(rightDevice, OVRInput.Controller.RTouch);
        leftTriggerDown = leftTriggerValue >= 0.55f && previousLeftTriggerValue < 0.55f;
        rightTriggerDown = rightTriggerValue >= 0.55f && previousRightTriggerValue < 0.55f;

        bool leftGripPressed = ReadGripPressed(leftDevice, OVRInput.Controller.LTouch);
        bool rightGripPressed = ReadGripPressed(rightDevice, OVRInput.Controller.RTouch);
        bool leftFacePressed = ReadFacePressed(leftDevice, OVRInput.Controller.LTouch, OVRInput.Button.Three);
        bool rightFacePressed = ReadFacePressed(rightDevice, OVRInput.Controller.RTouch, OVRInput.Button.One);
        battleControllerButtonHeld = leftTriggerValue >= 0.35f || rightTriggerValue >= 0.35f ||
                                     leftGripPressed || rightGripPressed ||
                                     leftFacePressed || rightFacePressed;
        battleControllerButtonDown = leftTriggerDown || rightTriggerDown ||
                                     (leftGripPressed && !previousLeftGripPressed) ||
                                     (rightGripPressed && !previousRightGripPressed) ||
                                     (leftFacePressed && !previousLeftFacePressed) ||
                                     (rightFacePressed && !previousRightFacePressed);
        if (battleControllerButtonDown && currentStage == Stage.Battle)
        {
            Debug.Log("[漓江回声] 已收到控制器打击输入");
        }

        EnsureControllerPointerVisuals();
        UpdateControllerPointerVisual(leftControllerAnchor, leftControllerRay, leftControllerReticle, leftControllerTracked, leftTriggerValue);
        UpdateControllerPointerVisual(rightControllerAnchor, rightControllerRay, rightControllerReticle, rightControllerTracked, rightTriggerValue);

        previousLeftTriggerValue = leftTriggerValue;
        previousRightTriggerValue = rightTriggerValue;
        previousLeftGripPressed = leftGripPressed;
        previousRightGripPressed = rightGripPressed;
        previousLeftFacePressed = leftFacePressed;
        previousRightFacePressed = rightFacePressed;
    }

    private static bool IsTracked(UnityEngine.XR.InputDevice device)
    {
        if (!device.isValid)
        {
            return false;
        }

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked))
        {
            return tracked;
        }

        return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out _);
    }

    private static float ReadTrigger(UnityEngine.XR.InputDevice device, OVRInput.Controller controller)
    {
        float value = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller);
        if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float xrValue))
        {
            value = Mathf.Max(value, xrValue);
        }

        if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool pressed) && pressed)
        {
            value = Mathf.Max(value, 1f);
        }

        return value;
    }

    private static bool ReadGripPressed(UnityEngine.XR.InputDevice device, OVRInput.Controller controller)
    {
        bool pressed = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, controller) >= 0.55f;
        if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.grip, out float gripValue))
        {
            pressed |= gripValue >= 0.55f;
        }

        if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool gripButton))
        {
            pressed |= gripButton;
        }

        return pressed;
    }

    private static bool ReadFacePressed(
        UnityEngine.XR.InputDevice device,
        OVRInput.Controller controller,
        OVRInput.Button ovrButton)
    {
        bool pressed = OVRInput.Get(ovrButton, controller);
        if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool primaryButton))
        {
            pressed |= primaryButton;
        }

        return pressed;
    }

    private void EnsureControllerPointerVisuals()
    {
        if (leftControllerRay == null)
        {
            CreateControllerPointer("左手描画射线", new Color(0.27f, 1f, 0.82f, 0.95f), out leftControllerRay, out leftControllerReticle);
        }

        if (rightControllerRay == null)
        {
            CreateControllerPointer("右手描画射线", new Color(1f, 0.72f, 0.24f, 0.95f), out rightControllerRay, out rightControllerReticle);
        }
    }

    private void CreateControllerPointer(string pointerName, Color color, out LineRenderer line, out Transform reticle)
    {
        GameObject lineObject = new GameObject(pointerName);
        lineObject.transform.SetParent(transform, false);
        line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = 0.009f;
        line.endWidth = 0.004f;
        line.startColor = color;
        line.endColor = new Color(color.r, color.g, color.b, 0.35f);
        line.numCapVertices = 5;

        Shader lineShader = Shader.Find("Sprites/Default");
        if (lineShader != null)
        {
            line.sharedMaterial = new Material(lineShader);
        }

        GameObject reticleObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        reticleObject.name = pointerName + "落点";
        reticleObject.transform.SetParent(transform, false);
        reticleObject.transform.localScale = Vector3.one * 0.035f;
        Collider reticleCollider = reticleObject.GetComponent<Collider>();
        if (reticleCollider != null)
        {
            Destroy(reticleCollider);
        }

        Renderer reticleRenderer = reticleObject.GetComponent<Renderer>();
        Shader reticleShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (reticleShader == null)
        {
            reticleShader = Shader.Find("Sprites/Default");
        }

        if (reticleRenderer != null && reticleShader != null)
        {
            Material reticleMaterial = new Material(reticleShader);
            reticleMaterial.color = color;
            reticleRenderer.sharedMaterial = reticleMaterial;
        }

        reticle = reticleObject.transform;
        line.enabled = false;
        reticleObject.SetActive(false);
    }

    private void UpdateControllerPointerVisual(
        Transform controller,
        LineRenderer line,
        Transform reticle,
        bool tracked,
        float triggerValue)
    {
        bool visible = tracked && controller != null && line != null && reticle != null;
        if (line != null)
        {
            line.enabled = visible;
        }

        if (reticle != null)
        {
            reticle.gameObject.SetActive(visible);
        }

        if (!visible)
        {
            return;
        }

        Vector3 origin = controller.position;
        Vector3 direction = GetControllerRayDirection(controller);
        Vector3 target = origin + direction * 2f;
        if (TryProjectRay(new Ray(origin, direction), out Vector3 localPoint))
        {
            target = stageRoot.TransformPoint(localPoint);
        }

        line.SetPosition(0, origin);
        line.SetPosition(1, target);
        reticle.position = target;
        reticle.localScale = Vector3.one * Mathf.Lerp(0.035f, 0.052f, triggerValue);
    }

    private bool TryGetControllerHover(Rect localBounds, out bool pressed)
    {
        bool leftHover = leftControllerTracked &&
                         leftControllerAnchor != null &&
                         TryProjectControllerRay(leftControllerAnchor, out Vector3 leftPoint) &&
                         localBounds.Contains(new Vector2(leftPoint.x, leftPoint.y));
        bool rightHover = rightControllerTracked &&
                          rightControllerAnchor != null &&
                          TryProjectControllerRay(rightControllerAnchor, out Vector3 rightPoint) &&
                          localBounds.Contains(new Vector2(rightPoint.x, rightPoint.y));

        pressed = (leftHover && leftTriggerDown) || (rightHover && rightTriggerDown);
        return leftHover || rightHover;
    }

    private Vector3 GetControllerRayDirection(Transform controller)
    {
        Vector3 forward = controller.forward;
        if (stageRoot == null)
        {
            return forward;
        }

        Vector3 toStage = stageRoot.position - controller.position;
        return Vector3.Dot(forward, toStage) >= Vector3.Dot(-forward, toStage) ? forward : -forward;
    }

    private void CacheControllerAnchors()
    {
        if (leftControllerAnchor == null)
        {
            GameObject leftObject = GameObject.Find("LeftControllerAnchor");
            if (leftObject != null)
            {
                leftControllerAnchor = leftObject.transform;
            }
        }

        if (rightControllerAnchor == null)
        {
            GameObject rightObject = GameObject.Find("RightControllerAnchor");
            if (rightObject != null)
            {
                rightControllerAnchor = rightObject.transform;
            }
        }
    }

    private void UpdateControllerMotion()
    {
        CacheControllerAnchors();
        if (leftControllerAnchor == null || rightControllerAnchor == null || Time.deltaTime <= 0.0001f)
        {
            controllerMotionReady = false;
            return;
        }

        Vector3 leftPosition = leftControllerAnchor.position;
        Vector3 rightPosition = rightControllerAnchor.position;
        if (controllerMotionReady)
        {
            float inverseDelta = 1f / Time.deltaTime;
            leftControllerVelocity = (leftPosition - lastLeftControllerPosition) * inverseDelta;
            rightControllerVelocity = (rightPosition - lastRightControllerPosition) * inverseDelta;
        }

        lastLeftControllerPosition = leftPosition;
        lastRightControllerPosition = rightPosition;
        controllerMotionReady = true;
    }

    private bool BattleGesturePressed()
    {
        return BattleStrikePressed();
    }

    private bool BattleStrikePressed()
    {
        bool keyboardPressed = Keyboard.current != null &&
                               (Keyboard.current.spaceKey.wasPressedThisFrame ||
                                Keyboard.current.enterKey.wasPressedThisFrame ||
                                Keyboard.current.numpadEnterKey.wasPressedThisFrame);
        bool mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        if (keyboardPressed || mousePressed || battleControllerButtonDown)
        {
            swipeCooldown = 0.16f;
            return true;
        }

        return TryBattleSwing(false);
    }

    private bool BattleSwipePerformed()
    {
        // 蛙纹标准动作已定为「上挑」:无头显调试时用 ↑ 键代表向上挥。
        bool keyboardSwipe = Keyboard.current != null &&
                             Keyboard.current.upArrowKey.wasPressedThisFrame;
        bool mouseSwipe = Mouse.current != null && Mouse.current.leftButton.isPressed &&
                          Mouse.current.delta.ReadValue().sqrMagnitude >= 64f;
        if (keyboardSwipe || mouseSwipe)
        {
            swipeCooldown = 0.22f;
            PlaySfx("swipe", 0.5f);
            return true;
        }

        bool performed = TryBattleSwing(true);
        if (performed)
        {
            PlaySfx("swipe", 0.5f);
        }

        return performed;
    }

    private bool BattleHoldHeld()
    {
        bool keyboardHeld = Keyboard.current != null &&
                            (Keyboard.current.spaceKey.isPressed ||
                             Keyboard.current.enterKey.isPressed ||
                             Keyboard.current.numpadEnterKey.isPressed);
        bool mouseHeld = Mouse.current != null && Mouse.current.leftButton.isPressed;
        return keyboardHeld || mouseHeld || battleControllerButtonHeld;
    }

    private bool TryBattleSwing(bool strictSwipe)
    {
        if (!controllerMotionReady || swipeCooldown > 0f || nextNoteIndex >= noteTimes.Length)
        {
            return false;
        }

        float side = nextNoteIndex % 2 == 0 ? -1f : 1f;
        foreach (RhythmNote note in activeNotes)
        {
            if (!note.Judged && note.ChartIndex == nextNoteIndex)
            {
                side = note.Side;
                break;
            }
        }

        bool deliberateSwing = IsDeliberateBattleSwing(leftControllerVelocity, side, strictSwipe) ||
                               IsDeliberateBattleSwing(rightControllerVelocity, side, strictSwipe);
        if (deliberateSwing)
        {
            swipeCooldown = strictSwipe ? 0.25f : 0.18f;
        }

        return deliberateSwing;
    }

    private bool IsDeliberateBattleSwing(Vector3 worldVelocity, float noteSide, bool strictSwipe)
    {
        float minimumSpeed = strictSwipe ? 0.55f : 0.42f;
        if (stageRoot == null || worldVelocity.magnitude < minimumSpeed)
        {
            return false;
        }

        Vector3 localVelocity = stageRoot.InverseTransformDirection(worldVelocity);
        float inwardVelocity = noteSide < 0f ? localVelocity.x : -localVelocity.x;
        if (strictSwipe)
        {
            // 蛙纹(挥划)标准动作 = 上挑(向上挥),对应「青蛙上跳」意象:只认明显向上的挥动,
            // 不再吃向下/向内/前后,让动作唯一、直观。(整体速度已由上面的 minimumSpeed 把关。)
            return localVelocity.y >= 0.50f;
        }

        return inwardVelocity >= 0.12f ||
               Mathf.Abs(localVelocity.y) >= 0.36f ||
               Mathf.Abs(localVelocity.z) >= 0.36f;
    }

    private static bool IsHeadsetRunning()
    {
        return Application.platform == RuntimePlatform.Android || OVRManager.isHmdPresent;
    }

    private void UpdateRingVisual(float beatTime)
    {
        if (ringRenderer == null || ringTransform == null || nextNoteIndex >= noteTimes.Length)
        {
            return;
        }

        float untilNote = noteTimes[nextNoteIndex] - beatTime;
        float normalized = Mathf.InverseLerp(0.7f, 0f, Mathf.Clamp(untilNote, 0f, 0.7f));

        // 反馈交给圆环上挂的脚本(默认脚本=下面这套旧观感);脚本缺失时兜底直接用旧算法,保证永不异常。
        if (ringFeedback != null)
        {
            ringFeedback.OnBeat(normalized);
            return;
        }

        float scale = Mathf.Lerp(1.12f, 0.92f, normalized);
        ringTransform.localScale = ringBaseScale * scale;
        ringRenderer.color = new Color(1f, 0.92f, 0.45f, Mathf.Lerp(0.42f, 1f, normalized));
    }

    private void UpdatePatternProgress()
    {
        if (patternRenderer == null)
        {
            return;
        }

        float progress = Mathf.Clamp01(nextNoteIndex / (float)noteTimes.Length);
        if (progress >= 0.68f)
        {
            patternRenderer.sprite = GetSprite(donePaths[selectedLevel], true);
            patternRenderer.color = Color.white;
        }
        else
        {
            patternRenderer.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.52f, 1f, progress));
        }
    }

    private void UpdateProgressFill()
    {
        if (progressFillRenderer == null)
        {
            return;
        }

        float progress = Mathf.Clamp01(nextNoteIndex / (float)noteTimes.Length);
        Transform fillTransform = progressFillRenderer.transform;
        Vector3 scale = fillTransform.localScale;
        scale.x = Mathf.Lerp(0.02f, 2.94f, progress);
        fillTransform.localScale = scale;

        Vector3 position = fillTransform.localPosition;
        position.x = Mathf.Lerp(-1.47f, 0f, progress);
        fillTransform.localPosition = position;
    }

    private void UpdateScoreText()
    {
        if (scoreText != null)
        {
            scoreText.text = "分数 " + score + "    连击 " + combo;
        }
    }

    private void SetFeedback(string message, Color color)
    {
        if (feedbackText == null)
        {
            return;
        }

        feedbackText.text = message;
        feedbackText.color = color;
        feedbackTimer = 0.48f;
    }

    private void UpdateFeedbackFade()
    {
        if (feedbackText == null || feedbackTimer <= 0f)
        {
            return;
        }

        feedbackTimer -= Time.deltaTime;
        Color color = feedbackText.color;
        color.a = Mathf.Clamp01(feedbackTimer / 0.48f);
        feedbackText.color = color;
    }

    private int GetInitialCardPageIndex()
    {
        string preferredPath = infoCardPaths[Mathf.Clamp(selectedLevel, 0, infoCardPaths.Length - 1)];
        for (int i = 0; i < cardPagePaths.Length; i++)
        {
            if (cardPagePaths[i] == preferredPath)
            {
                return i;
            }
        }

        return 0;
    }

    private RectInt GetDoneCrop(int level)
    {
        RectInt[] crops = { SnakeDoneCrop, BirdDoneCrop, CoinDoneCrop };
        return crops[Mathf.Clamp(level, 0, crops.Length - 1)];
    }

    private void ShowCard()
    {
        ResetStage(Stage.Card);
        PlayStageLoop("ambience", 0.28f);
        PlaySfx("card_open", 0.72f);
        cardPageIndex = GetInitialCardPageIndex();

        AddLayer("transition/purple_frame", "卡片半透明紫幕", Vector3.zero, MainCanvasWidth, -20, 0.24f);
        AddLayer("ui/card_back", "卡片底纹", Vector3.zero, 4.65f, -5, 0.86f);
        AddLayer("pattern/drawing_card", "纹样绘制卡面底", new Vector3(0f, 0f, -0.03f), 4.2f, -4, 0.34f);
        GameObject cardObject = AddLayer(cardPagePaths[cardPageIndex], "纹样解析卡片", new Vector3(0f, 0f, -0.06f), 4.08f, 5, 0.98f);
        cardPageRenderer = cardObject.GetComponent<SpriteRenderer>();
        AddIcon("cards/left_button", "左翻页按钮", new Vector3(-2.45f, -0.02f, -0.12f), 0.42f, 10, 0.98f);
        AddIcon("cards/right_button", "右翻页按钮", new Vector3(2.45f, -0.02f, -0.12f), 0.42f, 10, 0.98f);

        GameObject donePattern = AddCroppedSprite(
            donePaths[selectedLevel],
            "完成纹样展示",
            GetDoneCrop(selectedLevel),
            new Vector3(-1.36f, -0.14f, -0.14f),
            0.46f,
            12,
            0.62f,
            false);
        RegisterMotion(donePattern, MotionKind.Pulse, 0.025f, 2.2f, 0f);
    }

    private void UpdateCard()
    {
        int direction = ReadHorizontalStep();
        if (direction != 0 && selectMoveCooldown <= 0f)
        {
            cardPageIndex = (cardPageIndex + direction + cardPagePaths.Length) % cardPagePaths.Length;
            selectMoveCooldown = 0.22f;
            PlaySfx("swipe", 0.42f);
            if (cardPageRenderer != null)
            {
                cardPageRenderer.sprite = GetSprite(cardPagePaths[cardPageIndex], false);
            }
        }

        if (AdvancePressed() || stageTimer > 10f)
        {
            PlaySfx("page_close", 0.68f);
            ShowResult();
        }
    }

    private void ShowResult()
    {
        ResetStage(Stage.Result);
        PlayStageLoop("ambience", 0.28f);

        bool victory = score >= Mathf.RoundToInt(noteTimes.Length * 70f);
        AddLayer("transition/purple_frame", "结算半透明紫幕", Vector3.zero, MainCanvasWidth, -20, 0.32f);
        AddLayer("ui/card_back", "结算主卡面", Vector3.zero, 4.58f, -8, 0.9f);
        AddLayer("pattern/drawing_card", "结算纹样底框", new Vector3(0f, -0.02f, -0.03f), 4.05f, -6, 0.22f);
        AddLayer("battle/final_boss_transparent", "结算怪物剪影", new Vector3(0f, -0.04f, -0.05f), 3.45f, -2, victory ? 0.28f : 0.74f);
        GameObject fire = AddLayer("battle/final_boss_fire", "结算火焰", new Vector3(0f, -0.18f, -0.08f), 3.72f, -1, victory ? 0.38f : 0.82f);
        RegisterMotion(fire, MotionKind.Flame, 0.045f, 6.4f, 0f);

        GameObject resultBadge = AddIcon(victory ? "ui/victory" : "ui/failed", victory ? "胜利" : "失败", new Vector3(0f, 0.18f, -0.14f), victory ? 0.78f : 0.58f, 8, 0.98f);
        RegisterMotion(resultBadge, MotionKind.Pulse, 0.018f, 2.6f, 0f);

        GameObject donePattern = AddCroppedSprite(
            donePaths[selectedLevel],
            "结算完成纹样",
            GetDoneCrop(selectedLevel),
            new Vector3(-1.36f, -0.32f, -0.16f),
            0.42f,
            10,
            0.72f,
            false);
        RegisterMotion(donePattern, MotionKind.Pulse, 0.02f, 2.1f, 1.2f);

        hintText = AddText("最终得分  " + score, new Vector3(0f, -0.78f, -0.2f), 0.026f, new Color(1f, 0.93f, 0.74f), 30);
    }

    private void UpdateResult()
    {
        if (AdvancePressed())
        {
            PlaySfx("button", 0.62f);
            ReturnToSelectStage();
        }
    }

    // 结算后重选关:统一回到独立的「选关滚轮场景」(Stage_Select),而不是控制器内置的老选关。
    // 同时销毁当前(持久化)控制器,让重进旧主场景时【新建一个全新控制器】,走首次那条已验证能用的路,
    // 避免持久化控制器不重播过场导致的重入卡死。
    private void ReturnToSelectStage()
    {
        const string selectStageScene = "Stage_Select";
        LijiangEchoGameFlow flow = LijiangEchoGameFlow.Instance;
        if (flow != null)
        {
            instance = null;                  // 让重入时的创建判断/单例守卫认为「无控制器」→ 新建
            flow.GoToStage(selectStageScene); // 卸载旧主场景、加载滚轮选关场景(Flow 是独立持久对象,不受本对象销毁影响)
            Destroy(gameObject);
            return;
        }

        // 兜底:未经 Flow(单独打开旧主场景做测试)时,退回控制器内置选关。
        ShowSelect();
    }

    private void ToggleMenuOverlay()
    {
        if (stageRoot == null)
        {
            return;
        }

        if (menuObjects.Count > 0)
        {
            ClearMenuOverlay();
            return;
        }

        RegisterMenuObject(AddLayer("transition/purple_frame", "系统菜单暗幕", Vector3.zero, MainCanvasWidth, 80, 0.32f));
        RegisterMenuObject(AddLayer("ui/card_back", "系统菜单面板", new Vector3(0f, 0.04f, -0.64f), 3.75f, 82, 0.78f));

        RegisterMenuObject(AddIcon("ui/home", "菜单主页", new Vector3(-1.08f, 0.05f, -0.7f), 0.42f, 86, 0.96f));
        RegisterMenuObject(AddIcon("ui/music", "菜单音乐", new Vector3(-0.36f, 0.05f, -0.7f), 0.42f, 86, 0.96f));
        RegisterMenuObject(AddIcon("ui/skip", "菜单跳过", new Vector3(0.36f, 0.05f, -0.7f), 0.42f, 86, 0.96f));
        RegisterMenuObject(AddIcon("ui/back", "菜单返回", new Vector3(1.08f, 0.05f, -0.7f), 0.42f, 86, 0.96f));

        RegisterMenuObject(AddText("主页", new Vector3(-1.08f, -0.38f, -0.72f), 0.018f, Color.white, 90).gameObject);
        RegisterMenuObject(AddText("音乐", new Vector3(-0.36f, -0.38f, -0.72f), 0.018f, Color.white, 90).gameObject);
        RegisterMenuObject(AddText("跳过", new Vector3(0.36f, -0.38f, -0.72f), 0.018f, Color.white, 90).gameObject);
        RegisterMenuObject(AddText("返回", new Vector3(1.08f, -0.38f, -0.72f), 0.018f, Color.white, 90).gameObject);
    }

    private void RegisterMenuObject(GameObject item)
    {
        if (item != null)
        {
            menuObjects.Add(item);
        }
    }

    private void ClearMenuOverlay()
    {
        foreach (GameObject item in menuObjects)
        {
            if (item != null)
            {
                Destroy(item);
            }
        }

        menuObjects.Clear();
    }

    private void EnsureAudioSources()
    {
        if (ambienceSource == null)
        {
            ambienceSource = gameObject.AddComponent<AudioSource>();
            ambienceSource.playOnAwake = false;
            ambienceSource.loop = true;
            ambienceSource.spatialBlend = 0f;
            ambienceSource.priority = 128;
        }

        if (battleMusicSource == null)
        {
            battleMusicSource = gameObject.AddComponent<AudioSource>();
            battleMusicSource.playOnAwake = false;
            battleMusicSource.loop = false;
            battleMusicSource.spatialBlend = 0f;
            battleMusicSource.priority = 32;
        }

        if (sfxSource == null)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
            sfxSource.spatialBlend = 0f;
            sfxSource.priority = 64;
        }

        EnsureAudioListener();
    }

    /// <summary>
    /// 兜底:场景里没有"启用的 AudioListener"时,所有游戏 AudioSource 都听不到声音
    /// (编辑器预览走 AudioUtil 另一条路还能响,所以会出现"诊断正常但游戏没声")。
    /// 这里保证至少有一个可用的 AudioListener:优先挂到当前游戏相机,否则挂到本控制器上。
    /// </summary>
    private void EnsureAudioListener()
    {
        AudioListener[] listeners = FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (AudioListener l in listeners)
        {
            if (l != null && l.enabled && l.gameObject.activeInHierarchy)
            {
                return; // 已有可用的
            }
        }

        Camera cam = FindGameplayCamera();
        GameObject host = cam != null ? cam.gameObject : gameObject;
        AudioListener existing = host.GetComponent<AudioListener>();
        if (existing == null)
        {
            existing = host.AddComponent<AudioListener>();
        }

        existing.enabled = true;
        if (AudioListener.volume <= 0.01f)
        {
            AudioListener.volume = 1f; // 全局音量被设成 0 也会没声
        }

        Debug.Log("[漓江回声] 未发现可用 AudioListener,已在 " + host.name + " 上补一个,游戏音频才能听到。");
    }

    private AudioClip GetAudioClip(string clipName)
    {
        if (audioCache.TryGetValue(clipName, out AudioClip cachedClip))
        {
            return cachedClip;
        }

        AudioClip clip = Resources.Load<AudioClip>("LijiangEchoAudio/" + clipName);
        if (clip == null)
        {
            Debug.LogWarning("[漓江回声] 未找到音频资源：" + clipName);
            return null;
        }

        audioCache[clipName] = clip;
        return clip;
    }

    private void PlayStageLoop(string clipName, float volume)
    {
        EnsureAudioSources();
        AudioClip clip = GetAudioClip(clipName);
        if (clip == null)
        {
            return;
        }

        ambienceSource.volume = volume;
        if (ambienceSource.clip == clip && ambienceSource.isPlaying)
        {
            return;
        }

        ambienceSource.Stop();
        ambienceSource.clip = clip;
        ambienceSource.loop = true;
        ambienceSource.Play();
    }

    private void StopStageLoop()
    {
        if (ambienceSource != null)
        {
            ambienceSource.Stop();
            ambienceSource.clip = null;
        }
    }

    private void PlayAuxiliaryLoop(string clipName, float volume)
    {
        EnsureAudioSources();
        AudioClip clip = GetAudioClip(clipName);
        if (clip == null)
        {
            return;
        }

        battleMusicSource.Stop();
        battleMusicSource.clip = clip;
        battleMusicSource.volume = volume;
        battleMusicSource.loop = true;
        battleMusicSource.Play();
    }

    private void StopAuxiliaryLoop()
    {
        if (battleMusicSource != null)
        {
            battleMusicSource.Stop();
            battleMusicSource.clip = null;
            battleMusicSource.loop = false;
        }
    }

    private void PlaySfx(string clipName, float volume)
    {
        EnsureAudioSources();
        AudioClip clip = GetAudioClip(clipName);
        if (clip != null)
        {
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }

    private void StartBattleMusic()
    {
        EnsureAudioSources();
        AudioClip clip = GetAudioClip("battle_music");
        battleMusicStarted = true;
        battleMusicTime = 0f;
        battleEndingTimer = 0f;
        if (clip == null)
        {
            return;
        }

        // 从播放头试玩:定位到指定秒起播,并跳过该时间点之前的音符(不让它们一次性涌出/漏判)。
        float seek = battleSeekTime >= 0f ? Mathf.Clamp(battleSeekTime, 0f, Mathf.Max(0f, clip.length - 0.05f)) : 0f;
        battleMusicTime = seek;
        if (seek > 0f)
        {
            while (nextSpawnIndex < noteTimes.Length && noteTimes[nextSpawnIndex] < seek - NoteApproachTime)
            {
                nextSpawnIndex++;
            }

            while (nextNoteIndex < noteTimes.Length && noteTimes[nextNoteIndex] < seek - 0.05f)
            {
                nextNoteIndex++;
            }
        }

        battleMusicSource.Stop();
        battleMusicSource.clip = clip;
        battleMusicSource.loop = false;
        battleMusicSource.mute = false;
        battleMusicSource.volume = 0.86f;
        battleMusicSource.time = seek;
        battleMusicSource.Play();
        AudioListener anyListener = FindFirstObjectByType<AudioListener>();
        Debug.Log($"[漓江回声] 战斗音乐开始(从 {seek:F2}s 起):clip={clip.name} 采样={clip.samples} 时长={clip.length:F2}s " +
                  $"isPlaying={battleMusicSource.isPlaying} 音量={battleMusicSource.volume} mute={battleMusicSource.mute} " +
                  $"AudioListener={(anyListener != null ? anyListener.gameObject.name : "无!")} 全局音量={AudioListener.volume}");
    }

    private float GetBattleMusicTime()
    {
        if (battleMusicSource != null && battleMusicSource.clip != null && battleMusicSource.isPlaying)
        {
            battleMusicTime = Mathf.Max(battleMusicTime, battleMusicSource.time);
        }
        else if (battleMusicSource == null || battleMusicSource.clip == null)
        {
            battleMusicTime = Mathf.Max(battleMusicTime, stageTimer - 3f);
        }

        return Mathf.Max(0f, battleMusicTime);
    }

    private bool IsBattleMusicFinished()
    {
        if (!battleMusicStarted)
        {
            return false;
        }

        if (battleMusicSource == null || battleMusicSource.clip == null)
        {
            return nextNoteIndex >= noteTimes.Length && battleMusicTime > noteTimes[^1] + 1f;
        }

        return !battleMusicSource.isPlaying && battleMusicTime >= battleMusicSource.clip.length - 0.12f;
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
        introPreLevelFinished = false;
    }

    private GameObject AddVideoLayer(string objectName, string videoPath, Vector3 localPosition, float targetWidth, int order, Transform parent = null)
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
        PlayIntroVideo(videoPath);

        spawnedObjects.Add(videoObject);
        return videoObject;
    }

    private void PlayIntroVideo(string videoPath)
    {
        if (introVideoPlayer == null)
        {
            return;
        }

        string url = Application.streamingAssetsPath + "/" + videoPath;
        introVideoPlayer.url = url.Replace("\\", "/");
        introVideoPlayer.Stop();
        introVideoPlayer.Play();
    }

    private void HandleIntroVideoEnded(VideoPlayer player)
    {
        introPreLevelFinished = true;
    }

    private void SetRendererAlpha(SpriteRenderer renderer, float alpha)
    {
        if (renderer == null)
        {
            return;
        }

        Color color = renderer.color;
        color.a = Mathf.Clamp01(alpha);
        renderer.color = color;
    }

    private GameObject AddLayer(string resourcePath, string objectName, Vector3 localPosition, float targetWidth, int order, float alpha = 1f, Transform parent = null)
    {
        GameObject spriteObject = AddSprite(resourcePath, objectName, localPosition, Vector3.one, order, alpha, false, parent);
        FitRendererWidth(spriteObject.GetComponent<SpriteRenderer>(), targetWidth);
        return spriteObject;
    }

    private GameObject AddIcon(string resourcePath, string objectName, Vector3 visibleCenter, float targetHeight, int order, float alpha = 1f)
    {
        GameObject spriteObject = AddSprite(resourcePath, objectName, visibleCenter, Vector3.one, order, alpha, true);
        SpriteRenderer renderer = spriteObject.GetComponent<SpriteRenderer>();
        FitRendererHeight(renderer, targetHeight);
        PlaceVisibleCenter(spriteObject.transform, renderer, visibleCenter);
        return spriteObject;
    }

    private GameObject AddCroppedSprite(
        string resourcePath,
        string objectName,
        RectInt topLeftCrop,
        Vector3 visibleCenter,
        float targetHeight,
        int order,
        float alpha,
        bool mirrorX,
        Transform parent = null,
        bool centerOnVisual = false)
    {
        GameObject spriteObject = new GameObject(objectName);
        spriteObject.transform.SetParent(parent != null ? parent : stageRoot, false);

        SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetCroppedSprite(resourcePath, topLeftCrop);
        renderer.sortingOrder = order;
        SetCroppedSpritePose(renderer, visibleCenter, targetHeight, alpha, mirrorX, centerOnVisual);

        spawnedObjects.Add(spriteObject);
        return spriteObject;
    }

    private void SetCroppedSpritePose(SpriteRenderer renderer, Vector3 visibleCenter, float targetHeight, float alpha, bool mirrorX, bool centerOnVisual = false, bool fitByMaxDimension = false)
    {
        if (renderer == null || renderer.sprite == null || renderer.sprite.bounds.size.y <= 0f)
        {
            return;
        }

        // fitByMaxDimension:按"较大边"适配到 targetHeight 当作方框边长(宽图按宽、高图按高),
        // 让横向长图(如鱼纹)不会因只按高度缩放而把宽度放得过大、横向暴冲成拖影;
        // 每个纹样都恰好塞进圆环大小。默认仍按高度缩放,兼容其他调用点。
        float denom = fitByMaxDimension
            ? Mathf.Max(renderer.sprite.bounds.size.x, renderer.sprite.bounds.size.y)
            : renderer.sprite.bounds.size.y;
        float scale = targetHeight / denom;
        Vector3 pos = visibleCenter;
        if (centerOnVisual)
        {
            // 精灵原点(pivot)不在可见内容中心时,把"不透明像素真实中心"对齐到 visibleCenter,
            // 否则纹样会整体偏向一侧(鱼纹往右散射就是这个原因)。
            Vector3 c = GetSpriteVisibleCenter(renderer.sprite);
            pos -= new Vector3((mirrorX ? -scale : scale) * c.x, scale * c.y, 0f);
        }

        renderer.transform.localPosition = pos;
        renderer.transform.localRotation = Quaternion.identity;
        renderer.transform.localScale = new Vector3(mirrorX ? -scale : scale, scale, scale);
        renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
    }

    private GameObject AddSprite(string resourcePath, string objectName, Vector3 localPosition, Vector3 localScale, int order, float alpha, bool tight, Transform parent = null)
    {
        GameObject spriteObject = new GameObject(objectName);
        spriteObject.transform.SetParent(parent != null ? parent : stageRoot, false);
        spriteObject.transform.localPosition = localPosition;
        spriteObject.transform.localRotation = Quaternion.identity;
        spriteObject.transform.localScale = localScale;

        SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetSprite(resourcePath, tight);
        renderer.sortingOrder = order;
        renderer.color = new Color(1f, 1f, 1f, alpha);

        spawnedObjects.Add(spriteObject);
        return spriteObject;
    }

    private GameObject AddSolidRect(string objectName, Vector3 localPosition, float width, float height, Color color, int order)
    {
        GameObject spriteObject = new GameObject(objectName);
        spriteObject.transform.SetParent(stageRoot, false);
        spriteObject.transform.localPosition = localPosition;
        spriteObject.transform.localRotation = Quaternion.identity;
        spriteObject.transform.localScale = new Vector3(width, height, 1f);

        SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetSolidSprite(color);
        renderer.sortingOrder = order;
        renderer.color = color;

        spawnedObjects.Add(spriteObject);
        return spriteObject;
    }

    private LineRenderer AddLineRenderer(string objectName, float width, Color color, int order)
    {
        GameObject lineObject = new GameObject(objectName);
        lineObject.transform.SetParent(stageRoot, false);
        lineObject.transform.localPosition = Vector3.zero;
        lineObject.transform.localRotation = Quaternion.identity;
        lineObject.transform.localScale = Vector3.one;

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
        line.numCapVertices = 5;
        line.numCornerVertices = 4;
        line.sortingOrder = order;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader != null)
        {
            Material material = new Material(shader);
            material.color = Color.white;
            line.sharedMaterial = material;
        }

        spawnedObjects.Add(lineObject);
        return line;
    }

    private void ApplyOverlaySpriteMaterial(SpriteRenderer renderer, int renderQueue)
    {
        if (renderer == null)
        {
            return;
        }

        Shader shader = Shader.Find("LijiangEcho/OverlaySprite");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        if (shader == null)
        {
            return;
        }

        Material material = new Material(shader);
        material.renderQueue = renderQueue;
        renderer.sharedMaterial = material;
    }

    private TextMesh AddText(string text, Vector3 localPosition, float size, Color color, int order)
    {
        GameObject textObject = new GameObject("文字_" + text);
        textObject.transform.SetParent(stageRoot, false);
        textObject.transform.localPosition = localPosition;
        textObject.transform.localRotation = Quaternion.identity;
        textObject.transform.localScale = Vector3.one;

        TextMesh textMesh = textObject.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.font = GetUiFont();
        textMesh.GetComponent<MeshRenderer>().sharedMaterial = GetUiFont().material;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.fontSize = 72;
        textMesh.characterSize = size;
        textMesh.color = color;
        textMesh.richText = false;

        MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
        renderer.sortingOrder = order;

        spawnedObjects.Add(textObject);
        return textMesh;
    }

    private void RegisterMotion(GameObject item, MotionKind kind, float amplitude, float speed, float phase)
    {
        if (item == null)
        {
            return;
        }

        SpriteRenderer renderer = item.GetComponent<SpriteRenderer>();
        motionItems.Add(new MotionItem
        {
            Transform = item.transform,
            Renderer = renderer,
            BasePosition = item.transform.localPosition,
            BaseScale = item.transform.localScale,
            BaseRotation = item.transform.localRotation,
            BaseColor = renderer != null ? renderer.color : Color.white,
            Kind = kind,
            Speed = speed,
            Amplitude = amplitude,
            Phase = phase
        });
    }

    private void UpdateMotions()
    {
        foreach (MotionItem item in motionItems)
        {
            if (item.Transform == null)
            {
                continue;
            }

            float wave = Mathf.Sin(Time.time * item.Speed + item.Phase);
            switch (item.Kind)
            {
                case MotionKind.FloatY:
                    item.Transform.localPosition = item.BasePosition + new Vector3(0f, wave * item.Amplitude, 0f);
                    break;
                case MotionKind.FloatX:
                    item.Transform.localPosition = item.BasePosition + new Vector3(wave * item.Amplitude, 0f, 0f);
                    break;
                case MotionKind.Pulse:
                    item.Transform.localScale = item.BaseScale * (1f + wave * item.Amplitude);
                    break;
                case MotionKind.Flame:
                    item.Transform.localScale = new Vector3(
                        item.BaseScale.x * (1f + wave * item.Amplitude * 0.45f),
                        item.BaseScale.y * (1f + Mathf.Abs(wave) * item.Amplitude),
                        item.BaseScale.z);
                    if (item.Renderer != null)
                    {
                        Color color = item.BaseColor;
                        color.a = Mathf.Clamp01(item.BaseColor.a * (0.76f + Mathf.Abs(wave) * 0.26f));
                        item.Renderer.color = color;
                    }
                    break;
                case MotionKind.Monster:
                    item.Transform.localPosition = item.BasePosition + new Vector3(0f, wave * item.Amplitude, 0f);
                    item.Transform.localScale = item.BaseScale * (1f + Mathf.Abs(wave) * 0.012f);
                    break;
                case MotionKind.Wing:
                    item.Transform.localPosition = item.BasePosition + new Vector3(wave * item.Amplitude * 0.45f, wave * item.Amplitude, 0f);
                    item.Transform.localRotation = item.BaseRotation * Quaternion.Euler(0f, 0f, wave * 3.8f);
                    break;
                case MotionKind.Hand:
                    item.Transform.localPosition = item.BasePosition + new Vector3(wave * item.Amplitude * 0.65f, wave * item.Amplitude, 0f);
                    item.Transform.localRotation = item.BaseRotation * Quaternion.Euler(0f, 0f, wave * 5.4f);
                    item.Transform.localScale = item.BaseScale * (1f + Mathf.Abs(wave) * 0.008f);
                    break;
            }
        }
    }

    private void FitRendererWidth(SpriteRenderer renderer, float targetWidth)
    {
        if (renderer == null || renderer.sprite == null || renderer.sprite.bounds.size.x <= 0f)
        {
            return;
        }

        float scale = targetWidth / renderer.sprite.bounds.size.x;
        renderer.transform.localScale = Vector3.one * scale;
    }

    private void FitRendererHeight(SpriteRenderer renderer, float targetHeight)
    {
        if (renderer == null || renderer.sprite == null || renderer.sprite.bounds.size.y <= 0f)
        {
            return;
        }

        float scale = targetHeight / renderer.sprite.bounds.size.y;
        renderer.transform.localScale = Vector3.one * scale;
    }

    private void FitRendererVisibleHeight(SpriteRenderer renderer, float visiblePixels, float targetVisibleHeight, float horizontalSign = 1f)
    {
        if (renderer == null || renderer.sprite == null || visiblePixels <= 0f)
        {
            return;
        }

        float scale = targetVisibleHeight / (visiblePixels / PixelsPerUnit);
        renderer.transform.localScale = new Vector3(Mathf.Sign(horizontalSign) * scale, scale, scale);
    }

    private void PlaceVisibleCenter(Transform itemTransform, SpriteRenderer renderer, Vector3 targetCenter)
    {
        if (renderer == null || renderer.sprite == null)
        {
            return;
        }

        Vector3 localCenter = renderer.sprite.bounds.center;
        Vector3 scaledCenter = Vector3.Scale(localCenter, itemTransform.localScale);
        itemTransform.localPosition = targetCenter - scaledCenter;
    }

    private void PlaceTexturePixelCenter(Transform itemTransform, SpriteRenderer renderer, Vector2 pixelCenter, Vector3 targetCenter)
    {
        if (renderer == null || renderer.sprite == null || renderer.sprite.texture == null)
        {
            return;
        }

        Texture2D texture = renderer.sprite.texture;
        Vector3 localPoint = new Vector3(
            (pixelCenter.x - texture.width * 0.5f) / PixelsPerUnit,
            (texture.height * 0.5f - pixelCenter.y) / PixelsPerUnit,
            0f);
        Vector3 scaledPoint = Vector3.Scale(localPoint, itemTransform.localScale);
        itemTransform.localPosition = targetCenter - scaledPoint;
    }

    /// <summary>
    /// 返回精灵"不透明像素真实中心"相对 pivot 的偏移(局部单位)。
    /// FullRect 精灵的 sprite.bounds.center 恒为 0(=pivot 几何中心),无法反映内容偏心,
    /// 会导致纹样/光晕整体偏向一侧。这里按 alpha 加权采样出真实质心。
    /// 贴图未开 Read/Write(GetPixels32 抛异常)时回退到 bounds.center,音符照常显示、不消失。
    /// 结果按精灵缓存,每张只算一次。
    /// </summary>
    private Vector3 GetSpriteVisibleCenter(Sprite sprite)
    {
        if (sprite == null)
        {
            return Vector3.zero;
        }

        if (visibleCenterCache.TryGetValue(sprite, out Vector3 cachedCenter))
        {
            return cachedCenter;
        }

        Vector3 result = sprite.bounds.center; // 回退:pivot 几何中心
        try
        {
            Texture2D texture = sprite.texture;
            Rect tr = sprite.textureRect; // 该精灵在贴图里的像素矩形(左下为原点)
            int rx = Mathf.Clamp(Mathf.RoundToInt(tr.x), 0, texture.width - 1);
            int ry = Mathf.Clamp(Mathf.RoundToInt(tr.y), 0, texture.height - 1);
            int rw = Mathf.Clamp(Mathf.RoundToInt(tr.width), 1, texture.width - rx);
            int rh = Mathf.Clamp(Mathf.RoundToInt(tr.height), 1, texture.height - ry);
            Color32[] pixels = texture.GetPixels32(); // 未开 Read/Write 会抛异常 → 走 catch 回退
            int texW = texture.width;
            int step = Mathf.Max(1, Mathf.Max(rw, rh) / 256); // 大图降采样,省时
            double sumA = 0.0, sumX = 0.0, sumY = 0.0;
            for (int yy = 0; yy < rh; yy += step)
            {
                int rowBase = (ry + yy) * texW + rx;
                for (int xx = 0; xx < rw; xx += step)
                {
                    byte a = pixels[rowBase + xx].a;
                    if (a < 12)
                    {
                        continue;
                    }

                    sumA += a;
                    sumX += (double)a * xx;
                    sumY += (double)a * yy;
                }
            }

            if (sumA > 0.0)
            {
                double cx = sumX / sumA; // 相对裁剪矩形左下角
                double cy = sumY / sumA;
                float offX = (float)((cx - rw * 0.5) / PixelsPerUnit);
                float offY = (float)((cy - rh * 0.5) / PixelsPerUnit);
                result = new Vector3(offX, offY, 0f);
            }
        }
        catch
        {
            // 贴图不可读:保持回退值(等同旧行为,不报错、不影响显示)。
        }

        visibleCenterCache[sprite] = result;
        return result;
    }

    private Font GetUiFont()
    {
        if (uiFont != null)
        {
            return uiFont;
        }

        uiFont = Resources.Load<Font>("Fonts/LijiangUiFont");
        if (uiFont != null)
        {
            return uiFont;
        }

        uiFont = Font.CreateDynamicFontFromOSFont(
            new[]
            {
                "STXinwei",
                "华文新魏",
                "STXingkai",
                "华文行楷",
                "STKaiti",
                "华文楷体",
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "SimHei"
            },
            96);
        return uiFont;
    }

    private Sprite GetSprite(string resourcePath, bool tight)
    {
        string cacheKey = resourcePath + (tight ? "#tight" : "#full");
        if (spriteCache.TryGetValue(cacheKey, out Sprite cachedSprite))
        {
            return cachedSprite;
        }

        Texture2D texture = Resources.Load<Texture2D>(ArtRoot + resourcePath);
        if (texture == null)
        {
            Debug.LogWarning("[漓江回声] 未找到美术资源：" + ArtRoot + resourcePath);
            texture = CreateFallbackTexture();
        }

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            PixelsPerUnit,
            0,
            tight ? SpriteMeshType.Tight : SpriteMeshType.FullRect);
        spriteCache[cacheKey] = sprite;
        return sprite;
    }

    private Sprite GetCroppedSprite(string resourcePath, RectInt topLeftCrop)
    {
        string cacheKey = resourcePath + "#crop:" + topLeftCrop.x + ":" + topLeftCrop.y + ":" + topLeftCrop.width + ":" + topLeftCrop.height;
        if (spriteCache.TryGetValue(cacheKey, out Sprite cachedSprite))
        {
            return cachedSprite;
        }

        Texture2D texture = Resources.Load<Texture2D>(ArtRoot + resourcePath);
        if (texture == null)
        {
            Debug.LogWarning("[漓江回声] 未找到美术资源：" + ArtRoot + resourcePath);
            texture = CreateFallbackTexture();
            topLeftCrop = new RectInt(0, 0, texture.width, texture.height);
        }

        float sourceWidth = texture.width;
        float sourceHeight = texture.height;
        if (resourcePath.StartsWith("transition/"))
        {
            sourceWidth = 3207f;
            sourceHeight = 630f;
        }
        else if (resourcePath.StartsWith("pattern/") && resourcePath != "pattern/drawing_card")
        {
            sourceWidth = 5000f;
            sourceHeight = 5000f;
        }
        else if (resourcePath == "battle/frog_swipe")
        {
            sourceWidth = 1672f;
            sourceHeight = 941f;
        }
        else if (resourcePath == "battle/hit_ring" || resourcePath == "battle/hit_block")
        {
            sourceWidth = 3840f;
            sourceHeight = 2160f;
        }

        float scaleX = texture.width / sourceWidth;
        float scaleY = texture.height / sourceHeight;
        int x = Mathf.Clamp(Mathf.RoundToInt(topLeftCrop.x * scaleX), 0, texture.width - 1);
        int width = Mathf.Clamp(Mathf.RoundToInt(topLeftCrop.width * scaleX), 1, texture.width - x);
        int top = Mathf.Clamp(Mathf.RoundToInt(topLeftCrop.y * scaleY), 0, texture.height - 1);
        int height = Mathf.Clamp(Mathf.RoundToInt(topLeftCrop.height * scaleY), 1, texture.height - top);
        int y = texture.height - top - height;

        // 单图单纹样:这些图各自是一张独立 PNG、里面只有一个纹样(鱼/蛙/各 pattern 纹样)。
        // 历史上的裁剪坐标是针对旧的大图集(pattern/ 假定 5000×5000、frog 假定 1672×941)写的,
        // 对现在的独立导出图是错的:会把纹样切成一小条 / 半张(如鸟纹"右下角半张")、或横向挤压。
        // 因此一律改用整张图,再由 TightenToOpaque 收紧到纹样紧包围盒 —— 完整、居中,
        // 无论用在音符/光晕/右下角待描绘纹样/选关结算都一致正确。drawing_card 是卡面底,不动。
        bool singleContent = resourcePath == "select/fish_symbol"
            || resourcePath == "battle/frog_swipe"
            || (resourcePath.StartsWith("pattern/") && resourcePath != "pattern/drawing_card");
        if (singleContent)
        {
            x = 0;
            y = 0;
            width = texture.width;
            height = texture.height;
        }

        // 收紧到不透明像素的紧包围盒:pivot 随之落在内容几何中心,
        // 均匀缩放即天然居中(修鱼纹"往右拉伸/散射"),宽度不含透明空边,
        // 光晕与本体天然同心。贴图未开 Read/Write 时静默保持原矩形(不报错)。
        int preX = x, preY = y, preW = width, preH = height;
        TightenToOpaque(texture, ref x, ref y, ref width, ref height);
        if (singleContent)
        {
            // 诊断:确认贴图是否可读、收紧前后矩形。鱼/蛇/蛙纹若仍偏心/畸变,把这行发我。
            bool tightened = x != preX || y != preY || width != preW || height != preH;
            Debug.Log($"[漓江回声][纹样诊断] {resourcePath} tex={texture.width}x{texture.height} " +
                      $"isReadable={texture.isReadable} 收紧前[{preX},{preY},{preW},{preH}] " +
                      $"收紧后[{x},{y},{width},{height}] 收紧生效={tightened}");
        }

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(x, y, width, height),
            new Vector2(0.5f, 0.5f),
            PixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        spriteCache[cacheKey] = sprite;
        return sprite;
    }

    /// <summary>
    /// 把给定像素矩形(左下原点)收紧到其中不透明像素的紧包围盒,结果写回 ref。
    /// 需要贴图可读(Read/Write);不可读或全透明时保持原矩形不变、不抛异常。
    /// 这样精灵 pivot(0.5,0.5)恰在可见内容几何中心,避免因透明边距造成的偏移与"拉伸"错觉。
    /// </summary>
    private void TightenToOpaque(Texture2D texture, ref int x, ref int y, ref int width, ref int height)
    {
        try
        {
            Color32[] pixels = texture.GetPixels32(); // 未开 Read/Write 会抛异常 → 走 catch 保持原样
            int texW = texture.width;
            int step = Mathf.Max(1, Mathf.Max(width, height) / 512); // 大图降采样,省时
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            for (int yy = 0; yy < height; yy += step)
            {
                int rowBase = (y + yy) * texW + x;
                for (int xx = 0; xx < width; xx += step)
                {
                    if (pixels[rowBase + xx].a < 12)
                    {
                        continue;
                    }

                    if (xx < minX) minX = xx;
                    if (xx > maxX) maxX = xx;
                    if (yy < minY) minY = yy;
                    if (yy > maxY) maxY = yy;
                }
            }

            if (maxX < minX || maxY < minY)
            {
                return; // 全透明:保持原矩形
            }

            // 放宽一个采样步长,避免降采样把边缘像素切掉。
            minX = Mathf.Max(0, minX - step);
            minY = Mathf.Max(0, minY - step);
            maxX = Mathf.Min(width - 1, maxX + step);
            maxY = Mathf.Min(height - 1, maxY + step);

            x += minX;
            y += minY;
            width = maxX - minX + 1;
            height = maxY - minY + 1;
        }
        catch
        {
            // 贴图不可读:保持原矩形(等同旧行为)。
        }
    }

    private Sprite GetSolidSprite(Color color)
    {
        string cacheKey = ColorUtility.ToHtmlStringRGBA(color);
        if (!solidTextureCache.TryGetValue(cacheKey, out Texture2D texture))
        {
            texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[8 * 8];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            solidTextureCache[cacheKey] = texture;
        }

        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 8f);
    }

    private Texture2D CreateFallbackTexture()
    {
        Texture2D texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[64 * 64];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color(0.7f, 0.2f, 0.9f, 0.8f);
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private bool AdvancePressed()
    {
        bool keyboardPressed = Keyboard.current != null &&
                               (Keyboard.current.spaceKey.wasPressedThisFrame ||
                                Keyboard.current.enterKey.wasPressedThisFrame ||
                                Keyboard.current.numpadEnterKey.wasPressedThisFrame);
        bool mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool ovrPressed = leftTriggerDown || rightTriggerDown ||
                          OVRInput.GetDown(OVRInput.Button.One);
        return keyboardPressed || mousePressed || ovrPressed;
    }

    private bool NonPointerConfirmPressed()
    {
        bool keyboardPressed = Keyboard.current != null &&
                               (Keyboard.current.spaceKey.wasPressedThisFrame ||
                                Keyboard.current.enterKey.wasPressedThisFrame ||
                                Keyboard.current.numpadEnterKey.wasPressedThisFrame);
        bool mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool faceButtonPressed = OVRInput.GetDown(OVRInput.Button.One);
        return keyboardPressed || mousePressed || faceButtonPressed;
    }

    private bool MenuPressed()
    {
        bool keyboardPressed = Keyboard.current != null &&
                               (Keyboard.current.escapeKey.wasPressedThisFrame ||
                                Keyboard.current.mKey.wasPressedThisFrame);
        bool ovrPressed = OVRInput.GetDown(OVRInput.Button.Two) ||
                          OVRInput.GetDown(OVRInput.Button.Four) ||
                          OVRInput.GetDown(OVRInput.Button.Start);
        return keyboardPressed || ovrPressed;
    }

    private int ReadHorizontalStep()
    {
        float value = 0f;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.leftArrowKey.isPressed || Keyboard.current.aKey.isPressed)
            {
                value -= 1f;
            }

            if (Keyboard.current.rightArrowKey.isPressed || Keyboard.current.dKey.isPressed)
            {
                value += 1f;
            }
        }

        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        UnityEngine.XR.InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        UnityEngine.XR.InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (leftDevice.isValid && leftDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 leftStick) &&
            Mathf.Abs(leftStick.x) > Mathf.Abs(stick.x))
        {
            stick = leftStick;
        }

        if (rightDevice.isValid && rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 rightStick) &&
            Mathf.Abs(rightStick.x) > Mathf.Abs(stick.x))
        {
            stick = rightStick;
        }

        if (Mathf.Abs(stick.x) > Mathf.Abs(value))
        {
            value = stick.x;
        }

        if (value > 0.45f)
        {
            return 1;
        }

        if (value < -0.45f)
        {
            return -1;
        }

        return 0;
    }
}
