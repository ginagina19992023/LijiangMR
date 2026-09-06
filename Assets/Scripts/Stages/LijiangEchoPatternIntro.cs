using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 扫码功能的【入场动画】(见 docs/TODO-QR-PATTERN-SCAN.md 第②段)。
///
/// 整体三段:① 扫到码、光圈浮现 → ②【本组件】纹样的生物出场、活动 3~5 秒 → ③ 进打击环节。
/// 本组件只负责②,演完走回调,不决定下一步去哪 —— 和 TraceStageController 一样是可重入模块。
///
/// 【每种生物按自己的习性演,不是同一个模板改参数】
///   鸟:几只鸟扇翅膀飞出,围光圈盘旋,忽远忽近
///   鱼:从光圈外跃起跳进圈里;另有鱼在圈旁探头,头旁泛起涟漪(更小的白圈),停一会儿也跃入
///   蛇:先响「嘶嘶」→ 扭动身子从旁边过来 → 缠上光圈 → 顺时针转一圈 → 消失
///   蛙:待设计(暂用简单蹦跳占位)
///
/// 不依赖摄像头/二维码,可以单独拉起来跑测;接上扫码后把 anchor 传成二维码的空间位置即可。
/// </summary>
[ExecuteAlways]
public class LijiangEchoPatternIntro : MonoBehaviour
{
    /// <summary>纹样序号,与 LijiangEchoGameController 的音符类型一一对应。</summary>
    public enum Pattern
    {
        Fish = 0,   // 单击
        Snake = 1,  // 长按
        Frog = 2,   // 挥划
        Bird = 3    // 双击
    }

    // ——— 各纹样用的贴图。都是工程里现成的,没有新增美术 ———
    // ⚠️ 鱼目前只有「纹样图」(select/fish_symbol),没有单独一条鱼的素材,
    //    先拿它顶着;真机看着不像"一条鱼在跳"的话,需要找美术要一张单体鱼。
    private const string RingArt = "battle/hit_ring_center";
    private const string FishArt = "select/fish_symbol";
    private const string SnakeArt = "transition/snake";
    private const string FrogArt = "select/frog_symbol";
    private const string BirdArtBig = "start/bird_big";
    private const string BirdArtSmall = "start/bird_small";

    [Header("整体")]
    [Tooltip("入场动画时长(秒)。需求说 3~5 秒。")]
    [SerializeField] private float duration = 4f;

    [Tooltip("中心光圈的大小。")]
    [SerializeField] private float ringSize = 0.62f;

    [Header("生物大小(反馈:原来太小,统一放大约 4~5 倍)")]
    [SerializeField] private float birdSizeBig = 1.35f;      // 原 0.30
    [SerializeField] private float birdSizeSmall = 1.00f;    // 原 0.22
    [SerializeField] private float fishSize = 1.20f;         // 原 0.26
    [SerializeField] private float fishPeekSize = 1.00f;     // 原 0.22
    [SerializeField] private float snakeHeadSize = 1.35f;    // 原 0.30,整条蛇的大小
    [SerializeField] private float frogSize = 1.25f;         // 原 0.28

    [Header("鸟:盘旋")]
    [SerializeField] private int birdCount = 4;
    [SerializeField] private float birdOrbitRadius = 0.85f;
    [SerializeField] private float birdOrbitSpeed = 1.1f;      // 圈/秒 的角速度系数
    [SerializeField] private float birdDepthSwing = 0.45f;     // 忽远忽近的幅度

    [Header("鱼:跃入与探头")]
    [SerializeField] private int fishLeapCount = 3;            // 从外面跳进圈里的
    [SerializeField] private int fishPeekCount = 2;            // 在旁边探头的
    [SerializeField] private float fishLeapHeight = 0.55f;     // 跃起弧线的高度

    [Tooltip("跃起时朝【相机方向】凸出多少米。0 = 老的平面弧线;越大越有\"跳到你面前再落回圈里\"的感觉。\n"
        + "起跳点和落点都在光圈那个平面上,凸起只发生在弧线中段。")]
    [SerializeField] private float fishLeapDepth = 0.45f;

    [Tooltip("鱼凑到最近时放大到几倍。纯靠位置变化不够明显,配合放大才看得出是冲着你来的。")]
    [SerializeField] private float fishNearScaleBoost = 1.35f;

    // 下面这些点原来是写死在代码里的,所以轨迹只能改代码。现在搬到 Inspector,
    // 而且在 Scene 视图里是可以直接拖的箭头(见 LijiangEchoPatternIntroEditor.OnSceneGUI)。
    // 数组长度和上面的数量对不上时会自动按默认值重算,所以改数量不用手动补。
    [Tooltip("每条鱼从哪里起跳。Scene 视图里可以直接拖。")]
    [SerializeField] private Vector3[] fishLeapStarts;

    [Tooltip("每条探头的鱼冒头的位置。Scene 视图里可以直接拖。")]
    [SerializeField] private Vector3[] fishPeekSpots;

    [Header("蛙:轨迹控制点(Scene 视图里可拖)")]
    [SerializeField] private Vector3 frogPadLeftPos = new Vector3(-0.95f, -0.22f, 0.10f);   // 小荷叶
    [SerializeField] private Vector3 frogOnPadLeft = new Vector3(-0.95f, -0.05f, 0.10f);    // 蹲在小荷叶上
    [SerializeField] private Vector3 frogPadNextPos = new Vector3(0.98f, -0.05f, -0.06f);   // 下一片荷叶
    [SerializeField] private Vector3 frogEnterFrom = new Vector3(-1.20f, -1.17f, 0.10f);    // 从画面外哪里进来
    [SerializeField] private Vector3 frogExitTo = new Vector3(2.20f, 0.55f, -0.35f);        // 往画面外哪里跳走

    [Header("蛇:轨迹控制点(Scene 视图里可拖)")]
    [Tooltip("蛇从哪儿开始游过来。终点固定在光圈底部 —— 那里的顺时针切线正好是朝左,接得上。")]
    [SerializeField] private Vector3 snakeApproachFrom = new Vector3(1.55f, -0.88f, 0.10f);

    [Header("蛇:缠绕")]
    [SerializeField] private float snakeApproachRatio = 0.35f; // 前 35% 时间用来"游过来"
    [SerializeField] private float snakeCoilRadius = 0.72f;

    [Tooltip("蛇身切成几节。整条蛇是一张图,这里把它横向切开当关节 —— 一条僵硬的整图绕圈是不行的。")]
    [Range(3, 24)] [SerializeField] private int snakeSegments = 10;

    [Tooltip("在原图(3207×630 的坐标系)里,蛇占哪一块。切片就在这个框里横向均分。\n"
        + "框没对准的话蛇会缺头少尾,直接在这里改数值。")]
    [SerializeField] private RectInt snakeSourceRect = new RectInt(1700, 190, 420, 300);

    [Tooltip("每一节比前一节沿路径落后多少(弧度)。越大蛇越长、关节越散。")]
    [SerializeField] private float snakeSegmentSpacing = 0.26f;

    [Tooltip("缠绕:关节绕着光圈这根「管子」转,这是管子半径(米)。0 = 平贴着圈走,完全不缠。")]
    [SerializeField] private float snakeWindRadius = 0.12f;

    [Tooltip("绕光圈一整圈的过程中,身子绕管子转几圈。转的时候一半在圈前一半在圈后。")]
    [SerializeField] private float snakeWindTurns = 2.5f;

    [Tooltip("扭动的幅度(米)与快慢。游过来的那一段主要靠它看出是活的。")]
    [SerializeField] private float snakeWaveAmplitude = 0.07f;
    [SerializeField] private float snakeWaveSpeed = 5f;

    // 转头的代码假设"贴图本身画的是朝右(+X)",但花山纹样这几张贴图各朝各的,
    // 所以留出每种生物的角度修正,方向不对直接在 Inspector 里拖,不用改代码。
    [Header("贴图朝向校正 —— 方向不对就调这里(单位:度)")]
    [Tooltip("贴图原本朝哪边:0=朝右,90=朝上,180=朝左,-90=朝下。可以填任意角度微调。\n"
        + "这四张纹样贴图画的都是朝左,所以默认全是 180。")]
    [Range(-180f, 180f)] [SerializeField] private float fishFacingDegrees = 180f;
    [Range(-180f, 180f)] [SerializeField] private float snakeFacingDegrees = 180f;
    [Range(-180f, 180f)] [SerializeField] private float frogFacingDegrees = 180f;
    [Range(-180f, 180f)] [SerializeField] private float birdFacingDegrees = 180f;

    [Header("往左走时是否左右翻面(让肚子始终朝下)")]
    [Tooltip("侧视的贴图(鱼、鸟)勾上;正面/俯视看不出左右的贴图(蛙)取消,免得来回闪。")]
    [SerializeField] private bool fishMirrorWhenLeft = true;
    [SerializeField] private bool frogMirrorWhenLeft = true;
    [SerializeField] private bool birdMirrorWhenLeft = true;

    // 这套动画是有纵深的(鸟忽远忽近、蛇一半绕到圈后面),Game 视图正对着看根本看不出来,
    // Play 起来又只有 4 秒。所以做成【不用 Play 也能摆】:勾上预览,拖时间轴,
    // 在 Scene 视图里转着看,深度一目了然。
    [Header("编辑模式预览(不用 Play 就能调轨迹)")]
    [Tooltip("勾上后直接在 Scene 视图里生成这套动画。预览物件带 DontSave,不会存进场景。")]
    [SerializeField] private bool previewInEditor;

    [Tooltip("预览哪一种纹样。")]
    [SerializeField] private Pattern previewPattern = Pattern.Fish;

    [Tooltip("时间轴:0=刚开始,1=结束。拖它就能逐帧看轨迹。")]
    [Range(0f, 1f)] [SerializeField] private float previewTime = 0.45f;

    [Tooltip("在编辑器里自动循环播放。想停下来逐帧看就取消,然后拖上面的时间轴。")]
    [SerializeField] private bool autoPlayInEditor = true;

    [Tooltip("在 Scene 视图里把每个生物的整条轨迹画成线,深度看得最清楚。")]
    [SerializeField] private bool drawPathGizmos = true;

    [Tooltip("轨迹线的采样点数,越多越平滑。")]
    [Range(8, 240)] [SerializeField] private int pathGizmoSamples = 90;

    // ——— 运行时 ———
    private Transform root;
    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<Actor> actors = new List<Actor>();
    private Transform ring;
    private Pattern pattern;
    private float timer;
    private bool running;
    private Action onComplete;

    /// <summary>一个出场的小东西(一只鸟/一条鱼/蛇身的一节)。</summary>
    private sealed class Actor
    {
        public Transform Tr;
        public SpriteRenderer Sr;
        public float Phase;        // 各自错开,免得整齐划一像队列
        public float BaseScale;
        public Vector3 From;       // 起点(鱼跃入用)
        public Vector3 To;         // 终点
        public float StartAt;      // 归一化时间 0~1,到点才开始动
        public float Span;         // 持续多久(归一化)
        public bool Peeking;       // 鱼:是探头的那种吗
        public Transform Ripple;   // 鱼探头时旁边的小涟漪圈
        public int Index;          // 第几条鱼 / 第几节蛇,用来回查可拖动的控制点
    }

    /// <summary>轨迹控制点数组的长度要和数量对得上。改了数量就按默认值重铺一遍,
    /// 这样在 Inspector 里改 fishLeapCount 不用自己去补数组。</summary>
    private void EnsureFishPoints()
    {
        int leaps = Mathf.Max(0, fishLeapCount);
        if (fishLeapStarts == null || fishLeapStarts.Length != leaps)
        {
            fishLeapStarts = new Vector3[leaps];
            for (int i = 0; i < leaps; i++)
            {
                fishLeapStarts[i] = DefaultFishLeapStart(i);
            }
        }

        int peeks = Mathf.Max(0, fishPeekCount);
        if (fishPeekSpots == null || fishPeekSpots.Length != peeks)
        {
            fishPeekSpots = new Vector3[peeks];
            for (int i = 0; i < peeks; i++)
            {
                fishPeekSpots[i] = DefaultFishPeekSpot(i);
            }
        }
    }

    private static Vector3 DefaultFishLeapStart(int i)
    {
        float side = i % 2 == 0 ? -1f : 1f;
        float spread = 0.75f + i * 0.18f;
        return new Vector3(side * spread, -0.45f, 0f);   // 圈外、偏下,和光圈同一个平面
    }

    private static Vector3 DefaultFishPeekSpot(int i)
    {
        float side = i % 2 == 0 ? 1f : -1f;
        return new Vector3(side * 0.62f, -0.10f - i * 0.12f, 0.08f);
    }

    /// <summary>把轨迹控制点恢复成默认布局(Inspector 上有个按钮调它)。</summary>
    public void ResetTrajectoryPoints()
    {
        fishLeapStarts = null;
        fishPeekSpots = null;
        EnsureFishPoints();

        frogPadLeftPos = new Vector3(-0.95f, -0.22f, 0.10f);
        frogOnPadLeft = new Vector3(-0.95f, -0.05f, 0.10f);
        frogPadNextPos = new Vector3(0.98f, -0.05f, -0.06f);
        frogEnterFrom = new Vector3(-1.20f, -1.17f, 0.10f);
        frogExitTo = new Vector3(2.20f, 0.55f, -0.35f);

        snakeApproachFrom = new Vector3(1.55f, -0.88f, 0.10f);
        previewDirty = true;
    }

    // ————————————————————————————— 对外接口 —————————————————————————————

    /// <summary>拉起一次入场动画。
    /// anchor 为空时在玩家面前起一个舞台(方便无二维码时跑测);
    /// 接上扫码后传二维码的空间位置,动画就钉在那儿。</summary>
    public void Begin(Pattern which, Transform anchor, Action onFinished)
    {
        Teardown();

        pattern = which;
        onComplete = onFinished;
        timer = 0f;

        if (anchor != null)
        {
            GameObject holder = new GameObject("漓江回声_扫码入场");
            holder.transform.SetParent(anchor, false);
            holder.transform.localPosition = Vector3.zero;
            holder.transform.localRotation = Quaternion.identity;
            root = holder.transform;
            spawned.Add(holder);
        }
        else
        {
            root = LijiangEchoStageKit.PrepareStageRoot("漓江回声_扫码入场");
        }

        BuildRing();
        BuildForPattern();

        running = true;
    }

    private void BuildForPattern()
    {
        switch (pattern)
        {
            case Pattern.Bird: BuildBird(); break;
            case Pattern.Fish: BuildFish(); break;
            case Pattern.Snake: BuildSnake(); break;
            default: BuildFrog(); break;
        }
    }

    /// <summary>音效:编辑模式预览和画 Gizmo 采样时都不该出声。</summary>
    private void PlayIntroSfx(string clipName, float volume)
    {
        if (!Application.isPlaying || sampling)
        {
            return;
        }

        LijiangEchoStageKit.PlaySfx(clipName, volume);
    }

    /// <summary>收起。Begin 会先自动调一次,所以调用方只在中途取消时才需要显式调。</summary>
    public void Teardown()
    {
        running = false;
        onComplete = null;
        actors.Clear();
        ring = null;
        frogPadLeft = null;
        frogPadNext = null;
        frog = null;
        snakeJoints.Clear();
        lastFrogCallAt = -1f;
        spawned.Clear();

        if (root != null)
        {
            DestroyNow(root.gameObject);
            root = null;
        }
    }

    /// <summary>编辑模式下不能用 Destroy(它要等到帧末,而编辑器没有"帧末"),得用 DestroyImmediate。</summary>
    private static void DestroyNow(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            EditorPreviewTick();
            return;
        }

        if (!running || root == null)
        {
            return;
        }

        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / Mathf.Max(0.01f, duration));

        ApplyPose(t);

        if (timer >= duration)
        {
            Action callback = onComplete;
            running = false;
            callback?.Invoke();
        }
    }

    /// <summary>把动画摆到归一化时间 t 上。抽出来是为了让编辑模式的时间轴也能复用。</summary>
    private void ApplyPose(float t)
    {
        timer = t * Mathf.Max(0.01f, duration);

        UpdateRing(t);
        switch (pattern)
        {
            case Pattern.Bird: UpdateBird(t); break;
            case Pattern.Fish: UpdateFish(t); break;
            case Pattern.Snake: UpdateSnake(t); break;
            default: UpdateFrog(t); break;
        }
    }

    // ————————————————————————————— 编辑模式预览 —————————————————————————————

    private bool sampling;        // 正在为画轨迹而反复摆姿势,期间别出声
    private bool previewDirty;
    private float lastPreviewTime;

    // 编辑模式下 Update 不是自动每帧跑的,要有人推。推的人是
    // LijiangEchoPatternIntroPreviewDriver,它靠这张表知道"还有没有预览开着"。
    // 之前只在 Inspector 重绘时推,所以关掉预览再打开就卡在 previewTime=0 那一帧,
    // 而那一帧本来就是全透明的 —— 看着就像"再也打不开了"。
    private static readonly List<LijiangEchoPatternIntro> LiveInstances = new List<LijiangEchoPatternIntro>();

    public bool EditorPreviewActive => previewInEditor;

    public static bool AnyEditorPreviewActive()
    {
        for (int i = LiveInstances.Count - 1; i >= 0; i--)
        {
            if (LiveInstances[i] == null)
            {
                LiveInstances.RemoveAt(i);
                continue;
            }

            if (LiveInstances[i].previewInEditor)
            {
                return true;
            }
        }

        return false;
    }

    private void OnEnable()
    {
        if (!LiveInstances.Contains(this))
        {
            LiveInstances.Add(this);
        }
    }

    private void OnDisable()
    {
        LiveInstances.Remove(this);
    }

    /// <summary>Inspector 上的按钮用它重新打开预览,顺便把该重置的都重置掉,
    /// 免得沿用上一次的残留状态。</summary>
    public void OpenEditorPreview(Pattern which)
    {
        previewInEditor = true;
        previewPattern = which;
        previewTime = 0f;
        lastPreviewTime = 0f;
        autoPlayInEditor = true;
        lastEditorTime = 0f;
        previewDirty = true;
    }

    public void CloseEditorPreview()
    {
        previewInEditor = false;
        lastEditorTime = 0f;
        Teardown();
    }

    private void OnValidate()
    {
        // 拖时间轴不该重建预览 —— 那会把物件全删了重来,拖动时一路闪。
        // 只有改了大小/数量这类结构参数才重建。
        if (!Mathf.Approximately(previewTime, lastPreviewTime))
        {
            lastPreviewTime = previewTime;
            return;
        }

        previewDirty = true;
    }

    /// <summary>编辑模式的每一拍:按需重建预览,然后把动画摆到 previewTime 上。</summary>
    private void EditorPreviewTick()
    {
        if (!previewInEditor)
        {
            if (root != null)
            {
                Teardown();
            }

            return;
        }

        if (root == null || pattern != previewPattern || previewDirty)
        {
            previewDirty = false;
            BuildPreview();
        }

        if (root == null)
        {
            return;
        }

        if (autoPlayInEditor)
        {
            // 编辑模式没有 Time.deltaTime 可用(它在编辑器里不推进),自己按真实时间算
            float now = Time.realtimeSinceStartup;
            float step = lastEditorTime > 0f ? Mathf.Clamp(now - lastEditorTime, 0f, 0.1f) : 0f;
            lastEditorTime = now;

            previewTime = Mathf.Repeat(previewTime + step / Mathf.Max(0.01f, duration), 1f);
            lastPreviewTime = previewTime;   // 别让 OnValidate 误判成"改了结构参数"
        }
        else
        {
            lastEditorTime = 0f;
        }

        ApplyPose(Mathf.Clamp01(previewTime));
    }

    private float lastEditorTime;

    private void BuildPreview()
    {
        Teardown();

        pattern = previewPattern;
        onComplete = null;
        timer = 0f;
        lastEditorTime = 0f;   // 重建后第一拍的步长按 0 算,不要把停掉的那段时间一次补上

        // 预览挂在本物体下面,想整体挪位置直接拖这个 GameObject 就行
        GameObject holder = new GameObject("漓江回声_扫码入场_编辑预览");
        holder.transform.SetParent(transform, false);
        root = holder.transform;
        spawned.Add(holder);

        BuildRing();
        BuildForPattern();

        // 预览出来的东西只是给眼睛看的,标上 DontSave,存场景时不会被写进去
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null)
            {
                spawned[i].hideFlags = HideFlags.DontSave;
            }
        }

        running = false;
    }

    /// <summary>把每个生物的整条轨迹画出来。
    ///
    /// 不另写一份路径公式 —— 那样迟早和动画本身对不上。这里直接把动画在 0~1 上采样若干次,
    /// 记下每一帧的位置连成线,画完再摆回原来的时间点。所以线永远等于真实轨迹。</summary>
    private void OnDrawGizmos()
    {
        if (!drawPathGizmos || root == null || sampling)
        {
            return;
        }

        List<Transform> movers = CollectMovers();
        if (movers.Count == 0)
        {
            return;
        }

        int samples = Mathf.Max(8, pathGizmoSamples);
        Vector3[,] track = new Vector3[movers.Count, samples];

        sampling = true;
        float restore = Mathf.Clamp01(Application.isPlaying ? timer / Mathf.Max(0.01f, duration) : previewTime);
        float restoreFrogCall = lastFrogCallAt;   // 采样会把蛙叫的"已播过"游标推到底,得还回去
        try
        {
            for (int s = 0; s < samples; s++)
            {
                ApplyPose(s / (float)(samples - 1));
                for (int m = 0; m < movers.Count; m++)
                {
                    track[m, s] = movers[m] != null ? movers[m].position : Vector3.zero;
                }
            }

            ApplyPose(restore);
        }
        finally
        {
            lastFrogCallAt = restoreFrogCall;
            sampling = false;
        }

        for (int m = 0; m < movers.Count; m++)
        {
            for (int s = 1; s < samples; s++)
            {
                // 按时间从青渐变到品红,一眼看出走向;深度靠在 Scene 视图里转视角看
                Gizmos.color = Color.Lerp(Color.cyan, Color.magenta, s / (float)(samples - 1));
                Gizmos.DrawLine(track[m, s - 1], track[m, s]);
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(track[m, 0], 0.03f);          // 起点
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(track[m, samples - 1], 0.03f); // 终点
        }
    }

    /// <summary>会动的那些 Transform(蛙和蛇不走 actors 列表,单独收一下)。</summary>
    private List<Transform> CollectMovers()
    {
        List<Transform> movers = new List<Transform>();

        for (int i = 0; i < actors.Count; i++)
        {
            if (actors[i] != null && actors[i].Tr != null)
            {
                movers.Add(actors[i].Tr);
            }
        }

        // 蛇的关节已经在 actors 里了,这里只补上不走 actors 列表的蛙
        if (frog != null)
        {
            movers.Add(frog);
        }

        return movers;
    }

    // ————————————————————————————— 光圈 —————————————————————————————

    private void BuildRing()
    {
        GameObject ringObject = LijiangEchoStageKit.AddIcon(
            root, spawned, RingArt, "扫码光圈", Vector3.zero, ringSize, 20, 0f);
        ring = ringObject != null ? ringObject.transform : null;
    }

    /// <summary>光圈:开头 0.4 秒淡入并微微放大,之后保持,呼吸感靠轻微脉动。</summary>
    private void UpdateRing(float t)
    {
        if (ring == null)
        {
            return;
        }

        float appear = Mathf.Clamp01(timer / 0.4f);
        float ease = Mathf.SmoothStep(0f, 1f, appear);
        SetAlpha(ring, 0.9f * ease);

        float breathe = 1f + Mathf.Sin(Time.time * 2.2f) * 0.03f;
        ring.localScale = Vector3.one * (Mathf.Lerp(0.7f, 1f, ease) * breathe);
    }

    // ————————————————————————————— 鸟:盘旋 —————————————————————————————

    private void BuildBird()
    {
        PlayIntroSfx("birds", 0.5f);

        for (int i = 0; i < birdCount; i++)
        {
            string art = i % 2 == 0 ? BirdArtBig : BirdArtSmall;
            float size = i % 2 == 0 ? birdSizeBig : birdSizeSmall;
            Transform bird = AddCreature(art, "入场鸟_" + i, size, 30 + i);

            actors.Add(new Actor
            {
                Tr = bird,
                Sr = bird.GetComponentInChildren<SpriteRenderer>(true),
                Phase = i * (Mathf.PI * 2f / Mathf.Max(1, birdCount)),
                BaseScale = 1f,
                StartAt = i * 0.06f,
                Span = 1f
            });
        }
    }

    /// <summary>鸟:绕光圈盘旋,深度忽远忽近(不是平面转圈),越远画得越小越淡。</summary>
    private void UpdateBird(float t)
    {
        for (int i = 0; i < actors.Count; i++)
        {
            Actor a = actors[i];
            if (a.Tr == null)
            {
                continue;
            }

            float local = Mathf.Clamp01((t - a.StartAt) / Mathf.Max(0.01f, 1f - a.StartAt));
            float angle = a.Phase + timer * birdOrbitSpeed * Mathf.PI * 2f * 0.35f;

            // 深度用另一个频率,和绕圈错开,才有"忽远忽近"而不是规律圆周
            float depth = Mathf.Sin(angle * 0.8f + a.Phase) * birdDepthSwing;

            // 从光圈中心飞出来:半径随出场进度张开
            float radius = birdOrbitRadius * Mathf.SmoothStep(0f, 1f, local);
            a.Tr.localPosition = new Vector3(
                Mathf.Cos(angle) * radius,
                Mathf.Sin(angle) * radius * 0.55f + 0.05f,   // 压扁一点,像俯视的盘旋
                depth);

            // 越远(depth 越大)越小越淡,制造纵深
            float near = Mathf.InverseLerp(birdDepthSwing, -birdDepthSwing, depth);
            float scale = a.BaseScale * Mathf.Lerp(0.7f, 1.15f, near);

            // 头朝盘旋的切线方向(圆的切线 = 半径转 90°),而不是只做左右翻面
            Vector3 tangent = new Vector3(-Mathf.Sin(angle), Mathf.Cos(angle) * 0.55f, 0f);
            FaceAlong(a.Tr, tangent, scale, birdFacingDegrees, birdMirrorWhenLeft);
            SetAlpha(a.Tr, Mathf.Lerp(0.55f, 1f, near) * FadeOutTail(t) * local);
        }
    }

    // ————————————————————————————— 鱼:跃入与探头 —————————————————————————————

    /// <summary>把 Inspector / Scene 手柄上的控制点同步到这条鱼身上。
    /// 涟漪也跟着挪,不然拖了探头点、涟漪还留在原地。</summary>
    private void RefreshFishPoint(Actor a)
    {
        if (a.Peeking)
        {
            if (fishPeekSpots != null && a.Index < fishPeekSpots.Length)
            {
                Vector3 spot = fishPeekSpots[a.Index];
                a.From = spot + new Vector3(0f, -0.18f, 0f);
            }
        }
        else if (fishLeapStarts != null && a.Index < fishLeapStarts.Length)
        {
            a.From = fishLeapStarts[a.Index];
        }
    }

    private void BuildFish()
    {
        PlayIntroSfx("water", 0.45f);
        EnsureFishPoints();

        // 从光圈外面跃起、跳进圈里的
        for (int i = 0; i < fishLeapStarts.Length; i++)
        {
            Transform fish = AddCreature(FishArt, "入场鱼_跃入_" + i, fishSize, 30 + i);

            actors.Add(new Actor
            {
                Tr = fish,
                Sr = fish.GetComponentInChildren<SpriteRenderer>(true),
                BaseScale = 1f,
                Index = i,
                From = fishLeapStarts[i],
                To = Vector3.zero,          // 跳进圈心
                StartAt = 0.12f + i * 0.18f,
                Span = 0.42f,
                Peeking = false
            });
        }

        // 在圈旁探头的:头旁边有一圈更小的白色涟漪
        for (int i = 0; i < fishPeekSpots.Length; i++)
        {
            Vector3 spot = fishPeekSpots[i];

            Transform fish = AddCreature(FishArt, "入场鱼_探头_" + i, fishPeekSize, 28 + i);
            fish.localPosition = spot;
            // 涟漪直接复用光圈贴图,缩小并降透明度,不用另做美术
            Transform ripple = AddCreature(RingArt, "入场鱼_涟漪_" + i, ringSize * 0.34f, 26 + i);
            ripple.localPosition = spot;

            actors.Add(new Actor
            {
                Tr = fish,
                Sr = fish.GetComponentInChildren<SpriteRenderer>(true),
                BaseScale = 1f,
                Index = i,
                From = spot + new Vector3(0f, -0.18f, 0f),   // 从水面下探上来
                To = Vector3.zero,                            // 最后也跃入圈心
                StartAt = 0.20f + i * 0.14f,
                Span = 0.30f,
                Peeking = true,
                Ripple = ripple
            });
        }
    }

    /// <summary>鱼:跃入的走抛物线跳进圈心;探头的先冒头停一会儿(旁边涟漪扩散),末段也跃入。</summary>
    private void UpdateFish(float t)
    {
        for (int i = 0; i < actors.Count; i++)
        {
            Actor a = actors[i];
            if (a.Tr == null)
            {
                continue;
            }

            // 每帧回读控制点,这样在 Scene 视图里拖手柄是立刻生效的,不用重建预览
            RefreshFishPoint(a);

            if (!a.Peeking)
            {
                float p = Mathf.Clamp01((t - a.StartAt) / a.Span);
                if (p <= 0f)
                {
                    SetAlpha(a.Tr, 0f);
                    continue;
                }

                // 抛物线:水平匀速,竖直先上后下,中段朝相机凸出来,最后落进原平面的圈心
                a.Tr.localPosition = LeapPoint(a.From, a.To, p, fishLeapHeight, fishLeapDepth);

                // 头朝抛物线的切线方向:起跳时朝上、落下时朝下,而不是一直平着
                // 大小 = 入水时缩小(像沉进去) × 离得近时放大(冲着你来)
                float s = a.BaseScale
                    * Mathf.Lerp(1f, 0.55f, p)
                    * Mathf.Lerp(1f, fishNearScaleBoost, NearBulge(p));
                FaceAlong(a.Tr, LeapTangent(a.From, a.To, p, fishLeapHeight), s, fishFacingDegrees, fishMirrorWhenLeft);
                SetAlpha(a.Tr, Mathf.Clamp01(1f - Mathf.Pow(p, 3f)));
                continue;
            }

            // —— 探头的 ——
            float peekIn = Mathf.Clamp01((t - a.StartAt) / a.Span);          // 冒头
            float leaveAt = 0.68f;                                           // 之后也跃入
            float leave = Mathf.Clamp01((t - leaveAt) / Mathf.Max(0.01f, 1f - leaveAt));

            if (leave <= 0f)
            {
                Vector3 spot = Vector3.Lerp(a.From, a.From + new Vector3(0f, 0.18f, 0f), Mathf.SmoothStep(0f, 1f, peekIn));
                spot.y += Mathf.Sin(Time.time * 2.6f + a.StartAt * 10f) * 0.012f;   // 水面轻晃
                a.Tr.localPosition = spot;
                a.Tr.localScale = Vector3.one * a.BaseScale;
                SetAlpha(a.Tr, peekIn);

                // 涟漪:随冒头扩散并变淡,循环
                if (a.Ripple != null)
                {
                    float loop = Mathf.Repeat(timer * 0.9f, 1f);
                    a.Ripple.localPosition = a.From + new Vector3(0f, 0.16f, 0.02f);
                    a.Ripple.localScale = Vector3.one * Mathf.Lerp(0.5f, 1.6f, loop);
                    SetAlpha(a.Ripple, (1f - loop) * 0.5f * peekIn);
                }
            }
            else
            {
                // 跃入圈心。探头的鱼和跃入的鱼走同一套弧线(反馈:要统一),只是幅度小一号
                Vector3 start = a.From + new Vector3(0f, 0.18f, 0f);
                float h = fishLeapHeight * 0.7f;
                a.Tr.localPosition = LeapPoint(start, Vector3.zero, leave, h, fishLeapDepth * 0.7f);

                float s = a.BaseScale
                    * Mathf.Lerp(1f, 0.5f, leave)
                    * Mathf.Lerp(1f, fishNearScaleBoost, NearBulge(leave));
                FaceAlong(a.Tr, LeapTangent(start, Vector3.zero, leave, h), s, fishFacingDegrees, fishMirrorWhenLeft);
                SetAlpha(a.Tr, Mathf.Clamp01(1f - Mathf.Pow(leave, 3f)));
                SetAlpha(a.Ripple, 0f);
            }
        }
    }

    // ————————————————————————————— 蛇:缠绕 —————————————————————————————

    // 缠绕从光圈【底部】起步:底部的顺时针切线正好是"朝左",和游过来的方向接得上,
    // 不会在切换的那一帧突然扭 90°。
    private const float SnakeCoilStartAngle = -Mathf.PI * 0.5f;

    private readonly List<Actor> snakeJoints = new List<Actor>();

    /// <summary>把整张蛇图横向切成若干节,每一节是一个关节。
    ///
    /// 走过两次弯路,记下来:
    ///   一开始是把【整条蛇】复制 7 份首尾排开 —— 屏幕上七条一样的蛇叠成千层。
    ///   然后改成只用一条完整的蛇 —— 不叠了,但整图刚性地绕圈,很僵硬。
    /// 现在按反馈做成真的关节:切片 + 沿路径依次落后 + 绕着光圈这根管子拧。</summary>
    private void BuildSnake()
    {
        // 需求:先响「嘶嘶」声,音效先于画面
        PlayIntroSfx("snake", 0.7f);

        snakeJoints.Clear();

        int count = Mathf.Max(3, snakeSegments);
        int stepWidth = Mathf.Max(1, snakeSourceRect.width / count);

        for (int i = 0; i < count; i++)
        {
            // 相邻切片彼此重叠一截,否则关节之间会露出缝
            int x = snakeSourceRect.x + i * stepWidth;
            int width = Mathf.Min(Mathf.RoundToInt(stepWidth * 1.6f),
                snakeSourceRect.x + snakeSourceRect.width - x);
            if (width <= 0)
            {
                break;
            }

            RectInt slice = new RectInt(x, snakeSourceRect.y, width, snakeSourceRect.height);
            Transform joint = AddCroppedCreature(
                SnakeArt, slice, "入场蛇_关节_" + i, snakeHeadSize, 34 - i);

            Actor actor = new Actor
            {
                Tr = joint,
                Sr = joint.GetComponentInChildren<SpriteRenderer>(true),
                BaseScale = 1f,
                Index = i
            };

            snakeJoints.Add(actor);
            actors.Add(actor);
        }
    }

    /// <summary>蛇:整条从右边扭动着游过来 → 缠上光圈 → 顺时针沿圈转一圈 → 消失。
    ///
    /// 关节全部走同一条路径,只是各自沿路径落后一点(跟随头部),所以是"跟着头走"
    /// 而不是各转各的。路径本身是绕着光圈的一条螺旋:一边沿圈向前,一边绕着圈这根
    /// 管子拧 —— 拧到管子背面的那半段会排到光圈后面去,缠绕感就是这么来的。</summary>
    private void UpdateSnake(float t)
    {
        if (snakeJoints.Count == 0)
        {
            return;
        }

        // 头部沿路径走到哪儿。负数 = 还在圈外那段直路上。
        float head = SnakeHeadArc(t);

        for (int i = 0; i < snakeJoints.Count; i++)
        {
            Actor a = snakeJoints[i];
            if (a.Tr == null)
            {
                continue;
            }

            float u = head - i * snakeSegmentSpacing;
            Vector3 pos = SnakePathPoint(u);

            // 朝向用路径上的数值切线,连扭动带缠绕都算进去了,比单算圆的切线活
            Vector3 tangent = SnakePathPoint(u + 0.06f) - SnakePathPoint(u - 0.06f);
            float faceDegrees = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg - snakeFacingDegrees;

            a.Tr.localPosition = pos;
            a.Tr.localRotation = Quaternion.Euler(0f, 0f, faceDegrees);
            a.Tr.localScale = Vector3.one;

            // 拧到光圈【后面】的关节要排到光圈后面去,不然缠绕看着还是贴在前面
            if (a.Sr != null)
            {
                a.Sr.sortingOrder = pos.z < 0f ? 34 - i : 14 - i;   // 光圈是 20
            }

            // 还没进场的关节(u 太靠前)先不显示,免得凭空堆在起点
            float entering = Mathf.InverseLerp(-SnakeApproachSpan - 0.4f, -SnakeApproachSpan, u);
            SetAlpha(a.Tr, entering * FadeOutTail(t));
        }
    }

    /// <summary>圈外那段直路,折算成多少"弧度"。和缠绕段共用一个参数,关节才能平滑地
    /// 从直路过渡到圈上,不用在两套坐标之间跳。</summary>
    private const float SnakeApproachSpan = 2.2f;

    private float SnakeHeadArc(float t)
    {
        float approach = Mathf.Clamp01(t / Mathf.Max(0.01f, snakeApproachRatio));
        float coil = Mathf.Clamp01((t - snakeApproachRatio) / Mathf.Max(0.01f, 1f - snakeApproachRatio));

        if (coil <= 0f)
        {
            return Mathf.Lerp(-SnakeApproachSpan, 0f, Mathf.SmoothStep(0f, 1f, approach));
        }

        return coil * Mathf.PI * 2f;
    }

    /// <summary>路径上参数 u 处的点。u &lt; 0 在圈外的直路上,u ≥ 0 是绕着光圈的螺旋。</summary>
    private Vector3 SnakePathPoint(float u)
    {
        Vector3 ringBottom = new Vector3(0f, -snakeCoilRadius, 0f);

        if (u < 0f)
        {
            // 圈外:从控制点直着游到光圈底部,垂直方向叠正弦 = 扭动
            float k = Mathf.Clamp01(1f + u / SnakeApproachSpan);
            Vector3 pos = Vector3.Lerp(snakeApproachFrom, ringBottom, k);
            pos.y += Mathf.Sin(u * 3f - timer * snakeWaveSpeed) * snakeWaveAmplitude;
            return pos;
        }

        // 圈上:顺时针向前(角度递减),同时绕着光圈这根管子拧
        float angle = SnakeCoilStartAngle - u;
        Vector3 onRing = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * snakeCoilRadius;

        float wind = u * snakeWindTurns;
        Vector3 outward = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);   // 圈的径向
        Vector3 axis = Vector3.forward;                                          // 圈的法向

        Vector3 wrap = (outward * Mathf.Cos(wind) + axis * Mathf.Sin(wind)) * snakeWindRadius;
        Vector3 wave = outward * (Mathf.Sin(u * 3f - timer * snakeWaveSpeed) * snakeWaveAmplitude * 0.5f);

        return onRing + wrap + wave;
    }

    // ————————————————————————————— 蛙:占位 —————————————————————————————

    // 蛙:把光圈【当作一片荷叶】来理解 —— 但不换贴图,还是那个光圈,只是概念上是荷叶。
    // 左侧另起一个小光圈当"小荷叶",右前方再渐显一片当"下一片荷叶"。
    private Transform frogPadLeft;    // 起跳的那片小荷叶(圈外的小光圈)
    private Transform frogPadNext;    // 前方渐显的下一片
    private Transform frog;           // 那只青蛙

    private void BuildFrog()
    {
        // 小荷叶(圈外)与下一片荷叶(前方,先不显示)。都是同一张光圈贴图,只是更小。
        frogPadLeft = AddCreature(RingArt, "入场蛙_小荷叶", ringSize * 0.42f, 18);
        frogPadLeft.localPosition = new Vector3(-0.95f, -0.22f, 0.10f);

        frogPadNext = AddCreature(RingArt, "入场蛙_下一片荷叶", ringSize * 0.55f, 18);
        frogPadNext.localPosition = new Vector3(0.98f, -0.05f, -0.06f);

        frog = AddCreature(FrogArt, "入场蛙", frogSize, 32);
        frog.localPosition = new Vector3(-0.95f, -0.05f, 0.10f);

        frogBaseScale = 1f;   // 支点的 1 就是已经拟合好的大小
        lastFrogCallAt = -1f;
    }

    /// <summary>蛙的分镜(光圈=荷叶的概念,贴图不变):
    /// 0.00~0.18 小荷叶浮现,青蛙从画面下方跳上去,叫两声
    /// 0.18~0.40 蹲在小荷叶上
    /// 0.40~0.55 跳进中间的大光圈(主荷叶)
    /// 0.55~0.75 在上面左右转动几下张望
    /// 0.75~0.88 前方渐显下一片荷叶,青蛙跳过去
    /// 0.88~1.00 从下一片荷叶再跳出画面外</summary>
    private void UpdateFrog(float t)
    {
        // 全部来自可拖动的控制点(Scene 视图里的箭头 / Inspector 里的数值)
        Vector3 padLeftPos = frogPadLeftPos;
        Vector3 onPadLeft = frogOnPadLeft;
        Vector3 center = Vector3.zero;
        Vector3 padNextPos = frogPadNextPos;
        Vector3 offScreen = frogExitTo;

        // 荷叶本身也跟着控制点走,不然拖了点、荷叶还留在原地
        if (frogPadLeft != null) { frogPadLeft.localPosition = padLeftPos; }
        if (frogPadNext != null) { frogPadNext.localPosition = padNextPos; }

        // —— 两片小荷叶的显隐 ——
        SetAlpha(frogPadLeft, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 0.12f, t)) * 0.75f * FadeOutTail(t));
        SetAlpha(frogPadNext, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 0.86f, t)) * 0.75f * FadeOutTail(t));

        if (frog == null)
        {
            return;
        }

        Vector3 pos;
        Vector3 heading = Vector3.right;   // 朝向:跳跃时用弧线切线,站着时水平
        float s = frogBaseScale > 0f ? frogBaseScale : frogSize;

        if (t < 0.18f)
        {
            // 从画面下方跳到小荷叶上
            float p = Mathf.Clamp01(t / 0.18f);
            Vector3 from = frogEnterFrom;
            pos = LeapPoint(from, onPadLeft, p, 0.30f);
            heading = LeapTangent(from, onPadLeft, p, 0.30f);
            if (t < 0.02f) { PlayFrogCall(); }
        }
        else if (t < 0.40f)
        {
            // 蹲着,轻微呼吸;这期间叫几声
            pos = onPadLeft + new Vector3(0f, Mathf.Sin(Time.time * 3.2f) * 0.012f, 0f);
            PlayFrogCallAt(0.22f, t);
            PlayFrogCallAt(0.32f, t);
        }
        else if (t < 0.55f)
        {
            // 跳进中间的大光圈(主荷叶)
            float p = Mathf.Clamp01((t - 0.40f) / 0.15f);
            pos = LeapPoint(onPadLeft, center, p, 0.42f);
            heading = LeapTangent(onPadLeft, center, p, 0.42f);
        }
        else if (t < 0.75f)
        {
            // 在光圈上左右转动几下张望:只改朝向,不倾斜
            pos = center + new Vector3(0f, Mathf.Sin(Time.time * 3f) * 0.015f, 0f);
            heading = Mathf.Sin((t - 0.55f) * 26f) >= 0f ? Vector3.right : Vector3.left;
        }
        else if (t < 0.88f)
        {
            // 跳到前方渐显的下一片荷叶
            float p = Mathf.Clamp01((t - 0.75f) / 0.13f);
            pos = LeapPoint(center, padNextPos, p, 0.38f);
            heading = LeapTangent(center, padNextPos, p, 0.38f);
        }
        else
        {
            // 再跳出画面外
            float p = Mathf.Clamp01((t - 0.88f) / 0.12f);
            pos = LeapPoint(padNextPos, offScreen, p, 0.30f);
            heading = LeapTangent(padNextPos, offScreen, p, 0.30f);
        }

        frog.localPosition = pos;
        FaceAlong(frog, heading, s, frogFacingDegrees, frogMirrorWhenLeft);
        SetAlpha(frog, t < 0.02f ? 0f : FadeOutTail(t));
    }

    private float frogBaseScale;
    private float lastFrogCallAt = -1f;

    private void PlayFrogCall()
    {
        PlayIntroSfx("swipe", 0.42f);   // 暂用挥划音;有蛙叫素材后换掉
    }

    /// <summary>到某个归一化时刻叫一声(只叫一次)。</summary>
    private void PlayFrogCallAt(float at, float t)
    {
        if (t >= at && lastFrogCallAt < at)
        {
            lastFrogCallAt = at;
            PlayFrogCall();
        }
    }

    // ————————————————————————————— 小工具 —————————————————————————————

    /// <summary>生成一只会动的生物,返回它的【支点】Transform。
    ///
    /// 这几张纹样贴图都是一大片透明底、图案缩在某个角落,所以贴图的物理中心离图案很远。
    /// 直接转贴图物体等于绕着那个空的物理中心转,生物会甩到一边去 —— 这就是之前
    /// 方向和位置怎么调都不对的根子。
    ///
    /// 办法:外面套一个空的支点物体,贴图作为子物体按「可见中心」反向偏移挂进去。
    /// 之后动画只动支点,旋转/镜像/缩放就都是绕着生物本身发生的。
    ///
    /// 支点的 localScale 是 1 = 已经拟合好的目标大小,所以 Actor.BaseScale 一律填 1。</summary>
    private Transform AddCreature(string art, string objectName, float targetHeight, int order)
    {
        GameObject pivot = new GameObject(objectName);
        pivot.transform.SetParent(root, false);
        pivot.transform.localPosition = Vector3.zero;
        spawned.Add(pivot);

        // AddIcon 会把 localPosition 设成 -可见中心偏移,使可见内容正好落在 0。
        // 用 worldPositionStays:false 换父级,这个偏移原样保留,于是相对支点也是居中的。
        GameObject icon = LijiangEchoStageKit.AddIcon(
            root, spawned, art, objectName + "_图", Vector3.zero, targetHeight, order, 0f);
        if (icon != null)
        {
            icon.transform.SetParent(pivot.transform, false);
        }

        return pivot.transform;
    }

    /// <summary>同上,但只取贴图的一块(蛇切关节用)。同样套支点,旋转绕自己发生。</summary>
    private Transform AddCroppedCreature(string art, RectInt crop, string objectName, float targetHeight, int order)
    {
        GameObject pivot = new GameObject(objectName);
        pivot.transform.SetParent(root, false);
        pivot.transform.localPosition = Vector3.zero;
        spawned.Add(pivot);

        GameObject icon = LijiangEchoStageKit.AddCroppedSprite(
            root, spawned, art, objectName + "_图", crop, Vector3.zero, targetHeight, order, 0f, false);
        if (icon != null)
        {
            icon.transform.SetParent(pivot.transform, false);
        }

        return pivot.transform;
    }

    /// <summary>抛物线跳跃上的一点:水平匀速,竖直叠一个 sin 拱形。</summary>
    private static Vector3 LeapPoint(Vector3 from, Vector3 to, float p, float height)
    {
        Vector3 pos = Vector3.Lerp(from, to, p);
        pos.y += Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI) * height;
        return pos;
    }

    /// <summary>同上,但弧线中段还朝【相机方向】凸出来一块。
    ///
    /// -Z 是靠近玩家的方向(和鸟的 depth 是同一套约定:depth 越负越近、画得越大)。
    /// 起点和终点的 z 不动,所以鱼从原平面跳起、冲到你面前、再落回原平面的圈里。</summary>
    private static Vector3 LeapPoint(Vector3 from, Vector3 to, float p, float height, float depth)
    {
        Vector3 pos = LeapPoint(from, to, p, height);
        pos.z -= NearBulge(p) * depth;
        return pos;
    }

    /// <summary>弧线中段的凸起量,0 → 1 → 0。也用来算"离得近所以画得大"。</summary>
    private static float NearBulge(float p)
    {
        return Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI);
    }

    /// <summary>该抛物线在 p 处的切线方向(解析求导),用来让头朝着飞行方向。</summary>
    private static Vector3 LeapTangent(Vector3 from, Vector3 to, float p, float height)
    {
        Vector3 d = to - from;                                   // 水平分量的导数
        d.y += Mathf.Cos(Mathf.Clamp01(p) * Mathf.PI) * Mathf.PI * height;   // 拱形的导数
        d.z = 0f;
        return d;
    }

    /// <summary>让物件的头朝着自己的运动方向。
    ///
    /// <paramref name="artFacingDegrees"/> 是贴图本身画的朝向(0=朝右,90=朝上,180=朝左,
    /// -90=朝下)。以前这里写死了"贴图朝右",但花山纹样这几张各朝各的,方向自然对不上,
    /// 现在改成每种生物在 Inspector 里各填各的。
    ///
    /// <paramref name="mirrorWhenLeft"/>:侧视的贴图(鱼、鸟)往左走时用负的 scale.x 翻面,
    /// 肚子才不会朝天;正面/俯视的贴图关掉,免得来回闪。</summary>
    private static void FaceAlong(Transform target, Vector3 delta, float scale,
        float artFacingDegrees, bool mirrorWhenLeft)
    {
        if (target == null)
        {
            return;
        }

        if (delta.sqrMagnitude < 0.0000001f)
        {
            target.localScale = Vector3.one * scale;
            return;
        }

        if (!mirrorWhenLeft)
        {
            float heading = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            target.localRotation = Quaternion.Euler(0f, 0f, heading - artFacingDegrees);
            target.localScale = Vector3.one * scale;
            return;
        }

        // 负的 scale.x 会先把贴图整个镜像掉,贴图自带的朝向角也跟着翻,所以镜像那一支
        // 的角度是 (贴图朝向 - 俯仰) 而不是 (俯仰)。老代码漏了这个翻转,结果往左跳的时候
        // 头的俯仰是反的 —— 起跳该朝上却朝下。
        bool goingLeft = delta.x < 0f;
        float pitch = Mathf.Atan2(delta.y, Mathf.Abs(delta.x)) * Mathf.Rad2Deg;
        target.localRotation = Quaternion.Euler(0f, 0f,
            goingLeft ? artFacingDegrees - pitch : pitch - artFacingDegrees);
        target.localScale = new Vector3(goingLeft ? -scale : scale, scale, scale);
    }

    /// <summary>最后 15% 时间整体淡出,好让③的打击环节接上去不突兀。</summary>
    private static float FadeOutTail(float t)
    {
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.85f, 1f, t));
    }

    private static void SetAlpha(Transform target, float alpha)
    {
        if (target == null)
        {
            return;
        }

        // 生物现在是「空支点 + 贴图子物体」,渲染器在子物体上,所以要往下找一层。
        SpriteRenderer sr = target.GetComponent<SpriteRenderer>()
            ?? target.GetComponentInChildren<SpriteRenderer>(true);
        if (sr == null)
        {
            return;
        }

        float a = Mathf.Clamp01(alpha);

        Color c = sr.color;
        c.a = a;
        sr.color = c;

        // LijiangEchoSpriteLayer.OnValidate 会把 renderer 的颜色按它自己的 alpha 字段重刷一遍。
        // 生成时那个字段是 0,所以编辑器里一重编译/一改 Inspector,生物就整个消失、只剩光圈。
        // 这里把值同步过去,免得被它覆盖掉。
        LijiangEchoSpriteLayer layer = sr.GetComponent<LijiangEchoSpriteLayer>();
        if (layer != null)
        {
            layer.alpha = a;
        }
    }
}
