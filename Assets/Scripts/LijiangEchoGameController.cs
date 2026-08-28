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
    // 鱼纹图内容不在图片正中,导致落点看着偏右;单击(鱼纹)整体落点左移补偿(负=左移)。可调。
    private const float FishNoteXOffset = -0.14f;

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
    private const float HitBlockVisibleHeight = 0.34f;
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
    private Transform monsterRoot;
    private Transform introScrollRoot;
    private Transform introPreLevelRoot;
    private VideoPlayer introVideoPlayer;
    private RenderTexture introVideoTexture;
    private AudioSource ambienceSource;
    private AudioSource battleMusicSource;
    private AudioSource sfxSource;
    private bool battleMusicStarted;
    private float battleMusicTime;
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

    private int nextSpawnIndex;
    private int nextNoteIndex;
    private int cardPageIndex;
    private int score;
    private int combo;
    private float feedbackTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureController()
    {
        if (!SceneManager.GetSceneByName("LijiangEchoMR_Main").isLoaded)
        {
            // 开始/选关已拆到独立场景（Stage_Start/Stage_Select），本控制器只在旧主场景
            // 加载后才自动生成，避免在 Bootstrap/新阶段场景里重复搭建一套内容。
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
            false);
        RegisterMotion(sourcePattern, MotionKind.Pulse, 0.01f, 1.7f, 0f);

        tracePoints = BuildTracePath(selectedLevel);

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

        // 双手镜像绘制:把指引线/已描绘线/光标镜像到对侧(x→-x),形成左右对称的双手画效果
        // (会议:"画的虚线直接复制过来")。开关 ExternalTraceMirror 默认开;设 false 则单手画全程。
        if (ExternalTraceMirror ?? true)
        {
            LineRenderer mirrorGuide = AddLineRenderer("纹样描绘指引(镜像)", 0.03f, new Color(1f, 0.9f, 0.55f, 0.16f), 30);
            mirrorGuide.positionCount = tracePoints.Length;
            for (int gi = 0; gi < tracePoints.Length; gi++)
            {
                Vector3 gp = tracePoints[gi];
                mirrorGuide.SetPosition(gi, new Vector3(-gp.x, gp.y, gp.z - 0.018f));
            }

            traceMirrorDrawRenderer = AddLineRenderer("已描绘轨迹(镜像)", 0.072f, new Color(1f, 0.86f, 0.28f, 0.98f), 34);
            traceMirrorDrawRenderer.colorGradient = traceGlowGradient;

            GameObject mirrorPointerObject = AddIcon("battle/hit_ring_center", "手柄描绘光标(镜像)", new Vector3(0f, 0f, TracePlaneZ - 0.04f), 0.105f, 42, 0.92f);
            traceMirrorPointer = mirrorPointerObject.transform;
            traceMirrorPointer.gameObject.SetActive(false);
        }
        else
        {
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

        if (!TryGetTracePointer(out Vector3 localPoint, out bool drawing))
        {
            if (tracePointer != null)
            {
                tracePointer.gameObject.SetActive(false);
            }

            if (traceMirrorPointer != null)
            {
                traceMirrorPointer.gameObject.SetActive(false);
            }

            hasPreviousTracePointer = false;
            return;
        }

        if (tracePointer != null)
        {
            tracePointer.gameObject.SetActive(true);
            tracePointer.localPosition = new Vector3(localPoint.x, localPoint.y, TracePlaneZ - 0.04f);
        }

        if (traceMirrorPointer != null)
        {
            traceMirrorPointer.gameObject.SetActive(true);
            traceMirrorPointer.localPosition = new Vector3(-localPoint.x, localPoint.y, TracePlaneZ - 0.04f);
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
        int advanced = 0;
        while (tracePointIndex < tracePoints.Length && advanced < 10)
        {
            float distance = hasPreviousTracePointer
                ? DistanceToSegment(tracePoints[tracePointIndex], previousTracePointer, pointerOnPlane)
                : Vector3.Distance(tracePoints[tracePointIndex], pointerOnPlane);
            if (distance > TracePointTolerance)
            {
                break;
            }

            tracePointIndex++;
            advanced++;
        }

        previousTracePointer = pointerOnPlane;
        hasPreviousTracePointer = true;
        UpdateTraceLine();
        if (traceFeedbackText != null && tracePointIndex < tracePoints.Length)
        {
            traceFeedbackText.text = $"描画进度 {Mathf.RoundToInt(tracePointIndex * 100f / tracePoints.Length)}%";
        }

        if (tracePointIndex >= tracePoints.Length)
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

    private Vector3[] BuildTracePath(int level)
    {
        List<Vector3> points = new List<Vector3>();
        if (level == 2)
        {
            const int circlePoints = 72;
            for (int i = 0; i < circlePoints; i++)
            {
                float angle = Mathf.PI * 0.5f - i / (float)(circlePoints - 1) * Mathf.PI * 2f;
                points.Add(new Vector3(Mathf.Cos(angle) * 0.43f, Mathf.Sin(angle) * 0.43f + 0.02f, TracePlaneZ));
            }

            return points.ToArray();
        }

        Vector2[] controls = level == 0
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
        TextAsset chart = Resources.Load<TextAsset>("LijiangEchoCharts/chart_generated");
        if (chart == null)
        {
            chart = Resources.Load<TextAsset>("LijiangEchoCharts/chart_liusanjie");
        }

        if (chart == null || string.IsNullOrEmpty(chart.text))
        {
            return;
        }

        List<KeyValuePair<float, string>> rows = new List<KeyValuePair<float, string>>();
        foreach (string rawLine in chart.text.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
            {
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
        }

        noteTimes = times;
        holdNoteIndices = holds;
        doubleNoteIndices = doubles;
        Debug.Log($"[漓江回声] 已从谱面表格加载 {noteTimes.Length} 个音符(长按 {holds.Count}、双击 {doubles.Count})。");
    }

    // ===== 左右手击打(对应 VR 手柄左/右手) =====
    private const float HandStrikeDuration = 0.24f;
    private const float HandRestAngle = 180f;    // 平时:手臂朝下(手在轴下方、藏于画面下方)
    private const float HandStrikeAngle = 22f;   // 击打:从下方向上旋转,朝中心圆环的角度
    private const float HandArmLength = 1.0f;     // 臂长(手离轴心多远)
    private const float HandPivotSide = 0.35f;    // 左右轴心离中线的横向距离
    private const float HandPivotY = -0.82f;      // 轴心高度(越负越靠下)

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
        pivotObject.transform.localPosition = new Vector3(sideSign * HandPivotSide, HandPivotY, -0.55f); // 偏下两侧的轴心
        pivotObject.transform.localRotation = Quaternion.Euler(0f, 0f, HandRestAngle); // 平时手臂朝下
        spawnedObjects.Add(pivotObject);

        GameObject hand = AddIcon(art, handName, Vector3.zero, 0.55f, 240, 0f); // 初始全透明
        hand.transform.SetParent(pivotObject.transform, false);
        hand.transform.localPosition = new Vector3(0f, HandArmLength, 0f); // 手在轴的"手臂末端"
        hand.transform.localRotation = Quaternion.identity;
        handRenderer = hand.GetComponent<SpriteRenderer>();
        return pivotObject.transform;
    }

    private void UpdateBattleHands()
    {
        UpdateBattleHand(leftHandPivot, leftHandRenderer, ref leftHandStrikeTimer, -1f);
        UpdateBattleHand(rightHandPivot, rightHandRenderer, ref rightHandStrikeTimer, 1f);
    }

    private void UpdateBattleHand(Transform pivot, SpriteRenderer hand, ref float timer, float sideSign)
    {
        if (pivot == null)
        {
            return;
        }

        float rest = HandRestAngle;                 // 手臂朝下藏起
        float strike = sideSign * HandStrikeAngle;  // 向上旋转、朝中心圆环
        float angle = rest;
        float alpha = 0f; // 平时全透明(VR 里镜头外也看得见,靠透明来隐藏)
        if (timer > 0f)
        {
            timer -= Time.deltaTime;
            float progress = 1f - Mathf.Clamp01(timer / HandStrikeDuration);
            float swing = Mathf.Sin(progress * Mathf.PI); // 0→1→0:向上击打再落回
            angle = Mathf.Lerp(rest, strike, swing);
            alpha = Mathf.Clamp01(swing * 1.6f); // 挥起时显现,落回时淡出
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

        BuildBattleBackground();

        AddIcon("ui/settings", "左上设置入口", new Vector3(-2.42f, 1.05f, -0.38f), 0.24f, 70, 0.9f);
        GameObject centerRingObject = AddIcon(
            "battle/hit_ring_center",
            "中央节奏判定双圆环",
            new Vector3(0f, 0f, -0.82f),
            HitRingVisibleHeight,
            190,
            1f);
        ringRenderer = centerRingObject.GetComponent<SpriteRenderer>();
        ringTransform = centerRingObject.transform;
        ringBaseScale = ringTransform.localScale;

        RectInt[] traceCrops =
        {
            new RectInt(273, 2314, 1951, 2547),
            new RectInt(1822, 2125, 2973, 2185),
            new RectInt(995, 836, 1335, 1359)
        };
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
    /// 战斗静态舞台背景(远山/人群/怪物/火焰/祭坛/装饰手/边框)。抽成独立方法,
    /// 为后续"战斗场景化"(把这块烘焙成可在场景里直接摆位的物体)做准备。
    /// 目前仍在 ShowBattle 里运行时构建,视觉与之前完全一致。
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
        RegisterMotion(leftTopUpper, MotionKind.Hand, 0.022f, 4.6f, 0.2f);
        RegisterMotion(leftTopFore, MotionKind.Hand, 0.032f, 5.4f, 0.9f);
        RegisterMotion(rightTopUpper, MotionKind.Hand, 0.022f, 4.7f, 1.1f);
        RegisterMotion(rightTopFore, MotionKind.Hand, 0.032f, 5.2f, 1.8f);
        RegisterMotion(leftBottomUpper, MotionKind.Hand, 0.02f, 4.1f, 2.4f);
        RegisterMotion(leftBottomFore, MotionKind.Hand, 0.028f, 5f, 2.9f);
        RegisterMotion(rightBottomUpper, MotionKind.Hand, 0.02f, 4.2f, 3.2f);
        RegisterMotion(rightBottomFore, MotionKind.Hand, 0.028f, 5.1f, 3.7f);

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
        if (countdownTime < 0f)
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
            // 用户反馈:单击用鱼纹。select/fish_symbol 是独立整图,传超范围矩形 → GetCroppedSprite
            // 会夹到整图,即用整张鱼纹(无需知道其像素尺寸)。
            string resourcePath = "select/fish_symbol";
            RectInt crop = new RectInt(0, 0, 100000, 100000);
            float startHeight = 0.28f;
            float targetHeight = HitBlockVisibleHeight;
            string objectName = "鱼纹单击_" + nextSpawnIndex;
            if (kind == NoteKind.Hold)
            {
                resourcePath = "pattern/snake_done";
                crop = SnakeDoneCrop;
                startHeight = 0.34f;
                targetHeight = 0.5f;
                objectName = "蛇纹长按_" + nextSpawnIndex;
            }
            else if (kind == NoteKind.Swipe)
            {
                resourcePath = "battle/frog_swipe";
                crop = FrogSwipeCrop;
                startHeight = 0.26f;
                targetHeight = 0.37f;
                objectName = "蛙纹滑动_" + nextSpawnIndex;
            }
            else if (kind == NoteKind.Double)
            {
                // P4/P6：双击音符——用不同纹样(鸟纹)与单击(hit_block)在视觉上区分。
                // 仅当 doubleNoteIndices 里填了 index 才会出现；输入仍按单击命中处理。
                resourcePath = "pattern/bird_done";
                crop = BirdDoneCrop;
                startHeight = 0.28f;
                targetHeight = 0.44f;
                objectName = "双击纹样_" + nextSpawnIndex;
            }

            // 鱼纹(单击)落点左移补偿其图内容偏右;其余纹样仍落正中心。
            if (kind == NoteKind.Strike)
            {
                targetX = FishNoteXOffset;
            }

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

            // 加色柔光光晕:同纹样叠 2 层、越外越淡的金色,加色混合叠出外扩柔和的发光(比 3 层收敛)。
            // 关键:每层要以"纹样可见中心(sprite.bounds.center)"为中心放大,否则纹样原点偏移时
            // 放大的光晕会相对本体错位(鱼纹那种"散射"就是这个原因)。
            Vector3 spriteCenter = noteRenderer.sprite != null ? noteRenderer.sprite.bounds.center : Vector3.zero;
            float[] glowScales = { 1.35f, 1.75f };
            float[] glowBase = { 0.40f, 0.20f };
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
            if (note.Renderer == null)
            {
                activeNotes.RemoveAt(i);
                continue;
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
            SetCroppedSpritePose(
                note.Renderer,
                visibleCenter,
                Mathf.Lerp(note.TargetHeight * 0.76f, note.TargetHeight, eased) *
                (holdActive && heldNote == note ? 1f + Mathf.Sin(Time.time * 8f) * 0.035f : 1f),
                Mathf.Lerp(0.42f, 1f, eased),
                false); // 不镜像,朝正中心飞

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
                Destroy(note.Renderer.gameObject);
                activeNotes.RemoveAt(i);
            }
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
        foreach (RhythmNote note in activeNotes)
        {
            if (!note.Judged && note.ChartIndex == nextNoteIndex)
            {
                note.Judged = true;
                if (note.Renderer != null)
                {
                    Destroy(note.Renderer.gameObject);
                }
                break;
            }
        }

        // 触发左右手挥击:双击两手一起,否则按该音符的一侧
        TriggerHandStrike(GetNoteKind(nextNoteIndex) == NoteKind.Double ? 0f : hitSide);

        nextNoteIndex++;
        holdActive = false;
        holdProgress = 0f;
        heldNote = null;
        hitFlashTimer = 0.18f;
        SetFeedback(message, color);
        PlaySfx("hit", 0.78f);
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
        bool keyboardSwipe = Keyboard.current != null &&
                             (Keyboard.current.leftArrowKey.wasPressedThisFrame ||
                              Keyboard.current.rightArrowKey.wasPressedThisFrame ||
                              Keyboard.current.upArrowKey.wasPressedThisFrame ||
                              Keyboard.current.downArrowKey.wasPressedThisFrame);
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
            return inwardVelocity >= 0.28f ||
                   Mathf.Abs(localVelocity.y) >= 0.52f ||
                   Mathf.Abs(localVelocity.z) >= 0.52f;
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
            ShowSelect();
        }
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

        battleMusicSource.Stop();
        battleMusicSource.clip = clip;
        battleMusicSource.loop = false;
        battleMusicSource.volume = 0.86f;
        battleMusicSource.time = 0f;
        battleMusicSource.Play();
        Debug.Log($"[漓江回声] 战斗音乐开始，时长 {clip.length:F2} 秒");
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
        Transform parent = null)
    {
        GameObject spriteObject = new GameObject(objectName);
        spriteObject.transform.SetParent(parent != null ? parent : stageRoot, false);

        SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetCroppedSprite(resourcePath, topLeftCrop);
        renderer.sortingOrder = order;
        SetCroppedSpritePose(renderer, visibleCenter, targetHeight, alpha, mirrorX);

        spawnedObjects.Add(spriteObject);
        return spriteObject;
    }

    private void SetCroppedSpritePose(SpriteRenderer renderer, Vector3 visibleCenter, float targetHeight, float alpha, bool mirrorX)
    {
        if (renderer == null || renderer.sprite == null || renderer.sprite.bounds.size.y <= 0f)
        {
            return;
        }

        float scale = targetHeight / renderer.sprite.bounds.size.y;
        renderer.transform.localPosition = visibleCenter;
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
