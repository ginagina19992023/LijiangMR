using System.Collections.Generic;
using Meta.XR.MRUtilityKit;
using UnityEngine;

/// <summary>
/// 扫码出纹样(见 docs/TODO-QR-PATTERN-SCAN.md)。整条链路的入口。
///
/// 三段:
///   ① 扫到二维码 → 在【二维码所在的真实位置】浮现光圈       ← 本组件
///   ② 入场动画 3~5 秒                                      ← LijiangEchoPatternIntro
///   ③ 打击环节                                             ← LijiangEchoPatternStrike
///
/// 【为什么不用摄像头取帧 + ZXing】
/// 文档里原本估的做法是自己取摄像头帧、引入 ZXing 解码、再反算二维码的空间位置。
/// 但 Meta XR SDK 201 的 MRUK 已经把这件事做完了:系统级的 QR 追踪直接给出
/// 【解码后的字符串】和【一个持续跟踪的世界锚点】。所以这里不引第三方库、
/// 不自己写解码,也不用手算距离 —— 锚定精度和稳定性都由系统负责。
///   · MRUK.Instance.QRCodeTrackingSupported   —— 设备支不支持(Quest 2 不支持)
///   · SceneSettings.TrackerConfiguration      —— 打开 QR 追踪
///   · SceneSettings.TrackableAdded            —— 认出来了,给一个 MRUKTrackable
///   · MRUKTrackable.MarkerPayloadString       —— 二维码里的字符串
///   · MRUKTrackable.transform / PlaneRect     —— 世界位姿 + 实际边长
/// 权限 com.oculus.permission.USE_SCENE 和 USE_ANCHOR_API 工程里已经声明过了。
///
/// 二维码内容是我们自己定的短标识串(不是网址,免得头显系统当链接截走),
/// 打印用的四张见 D:\Recording\qrcodes\ 或用同样的内容自己生成:
///   lijiang:fish / lijiang:snake / lijiang:frog / lijiang:bird
/// </summary>
public class LijiangEchoQrScan : MonoBehaviour
{
    public const string PayloadPrefix = "lijiang:";

    private enum Phase
    {
        WaitingForCode,   // 还没扫到
        Intro,            // ② 入场动画播放中
        Strike,           // ③ 打击环节
        Finished          // 演完了,等着扫下一张
    }

    [Header("锚定")]
    [Tooltip("动画整体相对二维码边长的倍数。二维码印 10cm 的话,1.0 表示演出范围约 10cm 见方 —— 通常要放大。")]
    [SerializeField] private float sizeRelativeToCode = 6f;

    [Tooltip("动画往二维码正前方(离开纸面的方向)推出多远,单位米。太小会和纸面穿插。")]
    [SerializeField] private float liftOffPaper = 0.04f;

    [Tooltip("扫到二维码后,动画是否一直跟着它。关掉的话只在扫到的那一刻记下位置,之后二维码动了也不跟。")]
    [SerializeField] private bool followCode = true;

    [Tooltip("两段共用的光圈大小(世界单位)。"
        + "入场动画和打击环节都由这里统一下发 —— 各模块自己序列化的值一律不作数,"
        + "这样场景里存着的旧值(比如老的 0.62)也不会让两段光圈一大一小。")]
    [SerializeField] private float sharedRingSize = LijiangEchoPatternIntro.DefaultRingSize;

    [Header("行为")]
    [Tooltip("一张码演完之后,能不能再扫一次(同一张也算)。关掉的话一次玩完就结束。")]
    [SerializeField] private bool allowRescan = true;

    [Tooltip("演完之后隔多久才接受下一次扫码,免得站着不动被反复触发。")]
    [SerializeField] private float rescanCooldown = 2f;

    [Tooltip("启动后隔多久才第一次请求二维码追踪(秒)。"
        + "真机日志显示:启动后 0.2 秒去配必失败(空间子系统还没就绪),所以别急着配。")]
    [SerializeField] private float firstRequestDelay = 3f;

    [Tooltip("二维码追踪没配起来时,隔多久重试一次(秒)。")]
    [SerializeField] private float trackerRetryInterval = 2f;

    [Tooltip("最多重试几次。系统在会话刚起来那零点几秒是配不上的,要给它时间。")]
    [SerializeField] private int trackerMaxAttempts = 8;

    [Tooltip("请求扫码前先把房间数据加载一次。"
        + "追踪器和房间锚点走同一套空间服务,房间没加载过的话那套服务可能没建立上下文。")]
    [SerializeField] private bool loadSceneBeforeTracking = true;

    [Tooltip("房间数据加载失败时,自动拉起系统的空间设置流程。"
        + "会打断一次玩家,但没有房间数据的话整个空间服务(含二维码追踪)都用不了。")]
    [SerializeField] private bool requestSceneCaptureIfMissing = true;

    [Header("电脑上跑测(没有头显时)")]
    [Tooltip("在编辑器里按 1/2/3/4 直接触发鱼/蛇/蛙/鸟,跳过真实扫码。真机上不影响。")]
    [SerializeField] private bool simulateWithKeyboard = true;

    [Tooltip("模拟的二维码摆在相机正前方多远(米)。太近会被近裁剪面切掉,太远看不清。")]
    [SerializeField] private float simulateDistance = 1.2f;

    [Tooltip("头显里的保底触发:按手柄按键就当扫到一张码,四个纹样轮着来。"
        + "扫码万一不灵,靠它也能把整套演出跑完。")]
    [SerializeField] private bool allowManualTrigger = true;

    [Header("提示文字")]
    [SerializeField] private bool showStatusText = true;
    [SerializeField] private float statusTextSize = 0.022f;

    [Tooltip("提示文字在视野里往下压多少(米)。反馈:原来挡着画面了,所以调大。")]
    [SerializeField] private float statusTextDrop = 0.55f;

    [Tooltip("提示文字离玩家多远(米)。放远一点也会显得更靠边。")]
    [SerializeField] private float statusTextDistance = 1.35f;

    [Header("画面底色")]
    [Tooltip("给相机铺一层黑底,和其他场景一致。\n"
        + "电脑上能看见(纹样在黑底上才看得清),头显上看不见 —— 因为 Alpha 是 0,"
        + "Passthrough 会把真实世界合成进来,黑底不会挡住。")]
    [SerializeField] private bool useBlackBackdrop = true;

    [Tooltip("底色。Alpha 必须留 0,否则真机上会把 Passthrough 整个遮死。")]
    [SerializeField] private Color backdropColor = new Color(0.04f, 0.03f, 0.055f, 0f);

    // ——— 运行时 ———
    private Phase phase = Phase.WaitingForCode;
    private LijiangEchoPatternIntro intro;
    private Transform anchorRoot;          // 钉在二维码上的舞台根
    private MRUKTrackable currentCode;
    private GameObject simulatedCode;   // 电脑上跑测时假装的那张码
    private float finishedAt = -999f;
    private bool trackingRequested;
    private float lastWaitingLogAt = -99f;
    private string lastStatus;

    private readonly List<MRUKTrackable> scratch = new List<MRUKTrackable>();
    private readonly List<GameObject> statusSpawned = new List<GameObject>();
    private Transform statusRoot;
    private TextMesh statusText;

    // ————————————————————————————— 生命周期 —————————————————————————————

    private void Start()
    {
        intro = GetComponent<LijiangEchoPatternIntro>();
        if (intro == null)
        {
            intro = gameObject.AddComponent<LijiangEchoPatternIntro>();
        }

        RequestScenePermission();
        EnsurePassthrough();
        ApplyBackdrop();
        BuildStatusText();
        SetStatus("正在启动扫码…");

        Debug.Log("[漓江回声] 扫码模块已启动。"
            + $"设备支持扫码={(MRUK.Instance != null && MRUK.Instance.QRCodeTrackingSupported)}");

        LogMarkerExtensions();
    }

    /// <summary>把标记追踪相关的 OpenXR 扩展【实际有没有被启用】打出来。
    ///
    /// 为什么需要:真机上 QRCodeTrackingSupported 报 True,配置却一直失败
    /// (ErrorUnknown,连试 9 次都一样)。"支持"和"这次会话真的启用了"是两回事 ——
    /// 系统只是列出它认识这个扩展,不代表授予了本应用。
    ///
    /// 尤其 XR_METAX1_spatial_entity_marker 是 Meta 的【实验性】扩展(METAX1 前缀),
    /// 这类扩展要求 manifest 里带 com.oculus.experimental.enabled 才会放行。
    /// 打出来就能一眼定论,不用再靠猜。</summary>
    private static void LogMarkerExtensions()
    {
        string[] wanted =
        {
            "XR_METAX1_spatial_entity_marker",
            "XR_EXT_spatial_marker_tracking",
            "XR_EXT_spatial_entity",
            "XR_META_spatial_entity_discovery"
        };

        System.Text.StringBuilder sb = new System.Text.StringBuilder("[漓江回声] 标记追踪相关扩展实际启用情况:");
        foreach (string name in wanted)
        {
            bool on = UnityEngine.XR.OpenXR.OpenXRRuntime.IsExtensionEnabled(name);
            sb.Append($"\n  {name} = {(on ? "已启用" : "【没启用】")}");
        }

        Debug.Log(sb.ToString());
    }

    /// <summary>运行时申请场景权限。
    ///
    /// ⚠️ 踩过的坑:manifest 里声明了 com.oculus.permission.USE_SCENE 并【不等于】拿到了它。
    /// 这是 Android 的危险权限,必须运行时申请;真机上查 dumpsys 看到的是 granted=false,
    /// 于是 MRUK 配不了追踪器 —— 二维码放到眼前也毫无反应,而且不报错、不打日志,
    /// 表现就是"界面正常、就是不认码",极难查。
    ///
    /// 两个名字都申请:老的 com.oculus.* 和 Horizon OS 的 horizonos.*,不同系统版本认的不一样。</summary>
    private void RequestScenePermission()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        string[] needed =
        {
            "com.oculus.permission.USE_SCENE",
            "horizonos.permission.USE_SCENE"
        };

        foreach (string permission in needed)
        {
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(permission))
            {
                continue;
            }

            Debug.Log("[漓江回声] 申请权限:" + permission);
            UnityEngine.Android.Permission.RequestUserPermission(permission);
        }
#endif
    }

    /// <summary>确保场景里有透视层,没有就补一个。
    ///
    /// 踩过的坑:真机上进去【整个是黑的】,只有提示字浮着。原因不是黑底没去掉 ——
    /// 是这个场景压根没有 OVRPassthroughLayer,合成的时候没有真实世界可贴,
    /// 看到的就是相机那层近黑的清屏色。
    ///
    /// ForceEnablePassthrough 会打开 OVRManager.isInsightPassthroughEnabled,但它只
    /// 【配置已有的】层、不会创建。正式场景那层藏在 OVRCameraRig 的 prefab 实例里,
    /// 所以场景文件里看不见,而搭扫码场景时用的是包里那个干净的 OVRCameraRig,没带层。
    ///
    /// Underlay = 透视垫在所有渲染内容【下面】,配合相机 Alpha=0 的清屏,
    /// 没画东西的地方就露出真实世界。</summary>
    private void EnsurePassthrough()
    {
        OVRManager manager = OVRManager.instance != null
            ? OVRManager.instance
            : FindFirstObjectByType<OVRManager>(FindObjectsInactive.Include);
        if (manager != null)
        {
            manager.isInsightPassthroughEnabled = true;
        }

        OVRPassthroughLayer layer = FindFirstObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
        if (layer == null)
        {
            // 挂在相机机位上;没有机位就单起一个物体,免得场景里什么都没有时直接崩
            GameObject host = manager != null ? manager.gameObject : new GameObject("漓江回声_透视层");
            layer = host.AddComponent<OVRPassthroughLayer>();
            Debug.Log("[漓江回声] 场景里没有 OVRPassthroughLayer,已自动补一个(否则真机上是全黑的)。");
        }

#pragma warning disable CS0618
        layer.overlayType = OVROverlay.OverlayType.Underlay;
#pragma warning restore CS0618
        layer.hidden = false;
        layer.enabled = true;
    }

    /// <summary>黑底:电脑上看得见、头显上看不见。
    ///
    /// 靠的是 Alpha —— 相机用纯色清屏,RGB 是那个近黑的紫灰(和 LijiangEchoStageKit
    /// 的预览相机同一个色),但 Alpha 设成 0。桌面渲染不理会这个 Alpha,所以你在
    /// Game 视图里看到的是黑底;真机上 Passthrough 按 Alpha 做合成,0 就等于
    /// "这里全给真实世界",黑底一点都挡不住。
    ///
    /// 这也正是 LijiangEchoMrValidation 要求的设置(SolidColor + Alpha < 0.01),
    /// 所以顺手也把这个场景校验过了。</summary>
    private void ApplyBackdrop()
    {
        if (!useBlackBackdrop)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        cam.clearFlags = CameraClearFlags.SolidColor;

        Color color = backdropColor;
        color.a = 0f;   // 保险:Alpha 不为 0 的话真机上就是一块黑布糊住整个世界
        cam.backgroundColor = color;
    }

    private void OnDestroy()
    {
        MRUK mruk = MRUK.Instance;
        if (mruk != null && mruk.SceneSettings != null)
        {
            mruk.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
            mruk.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
        }
    }

    private void Update()
    {
        // MRUK 可能比本组件晚一步就绪,所以没成功就每帧再试;
        // 但别在启动那零点几秒就去配 —— 那时候空间子系统还没起来,必失败。
        if (!trackingRequested && Time.timeSinceLevelLoad >= firstRequestDelay)
        {
            RequestQrTracking();
        }

        UpdateTrackerRetry();

        if (phase == Phase.WaitingForCode)
        {
            PollForAlreadyDetectedCodes();
            PollKeyboardSimulation();
            PollManualTrigger();
        }
        else if (phase == Phase.Strike)
        {
            UpdateStrike();
        }
        else if (phase == Phase.Finished && allowRescan && Time.time - finishedAt > rescanCooldown)
        {
            phase = Phase.WaitingForCode;
            SetStatus("把二维码放进视野");
        }

        if (selfCheckAt > 0f && Time.time >= selfCheckAt)
        {
            selfCheckAt = -1f;
            RunSelfCheck();
        }

        FaceStatusTextToPlayer();
    }

    // ————————————————————————————— ① 扫码 —————————————————————————————

    /// <summary>打开系统的二维码追踪。MRUK 还没起来时返回 false,由 Update 继续重试。</summary>
    private void RequestQrTracking()
    {
        MRUK mruk = MRUK.Instance;
        if (mruk == null || mruk.SceneSettings == null)
        {
            SetStatus("等待 MRUK 初始化…");

            // 这一支原来一声不吭地 return,真机上排查时等于全瞎 —— 什么日志都没有,
            // 看不出是卡在这儿还是压根没跑到。隔几秒报一次(别每帧刷屏)。
            if (Time.time - lastWaitingLogAt > 3f)
            {
                lastWaitingLogAt = Time.time;
                Debug.LogWarning("[漓江回声] MRUK 还没就绪"
                    + $"(Instance={(mruk == null ? "空" : "有")},"
                    + $"SceneSettings={(mruk != null && mruk.SceneSettings != null ? "有" : "空")}),"
                    + "二维码追踪起不来。检查场景里有没有 MRUK 物体。");
            }

            return;
        }

        if (!mruk.QRCodeTrackingSupported)
        {
            // Quest 2 没有彩色透视摄像头,系统层面就不支持 —— 优雅降级,不要卡死
            trackingRequested = true;
            SetStatus(simulateWithKeyboard
                ? "本设备不支持扫码(Quest 3/3S 才有)\n电脑上可按 1/2/3/4 试玩"
                : "本设备不支持扫码,需要 Quest 3 / 3S");
            Debug.LogWarning("[漓江回声] 这台设备不支持二维码追踪(Quest 2 没有彩色透视摄像头)。");
            return;
        }

        // TrackerConfiguration 是 struct,得取出来改完再塞回去,直接改属性是改不到的
        OVRAnchor.TrackerConfiguration config = mruk.SceneSettings.TrackerConfiguration;
        config.QRCodeTrackingEnabled = true;
        mruk.SceneSettings.TrackerConfiguration = config;

        mruk.SceneSettings.TrackableAdded.RemoveListener(OnTrackableAdded);
        mruk.SceneSettings.TrackableAdded.AddListener(OnTrackableAdded);
        mruk.SceneSettings.TrackableRemoved.RemoveListener(OnTrackableRemoved);
        mruk.SceneSettings.TrackableRemoved.AddListener(OnTrackableRemoved);

        trackingRequested = true;
        SetStatus("把二维码放进视野");
        Debug.Log("[漓江回声] 已向系统请求二维码追踪,等待生效。");

        LoadSceneOnce();
    }

    private bool sceneLoadStarted;

    /// <summary>先让 MRUK 把房间数据load 上来,再谈追踪二维码。
    ///
    /// 一开始我把 LoadSceneOnStartup 关了 —— 想着"只要扫码,不需要房间网格"。
    /// 但真机日志里 ConfigureTrackers 失败之后紧跟着的就是
    ///   SP:AF:AnchorFramework: coroDiscoverSpaces ...
    ///   MRUK Shared: queryCompleteEvent->result returned error code: -2   (XR_ERROR_RUNTIME_FAILURE)
    /// 也就是【空间发现】这一步在报错。追踪器和房间锚点走的是同一套空间服务,
    /// 房间从没加载过的话,这套服务的上下文可能压根没建立起来。
    ///
    /// 所以这里主动加载一次;设备上没有房间数据时不弹系统的房间扫描
    /// (requestSceneCaptureIfNoDataFound: false)—— 那会把玩家踢出应用,
    /// 现场体验太差,宁可只记一条日志。</summary>
    private async void LoadSceneOnce()
    {
        if (sceneLoadStarted || !loadSceneBeforeTracking)
        {
            return;
        }

        sceneLoadStarted = true;

        MRUK mruk = MRUK.Instance;
        if (mruk == null)
        {
            return;
        }

        try
        {
            // 先安静地试一次:设备上已经有房间数据的话,这一次就成了,不打扰玩家
            MRUK.LoadDeviceResult result = await mruk.LoadSceneFromDevice(false);
            Debug.Log($"[漓江回声] 房间数据加载结果:{result}");

            if (result == MRUK.LoadDeviceResult.Success)
            {
                return;
            }

            // 没成:很可能这台头显根本没做过空间设置。
            // ⚠️ 这一步会把玩家送进系统的房间扫描流程 —— 打扰,但它是【一次性】的,
            // 而且没有房间数据的话整个空间服务(连带二维码追踪)都用不了。
            // 与其让人对着二维码干瞪眼、还查不出原因,不如让系统把该做的事引导完。
            if (!requestSceneCaptureIfMissing)
            {
                Debug.LogWarning("[漓江回声] 房间数据加载失败,且已关闭自动引导空间设置。"
                    + "请在头显里手动完成:设置 → 实体空间 → 空间设置。");
                return;
            }

            SetStatus("需要先设置房间\n请按系统提示完成一次空间设置");
            Debug.Log("[漓江回声] 房间数据加载失败,拉起系统的空间设置流程。");

            MRUK.LoadDeviceResult second = await mruk.LoadSceneFromDevice(true);
            Debug.Log($"[漓江回声] 空间设置之后再次加载:{second}");

            if (second == MRUK.LoadDeviceResult.Success)
            {
                // 空间服务这下应该通了,让追踪器重头再配一次
                trackerAttempts = 0;
                trackerToggledOff = false;
                nextTrackerRetryAt = 0f;
                SetStatus("把二维码放进视野");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[漓江回声] 加载房间数据出错:" + e.Message);
        }
    }

    // 追踪器重试
    private int trackerAttempts;
    private float nextTrackerRetryAt;
    private bool trackerToggledOff;

    /// <summary>盯着追踪器有没有【真的】开起来,没开就重来。
    ///
    /// 真机日志里查出来的:
    ///   ErrorUnknown: Unable to fully satisfy requested tracker configuration
    ///   MRUK Shared: queryCompleteEvent->result returned error code: -2
    /// 时间是启动后 0.2 秒 —— 那会儿空间子系统还没就绪,配置就失败了。
    ///
    /// 而 MRUK 自己【不会重试】:ConfigureTrackerAndLogResult 之前就把
    /// _lastRequestedConfiguration 设成了目标值,之后 _lastRequestedConfiguration != desiredConfig
    /// 永远不成立,失败一次这一整局就再也不配了 —— 表现就是"码放眼前毫无反应"。
    ///
    /// 办法:把请求的配置先关掉再打开,desiredConfig 变了,MRUK 就会重新配一次。</summary>
    private void UpdateTrackerRetry()
    {
        MRUK mruk = MRUK.Instance;
        if (!trackingRequested || mruk == null || mruk.SceneSettings == null)
        {
            return;
        }

        // 已经真的生效了就收工
        if (mruk.TrackerConfiguration.QRCodeTrackingEnabled)
        {
            if (trackerAttempts > 0)
            {
                Debug.Log($"[漓江回声] 二维码追踪已生效(重试了 {trackerAttempts} 次)。");
                trackerAttempts = -1;   // 只报一次
            }

            return;
        }

        if (trackerAttempts < 0 || trackerAttempts >= trackerMaxAttempts || Time.time < nextTrackerRetryAt)
        {
            return;
        }

        nextTrackerRetryAt = Time.time + trackerRetryInterval;

        // 【怎么才能真的让 MRUK 重配】—— 试错两次才找对:
        //
        // ✗ 同一帧里把 QRCodeTrackingEnabled 关掉再打开:MRUK 在它自己的 Update 里
        //   采样时值已经变回"开"了,和它记的 _lastRequestedConfiguration 一样,没察觉。
        // ✗ 拆成两拍分帧关/开:也不行。MRUK 第一道闸是
        //       if (TrackerConfiguration == desiredConfig) return;
        //   配置从没成功过,所以实际状态 TrackerConfiguration 一直是 default(QR=false),
        //   而我写的"关"也是 QR=false —— 两者相等,它直接 return,压根没记下这次"关"。
        // ✓ 用它自己的 OnDisable:那里面把 _lastRequestedConfiguration、
        //   TrackerConfiguration、_configureTrackersTask 全清成初始值,正是一次干净重置。
        //   而且 MRUK 没有 OnEnable,重新启用不会有别的副作用。
        if (trackerToggledOff)
        {
            trackerToggledOff = false;
            trackerAttempts++;

            mruk.enabled = true;   // 重新启用 → 下一帧它会拿着 QR=true 重新配一次

            SetStatus($"正在开启扫码…({trackerAttempts})");
            Debug.Log($"[漓江回声] 重新请求二维码追踪(第 {trackerAttempts} 次)。");
        }
        else
        {
            trackerToggledOff = true;
            mruk.enabled = false;   // 触发 OnDisable,把追踪器状态清干净
            return;                 // 这一拍只负责重置,别急着判失败
        }

        if (trackerAttempts >= trackerMaxAttempts)
        {
            SetStatus(allowManualTrigger
                ? "扫码暂时用不了\n按手柄扳机可以直接看纹样"
                : "扫码开不起来\n请退出应用重进,或检查系统权限");
            Debug.LogError("[漓江回声] 二维码追踪重试用尽,仍未生效。"
                + "检查:头显系统是否 v74+、应用的「空间数据」权限是否允许。");
        }
    }

    private void OnTrackableAdded(MRUKTrackable trackable)
    {
        TryStartFrom(trackable);
    }

    private void OnTrackableRemoved(MRUKTrackable trackable)
    {
        // 二维码从视野里消失不打断演出 —— 玩家低头看手柄就中断的话体验太差。
        // 只是不再跟随(锚点已经失效了)。
        if (trackable == currentCode)
        {
            currentCode = null;
        }
    }

    /// <summary>本组件可能比二维码晚出现(比如切进场景时码已经在视野里了),
    /// 那样 TrackableAdded 早就发过了,所以空闲时也主动查一遍已知的 trackable。</summary>
    private void PollForAlreadyDetectedCodes()
    {
        MRUK mruk = MRUK.Instance;
        if (mruk == null)
        {
            return;
        }

        mruk.GetTrackables(scratch);
        for (int i = 0; i < scratch.Count; i++)
        {
            if (TryStartFrom(scratch[i]))
            {
                return;
            }
        }
    }

    private bool TryStartFrom(MRUKTrackable trackable)
    {
        if (phase != Phase.WaitingForCode || trackable == null || !trackable.IsTracked)
        {
            return false;
        }

        if (trackable.TrackableType != OVRAnchor.TrackableType.QRCode)
        {
            return false;
        }

        if (!TryParsePayload(trackable.MarkerPayloadString, out LijiangEchoPatternIntro.Pattern pattern))
        {
            // 别人的码 / 我们不认识的码:忽略,不要打断,也不要刷屏
            return false;
        }

        currentCode = trackable;
        BeginAt(trackable.transform, MeasureCodeSize(trackable), pattern);
        return true;
    }

    /// <summary>二维码内容 → 纹样。只认 "lijiang:xxx",别人的二维码一律不理。</summary>
    public static bool TryParsePayload(string payload, out LijiangEchoPatternIntro.Pattern pattern)
    {
        pattern = LijiangEchoPatternIntro.Pattern.Fish;
        if (string.IsNullOrEmpty(payload))
        {
            return false;
        }

        string text = payload.Trim();
        if (!text.StartsWith(PayloadPrefix, System.StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        switch (text.Substring(PayloadPrefix.Length).Trim().ToLowerInvariant())
        {
            case "fish": pattern = LijiangEchoPatternIntro.Pattern.Fish; return true;
            case "snake": pattern = LijiangEchoPatternIntro.Pattern.Snake; return true;
            case "frog": pattern = LijiangEchoPatternIntro.Pattern.Frog; return true;
            case "bird": pattern = LijiangEchoPatternIntro.Pattern.Bird; return true;
            default: return false;
        }
    }

    /// <summary>二维码印多大,系统是知道的(PlaneRect)。用它来定演出的尺度,
    /// 这样打 A4 上的小码和打海报上的大码,观感是一致的。</summary>
    private static float MeasureCodeSize(MRUKTrackable trackable)
    {
        if (trackable != null && trackable.PlaneRect.HasValue)
        {
            Rect rect = trackable.PlaneRect.Value;
            float side = Mathf.Max(rect.width, rect.height);
            if (side > 0.001f)
            {
                return side;
            }
        }

        return 0.1f;   // 拿不到就按 10cm 算(我们自己打的那批就是这个量级)
    }

    // ————————————————————————————— ② 入场动画 —————————————————————————————

    /// <summary>把演出钉到二维码那儿并起播。codeSize 是二维码的实际边长(米)。</summary>
    private void BeginAt(Transform codeTransform, float codeSize, LijiangEchoPatternIntro.Pattern pattern)
    {
        if (anchorRoot != null)
        {
            Destroy(anchorRoot.gameObject);
        }

        GameObject holder = new GameObject("漓江回声_二维码锚点");
        anchorRoot = holder.transform;

        // ⚠️ 二维码的 forward 是【从纸面指向观众】的,直接拿它当舞台朝向,舞台的 +X 就
        // 落在玩家的左手边 —— 结果文字左右翻转、"从左飞来"也跑到右边去。绕 Y 转 180°
        // 让舞台 +X 对上玩家的右手边(和 Unity 默认相机下 identity 可读是一回事);
        // 这样一来局部 +Z 是扎进纸里的,所以往观众方向抬要走 -Z。
        Quaternion faceViewer = Quaternion.Euler(0f, 180f, 0f);

        if (followCode && codeTransform != null)
        {
            // 挂成子物体 = 系统更新锚点位姿时,演出自动跟着走
            anchorRoot.SetParent(codeTransform, false);
            anchorRoot.localPosition = new Vector3(0f, 0f, liftOffPaper);
            anchorRoot.localRotation = faceViewer;
        }
        else if (codeTransform != null)
        {
            anchorRoot.SetPositionAndRotation(
                codeTransform.position + codeTransform.forward * liftOffPaper,
                codeTransform.rotation * faceViewer);
        }

        // 动画内部是按"1 米见方左右"的舞台写的,这里按二维码实际大小缩放到现场尺度
        float scale = Mathf.Max(0.01f, codeSize * sizeRelativeToCode);
        anchorRoot.localScale = Vector3.one * scale;

        phase = Phase.Intro;
        SetStatus(PatternName(pattern));

        // 光圈大小统一从这里下发,盖掉组件自己序列化的值
        intro.RingSize = sharedRingSize;
        intro.Begin(pattern, anchorRoot, () => OnIntroFinished(pattern));
        Debug.Log($"[漓江回声] 扫到 {PatternName(pattern)},二维码边长 {codeSize:F3} m,演出缩放 {scale:F3}。");
        LogStageDiagnostics();
        selfCheckAt = Time.time + 1f;
    }

    // ————————————————————————————— ③ 打击 —————————————————————————————

    private LijiangEchoPatternIntro.Pattern strikePattern;
    private float strikeStartedAt;
    private LijiangEchoPatternStrike strike;

    /// <summary>入场动画演完 → 接真打击。四种纹样各按自己的打法判定,
    /// 具体实现见 LijiangEchoPatternStrike。</summary>
    private void OnIntroFinished(LijiangEchoPatternIntro.Pattern pattern)
    {
        strikePattern = pattern;
        strikeStartedAt = Time.time;
        phase = Phase.Strike;

        // 打击自己会在光圈上方写判定、下方写纹样名,头顶这行就清空 ——
        // 三处都写字只会互相挡,视野里越干净越好。
        SetStatus(string.Empty);

        if (intro != null)
        {
            intro.Teardown();   // 入场的生物收掉,把画面让给打击
        }

        if (strike == null)
        {
            strike = GetComponent<LijiangEchoPatternStrike>();
            if (strike == null)
            {
                strike = gameObject.AddComponent<LijiangEchoPatternStrike>();
            }
        }

        strike.RingSize = sharedRingSize;   // 和入场同一个值,两段光圈才一样大
        strike.Begin(pattern, anchorRoot, OnStrikeFinished);
    }

    private void OnStrikeFinished(int hits, int total)
    {
        SetStatus($"{PatternName(strikePattern)}\n命中 {hits} / {total}");
        Debug.Log($"[漓江回声] {PatternName(strikePattern)} 打击结束:命中 {hits} / {total}");
        FinishRound();
    }

    private void UpdateStrike()
    {
        // 打击本身由 LijiangEchoPatternStrike 自己跑;这里只兜一个总时限,
        // 免得玩家走开之后这一轮永远挂着、下一张码也扫不了。
        if (Time.time - strikeStartedAt > 30f)
        {
            OnStrikeFinished(0, 1);
        }
    }

    private void FinishRound()
    {
        phase = Phase.Finished;
        finishedAt = Time.time;

        if (intro != null)
        {
            intro.Teardown();
        }

        if (strike != null)
        {
            strike.Teardown();
        }

        if (anchorRoot != null)
        {
            Destroy(anchorRoot.gameObject);
            anchorRoot = null;
        }

        if (simulatedCode != null)
        {
            Destroy(simulatedCode);
            simulatedCode = null;
        }

        currentCode = null;
    }

    private static string StrikeHint(LijiangEchoPatternIntro.Pattern pattern)
    {
        switch (pattern)
        {
            case LijiangEchoPatternIntro.Pattern.Fish: return "单击 · 左右手分边";
            case LijiangEchoPatternIntro.Pattern.Snake: return "按住不放";
            case LijiangEchoPatternIntro.Pattern.Frog: return "滑动手柄";
            default: return "双击 · 两只手同时";
        }
    }

    private static string StrikeSfx(LijiangEchoPatternIntro.Pattern pattern)
    {
        switch (pattern)
        {
            case LijiangEchoPatternIntro.Pattern.Snake: return "snake";
            case LijiangEchoPatternIntro.Pattern.Frog: return "swipe";
            case LijiangEchoPatternIntro.Pattern.Bird: return "birds";
            default: return "water";
        }
    }

    public static string PatternName(LijiangEchoPatternIntro.Pattern pattern)
    {
        switch (pattern)
        {
            case LijiangEchoPatternIntro.Pattern.Fish: return "鱼纹";
            case LijiangEchoPatternIntro.Pattern.Snake: return "蛇纹";
            case LijiangEchoPatternIntro.Pattern.Frog: return "蛙纹";
            default: return "鸟纹";
        }
    }

    // ————————————————————————————— 电脑上跑测 —————————————————————————————

    /// <summary>没有头显时,按 1/2/3/4 当作扫到了对应的码,演出就摆在相机正前方。
    /// 这样在 Scanplay 场景里 Play 一下就能看整条链路,不用每次都戴头显。</summary>
    private void PollKeyboardSimulation()
    {
        if (!simulateWithKeyboard)
        {
            return;
        }

        UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
        if (keyboard == null)
        {
            return;
        }

        LijiangEchoPatternIntro.Pattern pattern;
        if (keyboard.digit1Key.wasPressedThisFrame) { pattern = LijiangEchoPatternIntro.Pattern.Fish; }
        else if (keyboard.digit2Key.wasPressedThisFrame) { pattern = LijiangEchoPatternIntro.Pattern.Snake; }
        else if (keyboard.digit3Key.wasPressedThisFrame) { pattern = LijiangEchoPatternIntro.Pattern.Frog; }
        else if (keyboard.digit4Key.wasPressedThisFrame) { pattern = LijiangEchoPatternIntro.Pattern.Bird; }
        else { return; }

        SimulateScan(pattern);
    }

    private int manualIndex;
    private bool previousManualHeld;

    /// <summary>头显里的保底触发:按手柄按键,直接演下一个纹样。
    ///
    /// 为什么要有这个:系统级二维码追踪在这台设备上一直配不起来(前置条件全部核实合格,
    /// 仍返回 XR_ERROR_RUNTIME_FAILURE)。那件事继续查,但不该因此让整套东西在头显里
    /// 完全没法看 —— 入场动画和打击都是好的,只是缺一个"从哪儿开始"的信号。
    ///
    /// 所以按一下手柄就当扫到了一张码,四个纹样轮着来。现场万一扫码不灵,
    /// 这条也能顶着把展演跑完。</summary>
    private void PollManualTrigger()
    {
        if (!allowManualTrigger)
        {
            return;
        }

        bool held = OVRInput.Get(OVRInput.Button.One) || OVRInput.Get(OVRInput.Button.Three)
            || OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch)
            || OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch);

        bool pressed = held && !previousManualHeld;
        previousManualHeld = held;

        if (!pressed)
        {
            return;
        }

        LijiangEchoPatternIntro.Pattern pattern =
            (LijiangEchoPatternIntro.Pattern)(manualIndex % 4);
        manualIndex++;

        Debug.Log($"[漓江回声] 手柄手动触发:{PatternName(pattern)}");
        SimulateScan(pattern);
    }

    /// <summary>假装扫到了一张码,摆在相机正前方 0.8 米。编辑器菜单也调这个。</summary>
    public void SimulateScan(LijiangEchoPatternIntro.Pattern pattern)
    {
        if (phase != Phase.WaitingForCode)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[漓江回声] 场景里没有可用的主相机(Camera.main 为空),模拟扫码没法定位。");
            return;
        }

        simulatedCode = new GameObject("漓江回声_模拟二维码");

        // 朝向要和真二维码一致:真码的 transform.forward 是【从纸面指向观众】的,
        // 舞台的 +Z 也就朝着人。所以这里用 -cam.forward,不是 cam.forward ——
        // 写成 cam.forward 的话舞台整个背对着你,贴图是反的,liftOffPaper 还会把它往里推。
        simulatedCode.transform.position = cam.transform.position + cam.transform.forward * simulateDistance;
        simulatedCode.transform.rotation = Quaternion.LookRotation(-cam.transform.forward, Vector3.up);

        // 模拟的码按 10cm 算,和我们打印的那批一致
        BeginAt(simulatedCode.transform, 0.1f, pattern);
    }

    // 自检:开演一秒后跑一次(那时生物已经淡入了,零时刻本来就是全透明,查不出东西)
    private float selfCheckAt = -1f;

    /// <summary>逐个图层自检并给出结论。
    ///
    /// 「扫到了但什么都看不见」有太多可能:没生成、摆到视野外、全透明、被相机剔除、
    /// 图层被禁用……光靠猜要试很多轮。这里一次把每个图层的位置/透明度/层/排序/
    /// 在不在视锥里全打出来,并直接说结论。</summary>
    private void RunSelfCheck()
    {
        if (anchorRoot == null)
        {
            Debug.LogError("[漓江回声] 自检:舞台根已经没了,动画被提前收掉了。");
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogError("[漓江回声] 自检:没有主相机。");
            return;
        }

        SpriteRenderer[] sprites = anchorRoot.GetComponentsInChildren<SpriteRenderer>(true);
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine($"[漓江回声] 图层自检(共 {sprites.Length} 个)");
        sb.AppendLine($"相机 {cam.name}:cullingMask={cam.cullingMask},near={cam.nearClipPlane:F2},far={cam.farClipPlane:F0}");

        Plane[] frustum = GeometryUtility.CalculateFrustumPlanes(cam);

        int invisibleAlpha = 0;
        int outsideView = 0;
        int culledLayer = 0;
        int disabled = 0;
        int fine = 0;

        foreach (SpriteRenderer sr in sprites)
        {
            if (sr == null)
            {
                continue;
            }

            bool layerOk = (cam.cullingMask & (1 << sr.gameObject.layer)) != 0;
            bool onOk = sr.enabled && sr.gameObject.activeInHierarchy;
            bool alphaOk = sr.color.a > 0.02f;
            bool inView = GeometryUtility.TestPlanesAABB(frustum, sr.bounds);

            if (!layerOk) { culledLayer++; }
            else if (!onOk) { disabled++; }
            else if (!alphaOk) { invisibleAlpha++; }
            else if (!inView) { outsideView++; }
            else { fine++; }

            sb.AppendLine(
                $"  {sr.name,-22} α={sr.color.a:F2} 层={LayerMask.LayerToName(sr.gameObject.layer)} "
                + $"序={sr.sortingOrder} 开={onOk} 视锥内={inView} "
                + $"世界位置={sr.bounds.center} 尺寸={sr.bounds.size}");
        }

        sb.AppendLine($"小结:正常 {fine},全透明 {invisibleAlpha},在视野外 {outsideView},"
            + $"被相机层剔除 {culledLayer},被禁用 {disabled}");

        if (fine > 0)
        {
            sb.AppendLine("→ 有图层是可见的。看不到的话检查 Game 视图用的是不是这台相机、"
                + "以及有没有别的东西挡在前面。");
            Debug.Log(sb.ToString());
            return;
        }

        if (outsideView > 0)
        {
            sb.AppendLine("→ 全部落在视野外:位置/缩放算错了。对照上面每个图层的世界位置排查。");
        }
        else if (invisibleAlpha > 0)
        {
            sb.AppendLine("→ 全部是全透明:透明度没被写上去(SetAlpha 没找到渲染器,"
                + "或者被 LijiangEchoSpriteLayer 重刷回 0)。");
        }
        else if (culledLayer > 0)
        {
            sb.AppendLine("→ 全部被相机的 cullingMask 剔除了:物件的 Layer 不在相机渲染范围内。");
        }

        Debug.LogError(sb.ToString());
    }

    /// <summary>把这一轮实际生成了什么、摆在哪儿打出来。
    /// 「扫到了但什么都看不见」这种问题,光看现象没法判断是没生成、摆错地方、还是全透明。</summary>
    private void LogStageDiagnostics()
    {
        if (anchorRoot == null)
        {
            Debug.LogWarning("[漓江回声] 舞台根没建起来。");
            return;
        }

        Camera cam = Camera.main;
        int sprites = anchorRoot.GetComponentsInChildren<SpriteRenderer>(true).Length;

        string where = cam != null
            ? $"距相机 {Vector3.Distance(cam.transform.position, anchorRoot.position):F2} m,"
              + $"在相机{(Vector3.Dot(cam.transform.forward, anchorRoot.position - cam.transform.position) > 0f ? "前方" : "【后方】")}"
            : "没有主相机";

        Debug.Log($"[漓江回声] 舞台诊断:锚点世界坐标 {anchorRoot.position},缩放 {anchorRoot.lossyScale.x:F3},"
            + $"图层数 {sprites},{where}。"
            + $"\n相机:{(cam != null ? cam.name : "无")},"
            + $"near {(cam != null ? cam.nearClipPlane.ToString("F3") : "-")},"
            + $"清屏 {(cam != null ? cam.clearFlags.ToString() : "-")}");
    }

    // ————————————————————————————— 提示文字 —————————————————————————————

    private void BuildStatusText()
    {
        if (!showStatusText)
        {
            return;
        }

        GameObject holder = new GameObject("漓江回声_扫码提示");
        statusRoot = holder.transform;
        statusRoot.SetParent(transform, false);

        statusText = LijiangEchoStageKit.AddText(
            statusRoot, statusSpawned, "", Vector3.zero, statusTextSize, Color.white, 60);
    }

    private void SetStatus(string text)
    {
        if (lastStatus == text)
        {
            return;
        }

        lastStatus = text;
        if (statusText != null)
        {
            statusText.text = text;
        }
    }

    /// <summary>提示挂在玩家面前、始终朝向玩家 —— 它是给人看的,不该跟着二维码歪。</summary>
    private void FaceStatusTextToPlayer()
    {
        if (statusRoot == null)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        Transform head = cam.transform;
        statusRoot.position = head.position
            + head.forward * statusTextDistance
            + head.up * -statusTextDrop;
        statusRoot.rotation = Quaternion.LookRotation(statusRoot.position - head.position, Vector3.up);
    }
}
