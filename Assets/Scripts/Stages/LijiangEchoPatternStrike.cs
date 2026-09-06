using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// 扫码功能的【③打击环节】(见 docs/TODO-QR-PATTERN-SCAN.md)。
///
/// 入场动画演完之后接这里:音符按各自的类型飞向光圈,玩家用对应的打法命中。
///   鱼纹 · 单击 —— 左边飞来的用左手打,右边飞来的用右手打
///   蛇纹 · 长按 —— 按住不放,音符从紫渐变到金,金了即满
///   蛙纹 · 挥划 —— 从下方蓄势升到圆心,过判定点后抛物线跃出;要向上挥
///   鸟纹 · 双击 —— 左右各飞来一只,圆心叠合;两只手要同时打
///
/// 【和战斗共用同一批东西,不另做一套】
/// 一开始我拿裸贴图自己拼音符,结果蛇纹、鸟纹和战斗完全两个样。现在:
///   · 音符 = 直接实例化战斗的 Prefab(Resources/LijiangEchoNotes/Note_鱼/蛇/蛙/鸟)——
///     贴图、裁剪、大小、居中、光晕全由 Prefab 决定,队友在编辑器里怎么摆这里就怎么显示
///   · 镜像规则 = 直接读战斗的设置资源 LijiangEchoBattleSettings
///     (autoMirrorNotesByDirection / mirrorStrike / mirrorHold / mirrorSwipe /
///      mirrorDouble / doubleNoteMirrorConverge),不在这里另立开关
///   · 长按变色 = 战斗那两个颜色原样照抄,而且变色的是【音符本身】不是光圈
///
/// 只有判定还是单独实现的:它锁在 LijiangEchoGameController 那 5800 行里,
/// 拆出来是 docs/REFACTOR-STEP2-BATTLE-SPLIT.md 的活。阈值仍对齐战斗的取值。
///
/// PC 兜底和战斗完全一致,避免两处规则打架:
///   鼠标左键 = 右手,Shift + 左键 = 左手,空格/回车 = 双手,↑ 键 = 向上挥
/// </summary>
public class LijiangEchoPatternStrike : MonoBehaviour
{
    [Flags]
    private enum Hand
    {
        None = 0,
        Left = 1,
        Right = 2,
        Both = Left | Right
    }

    private const string RingArt = "battle/hit_ring_center";

    // 战斗里长按的紫→金,原样照抄(LijiangEchoGameController:3651),不另配一套颜色
    private static readonly Color HoldPurple = new Color(0.78f, 0.48f, 1f);
    private static readonly Color HoldGold = new Color(1f, 0.9f, 0.35f);

    [Header("节奏")]
    [Tooltip("一轮打几个音符(蛇纹是长按,固定一个)。")]
    [Range(1, 8)] [SerializeField] private int noteCount = 3;

    [Tooltip("音符从出现到抵达光圈用多久(秒)。")]
    [SerializeField] private float approachSeconds = 1.6f;

    [Tooltip("两个音符之间隔多久。")]
    [SerializeField] private float noteInterval = 1.3f;

    [Tooltip("判定窗口(秒)。抵达时刻的前后各这么多算命中。对齐战斗的 hitWindowSeconds。")]
    [SerializeField] private float hitWindow = 0.5f;

    [Tooltip("蛙纹(挥划)的判定窗口放宽倍数。对齐战斗的 swipeWindowScale。")]
    [SerializeField] private float swipeWindowScale = 1.6f;

    [Header("蛇纹 · 长按")]
    [Tooltip("要按住多久才算满。")]
    [SerializeField] private float holdSeconds = 1.8f;

    [Tooltip("没按住时读条回落的速度倍率。松手就清零太挫,留一点缓冲。")]
    [SerializeField] private float holdDecay = 0.6f;

    [Header("挥划判定")]
    [Tooltip("手柄挥动速度阈值(米/秒)。对齐战斗的 swipeMinimumSpeed。")]
    [SerializeField] private float swipeMinimumSpeed = 0.34f;

    [Tooltip("其中向上的分量至少要多少。蛙纹的标准动作是【上挑】。")]
    [SerializeField] private float swipeUpwardSpeed = 0.30f;

    [Header("双手同时")]
    [Tooltip("两只手先后按下,间隔在这个时间内都算「同时」。对齐战斗的 twoHandSyncWindow。")]
    [SerializeField] private float twoHandSyncWindow = 0.35f;

    [Header("外观")]
    [Tooltip("中心光圈的大小(世界单位)。"
        + "⚠️ 扫码流程里这个值由 LijiangEchoQrScan 统一下发,和入场动画保证一样大。")]
    [SerializeField] private float ringSize = LijiangEchoPatternIntro.DefaultRingSize;

    /// <summary>光圈大小的外部入口,由扫码脚本统一下发,见 LijiangEchoPatternIntro.RingSize 的说明。</summary>
    public float RingSize
    {
        get => ringSize;
        set => ringSize = value;
    }

    [Tooltip("音符从多远飞来 —— 按光圈大小的倍数算,这样改光圈大小时布局自动跟着走。")]
    [SerializeField] private float spawnDistanceRatio = 2f;

    [Tooltip("提示文字大小 —— 按光圈大小的倍数算。反馈:原来 0.08 太大,字比光圈一半还高、压到纹样上了。")]
    [SerializeField] private float hintTextRatio = 0.022f;

    [Tooltip("纹样名放在光圈下方多远 —— 按光圈大小的倍数算,越大越靠下。")]
    [SerializeField] private float nameTextOffsetRatio = 1.6f;

    [Tooltip("判定文字放在光圈上方多远 —— 按光圈大小的倍数算。")]
    [SerializeField] private float judgeTextOffsetRatio = 1.3f;

    [Tooltip("音符画多大 —— 按光圈大小的倍数算(0.55 = 音符高度约为光圈的一半多)。\n"
        + "音符 Prefab 自带的尺寸是照战斗那个舞台配的,直接拿过来会和这里放大过的光圈不成比例,\n"
        + "所以量一下 Prefab 的实际高度再等比缩放。")]
    [SerializeField] private float noteSizeRatio = 0.55f;

    // ——— 运行时 ———
    private Transform root;
    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<Note> notes = new List<Note>();
    private Transform ring;
    private SpriteRenderer ringRenderer;
    private TextMesh judgeText;   // 上方:打法提示 → 命中/漏了/按住进度
    private TextMesh nameText;    // 下方:纹样名,固定不变
    private Transform revealed;          // 命中后浮现在圆心的纹样

    private LijiangEchoPatternIntro.Pattern pattern;
    private bool running;
    private float timer;
    private int hits;
    private int misses;
    private Action<int, int> onFinished;   // (命中数, 总数)

    // 战斗的设置资源。镜像规则、鸟纹汇合与否都读它,不在这里另立一套。
    private LijiangEchoBattleSettings settings;

    // 长按
    private float holdProgress;
    private Note holdNote;

    /// <summary>缩放乘在 Prefab 自己的大小上,不要用 Vector3.one 覆盖掉 ——
    /// Prefab 里怎么摆的就得保持怎么样。镜像过的音符 x 是负的,这里保留符号。</summary>
    private static void SetNoteScale(Note note, float k)
    {
        if (note == null || note.Tr == null)
        {
            return;
        }

        if (note.BaseScale == Vector3.zero)
        {
            note.BaseScale = note.Tr.localScale;
        }

        note.Tr.localScale = note.BaseScale * k;
    }

    // 手柄
    private Transform leftAnchor;
    private Transform rightAnchor;
    private Vector3 lastLeftPos;
    private Vector3 lastRightPos;
    private Vector3 leftVelocity;
    private Vector3 rightVelocity;
    private bool motionReady;

    private bool previousLeftPressed;
    private bool previousRightPressed;
    private float leftPressedAt = -99f;
    private float rightPressedAt = -99f;
    private Hand handsPressedThisFrame;

    private sealed class Note
    {
        public Transform Tr;
        public Vector3 From;
        public float ArriveAt;      // 抵达光圈的时刻(秒)
        public Hand RequiredHand;   // 该用哪只手(鱼纹左右分边)
        public bool Resolved;
        public bool Hit;

        // 鸟纹「镜像汇合」的分身(= 左翼)。纯视觉,不参与判定,
        // 位置永远取本体的 x 取反,和本体对称地飞向圆心。
        public Transform MirrorTwin;

        // Prefab 里可能有好几层(纹样 + 光晕),各层原本的透明度要留着按比例淡入,
        // 直接统一写 1 会把光晕也拉满,和战斗看着就不一样了。战斗那边同样是这么缓存的。
        public SpriteRenderer[] Renderers;
        public float[] BaseAlpha;
        public SpriteRenderer[] TwinRenderers;
        public float[] TwinBaseAlpha;

        // Prefab 自带的大小,飞入时的缩放乘在它上面
        public Vector3 BaseScale;
    }

    /// <summary>战斗的音符 Prefab。视觉(贴图/裁剪/大小/居中/光晕)完全由 Prefab 决定,
    /// 和战斗共用同一份 —— 队友在编辑器里怎么摆,这里就怎么显示。</summary>
    private static GameObject LoadNotePrefab(LijiangEchoPatternIntro.Pattern pattern)
    {
        return Resources.Load<GameObject>("LijiangEchoNotes/" + NotePrefabName(pattern));
    }

    private static string NotePrefabName(LijiangEchoPatternIntro.Pattern pattern)
    {
        switch (pattern)
        {
            case LijiangEchoPatternIntro.Pattern.Snake: return "Note_Snake";
            case LijiangEchoPatternIntro.Pattern.Frog: return "Note_Frog";
            case LijiangEchoPatternIntro.Pattern.Bird: return "Note_Bird";
            default: return "Note_Fish";
        }
    }

    /// <summary>要不要按飞入方向做水平镜像 —— 规则和取值都来自战斗的设置资源,
    /// 不在这里另立一套。资源缺失时退回和战斗脚本一样的默认值。
    /// (鱼纹默认开:从左飞入时镜像成朝右,鱼头就朝着圆心。)</summary>
    private bool ShouldAutoMirror()
    {
        if (settings != null && !settings.autoMirrorNotesByDirection)
        {
            return false;
        }

        switch (pattern)
        {
            case LijiangEchoPatternIntro.Pattern.Snake: return settings != null && settings.mirrorHold;
            case LijiangEchoPatternIntro.Pattern.Frog: return settings != null && settings.mirrorSwipe;
            case LijiangEchoPatternIntro.Pattern.Bird: return settings != null && settings.mirrorDouble;
            default: return settings == null || settings.mirrorStrike;   // 鱼纹默认开
        }
    }

    /// <summary>把刚实例化出来的音符等比缩放到「光圈的 noteSizeRatio 倍」那么高。
    ///
    /// Prefab 自带的大小是照战斗那个舞台配的,而这里的光圈被放大到了 1.97,
    /// 直接拿过来音符就显得很小 —— 所以量一下它渲染出来实际多高,再按比例缩。
    /// 量的是所有子渲染器合起来的包围盒(Prefab 里常常还有一层光晕)。</summary>
    private void FitNoteToRing(Transform note)
    {
        if (note == null || root == null)
        {
            return;
        }

        // ⚠️ 只量【纹样本身】那一层。四个 Prefab 都是 Visual(纹样)+ Glow(光晕)两层,
        // 而光晕比纹样大一圈 —— 之前量的是两层合起来的包围盒,等于把光晕也算进了目标高度,
        // 纹样自然就被压得很小。所以优先量名字里带 Visual 的那层,退而求其次也要排除 Glow。
        float currentHeight = MeasureVisualHeight(note);
        if (currentHeight < 0.0001f)
        {
            return;
        }

        // 世界高度换回舞台的局部单位(舞台本身被二维码大小缩放过)
        float stageScale = Mathf.Abs(root.lossyScale.y);
        if (stageScale < 0.0001f)
        {
            return;
        }

        currentHeight /= stageScale;
        float targetHeight = ringSize * noteSizeRatio;
        if (currentHeight < 0.0001f || targetHeight <= 0f)
        {
            return;
        }

        // 保留镜像用的负号
        Vector3 scale = note.localScale;
        float k = targetHeight / currentHeight;
        note.localScale = new Vector3(scale.x * k, scale.y * k, scale.z * k);
    }

    /// <summary>量出「纹样本身」在世界空间里有多高。
    ///
    /// 用 sprite.bounds × lossyScale 直接算,不走 Renderer.bounds —— 后者对刚
    /// Instantiate 出来的物体不一定已经更新。生成时没有旋转,所以这么算是精确的。</summary>
    private static float MeasureVisualHeight(Transform note)
    {
        Transform visual = FindDescendant(note, "Visual");
        SpriteRenderer[] renderers = (visual != null ? visual : note)
            .GetComponentsInChildren<SpriteRenderer>(true);

        float tallest = 0f;
        foreach (SpriteRenderer sr in renderers)
        {
            if (sr == null || sr.sprite == null)
            {
                continue;
            }

            // 没找到 Visual 时的兜底:至少别把光晕算进来
            if (visual == null &&
                sr.gameObject.name.IndexOf("Glow", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                continue;
            }

            float h = VisibleSpriteHeight(sr.sprite) * Mathf.Abs(sr.transform.lossyScale.y);
            tallest = Mathf.Max(tallest, h);
        }

        return tallest;
    }

    /// <summary>精灵里【图案本身】有多高,而不是整张贴图有多高。
    ///
    /// 这才是"打击的纹样看起来很小"的主因:sprite.bounds 量的是整个贴图矩形,
    /// 而这些纹样只占其中一小块 —— 鱼纹的图案只有 272px / 整图 630px,
    /// 按整图去拟合的话,鱼实际只有目标高度的 43%,自然显得小一大截。
    ///
    /// 用导入时烘好的 Tight 网格顶点来量真实高度(不需要贴图开 Read/Write,
    /// 和入场动画里算可见中心是同一条路子)。</summary>
    private static float VisibleSpriteHeight(Sprite sprite)
    {
        Vector2[] vertices = sprite.vertices;
        if (vertices != null && vertices.Length > 0)
        {
            float min = vertices[0].y;
            float max = vertices[0].y;
            for (int i = 1; i < vertices.Length; i++)
            {
                min = Mathf.Min(min, vertices[i].y);
                max = Mathf.Max(max, vertices[i].y);
            }

            float tight = max - min;
            if (tight > 0.0001f)
            {
                return tight;
            }
        }

        return sprite.bounds.size.y;   // 拿不到网格就退回整图
    }

    private static Transform FindDescendant(Transform parent, string name)
    {
        foreach (Transform child in parent.GetComponentsInChildren<Transform>(true))
        {
            if (child != parent && child.name == name)
            {
                return child;
            }
        }

        return null;
    }

    private static SpriteRenderer[] CacheRenderers(Transform target, out float[] baseAlpha)
    {
        SpriteRenderer[] renderers = target.GetComponentsInChildren<SpriteRenderer>(true);
        baseAlpha = new float[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            baseAlpha[i] = renderers[i] != null ? renderers[i].color.a : 1f;
        }

        return renderers;
    }

    private static void ApplyAlpha(SpriteRenderer[] renderers, float[] baseAlpha, float k)
    {
        if (renderers == null)
        {
            return;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null)
            {
                continue;
            }

            Color c = renderers[i].color;
            c.a = Mathf.Clamp01((baseAlpha != null && i < baseAlpha.Length ? baseAlpha[i] : 1f) * k);
            renderers[i].color = c;
        }
    }

    // ————————————————————————————— 对外 —————————————————————————————

    /// <summary>起一轮打击。anchor 传二维码锚点,打击就发生在那儿。</summary>
    public void Begin(LijiangEchoPatternIntro.Pattern which, Transform anchor, Action<int, int> onDone)
    {
        Teardown();

        pattern = which;
        onFinished = onDone;
        timer = 0f;
        hits = 0;
        misses = 0;
        holdProgress = 0f;
        holdNote = null;

        // 直接读战斗那份设置资源,镜像规则和鸟纹汇合与否都跟着它走
        settings = Resources.Load<LijiangEchoBattleSettings>(LijiangEchoBattleSettings.ResourceName);

        GameObject holder = new GameObject("漓江回声_扫码打击");
        holder.transform.SetParent(anchor, false);
        holder.transform.localPosition = Vector3.zero;
        holder.transform.localRotation = Quaternion.identity;
        root = holder.transform;
        spawned.Add(holder);

        BuildRing();
        BuildHint();
        BuildNotes();

        running = true;
    }

    public void Teardown()
    {
        running = false;
        onFinished = null;
        notes.Clear();
        holdNote = null;
        ring = null;
        ringRenderer = null;
        judgeText = null;
        nameText = null;
        revealed = null;
        revealedRenderers = null;
        revealedBaseAlpha = null;
        spawned.Clear();

        if (root != null)
        {
            Destroy(root.gameObject);
            root = null;
        }
    }

    // ————————————————————————————— 主循环 —————————————————————————————

    private void Update()
    {
        if (!running || root == null)
        {
            return;
        }

        SampleControllers();
        SampleHands();
        timer += Time.deltaTime;

        if (pattern == LijiangEchoPatternIntro.Pattern.Snake)
        {
            UpdateHold();
        }
        else
        {
            UpdateNotes();
        }
    }

    // ————————————————————————————— 布置 —————————————————————————————

    private void BuildRing()
    {
        GameObject ringObject = LijiangEchoStageKit.AddIcon(
            root, spawned, RingArt, "打击光圈", Vector3.zero, ringSize, 20, 0.9f);
        if (ringObject != null)
        {
            ring = ringObject.transform;
            ringRenderer = ringObject.GetComponent<SpriteRenderer>();
        }
    }

    /// <summary>两行字分工明确:
    ///   上方 = 会变的那行 —— 一开始是打法提示,打完换成命中/漏了/按住进度
    ///   下方 = 不变的那行 —— 就是纹样名,让人一眼知道现在在打哪个
    /// 之前两样都挤在下面一行,判定一出来提示就被顶掉了。</summary>
    private void BuildHint()
    {
        judgeText = LijiangEchoStageKit.AddText(
            root, spawned, HintFor(pattern),
            new Vector3(0f, ringSize * judgeTextOffsetRatio, 0f),
            HintTextSize, Color.white, 40);

        nameText = LijiangEchoStageKit.AddText(
            root, spawned, LijiangEchoQrScan.PatternName(pattern),
            new Vector3(0f, -ringSize * nameTextOffsetRatio, 0f),
            HintTextSize, Color.white, 40);
    }

    /// <summary>音符全部用【战斗那套 Prefab】实例化 —— 贴图、裁剪、大小、居中、光晕
    /// 都由 Prefab 决定,和战斗里长得一模一样。之前是拿裸贴图自己拼的,所以蛇纹、
    /// 鸟纹跟战斗完全对不上。</summary>
    private void BuildNotes()
    {
        GameObject prefab = LoadNotePrefab(pattern);
        if (prefab == null)
        {
            Debug.LogWarning($"[漓江回声] 找不到音符 Prefab:LijiangEchoNotes/{NotePrefabName(pattern)}");
            return;
        }

        if (pattern == LijiangEchoPatternIntro.Pattern.Snake)
        {
            // 长按:一个音符停在圆心,按住的过程中它从紫渐变到金(和战斗一致)
            holdNote = SpawnNote(prefab, "蛇纹长按", Vector3.zero, 0f, Hand.Both);
            return;
        }

        int count = Mathf.Max(1, noteCount);
        for (int i = 0; i < count; i++)
        {
            float arriveAt = approachSeconds + i * noteInterval;

            switch (pattern)
            {
                case LijiangEchoPatternIntro.Pattern.Fish:
                {
                    // 左右交替飞来,飞哪边就得用哪只手
                    bool fromLeft = i % 2 == 0;
                    SpawnNote(prefab, "鱼纹音符_" + i,
                        new Vector3(fromLeft ? -SpawnDistance : SpawnDistance, 0.1f, 0.25f),
                        arriveAt, fromLeft ? Hand.Left : Hand.Right);
                    break;
                }

                case LijiangEchoPatternIntro.Pattern.Frog:
                {
                    // 从下方蓄势升上来
                    SpawnNote(prefab, "蛙纹音符_" + i,
                        new Vector3(0f, -SpawnDistance, 0.2f), arriveAt, Hand.None);
                    break;
                }

                default:
                {
                    // 鸟纹「镜像汇合」,照搬战斗(LijiangEchoGameController:3017):
                    // 同一个 Note_Bird,原体固定从【右】飞入(= 右翼),再实例化一只
                    // localScale.x 取负的镜像分身从【左】飞入(= 左翼),对称汇合成整鸟。
                    Note note = SpawnNote(prefab, "鸟纹音符_" + i,
                        new Vector3(SpawnDistance, 0.35f, 0.2f), arriveAt, Hand.Both);

                    bool converge = settings == null || settings.doubleNoteMirrorConverge;
                    if (converge && note != null)
                    {
                        GameObject twin = Instantiate(prefab, root, false);
                        twin.name = "鸟纹音符_镜像分身_" + i;
                        twin.transform.localPosition = new Vector3(-SpawnDistance, 0.35f, 0.2f);
                        twin.transform.localRotation = Quaternion.identity;

                        FitNoteToRing(twin.transform);

                        Vector3 ts = twin.transform.localScale;
                        twin.transform.localScale = new Vector3(-Mathf.Abs(ts.x), ts.y, ts.z);
                        spawned.Add(twin);

                        note.MirrorTwin = twin.transform;
                        note.TwinRenderers = CacheRenderers(twin.transform, out float[] twinBase);
                        note.TwinBaseAlpha = twinBase;
                        ApplyAlpha(note.TwinRenderers, note.TwinBaseAlpha, 0f);
                    }

                    break;
                }
            }
        }
    }

    private Note SpawnNote(GameObject prefab, string objectName, Vector3 from, float arriveAt, Hand hand)
    {
        GameObject inst = Instantiate(prefab, root, false);
        inst.name = objectName;
        inst.transform.localPosition = from;
        inst.transform.localRotation = Quaternion.identity;

        // 先按光圈等比缩放,再做镜像 —— 顺序反了的话负号会被缩放覆盖掉
        FitNoteToRing(inst.transform);

        // 按飞入方向自动镜像 —— 规则和开关都取自战斗的设置资源。
        // 纹样默认朝左:从左侧飞入时镜像成朝右,头就朝着圆心(= 飞行方向)。
        if (from.x < 0f && ShouldAutoMirror())
        {
            Vector3 sc = inst.transform.localScale;
            inst.transform.localScale = new Vector3(-Mathf.Abs(sc.x), sc.y, sc.z);
        }

        spawned.Add(inst);

        Note note = new Note
        {
            Tr = inst.transform,
            From = from,
            ArriveAt = arriveAt,
            RequiredHand = hand,
            Renderers = CacheRenderers(inst.transform, out float[] baseAlpha)
        };
        note.BaseAlpha = baseAlpha;

        // 生成的这一帧先藏起来:飞入的透明度是 Update 里按进度算的,
        // 不先压成 0 的话会在起飞点闪一帧 Prefab 自带的满透明度。
        ApplyAlpha(note.Renderers, note.BaseAlpha, 0f);

        notes.Add(note);
        return note;
    }

    // ————————————————————————————— 飞入与判定 —————————————————————————————

    private void UpdateNotes()
    {
        float window = CurrentWindow();
        int pending = 0;

        for (int i = 0; i < notes.Count; i++)
        {
            Note note = notes[i];
            if (note.Tr == null)
            {
                continue;
            }

            float toArrive = note.ArriveAt - timer;
            float p = Mathf.Clamp01(1f - toArrive / Mathf.Max(0.01f, approachSeconds));

            // 飞向圆心。蛙纹是「蓄势升上来」,所以走一条抛物线而不是直线
            Vector3 pos;
            if (pattern == LijiangEchoPatternIntro.Pattern.Frog)
            {
                pos = Vector3.Lerp(note.From, Vector3.zero, p);
                pos.y += Mathf.Sin(p * Mathf.PI) * 0.18f;
            }
            else
            {
                pos = Vector3.Lerp(note.From, Vector3.zero, Mathf.SmoothStep(0f, 1f, p));
            }

            note.Tr.localPosition = pos;

            // 越近越大一点。缩放乘在 Prefab 自己的大小上 —— Prefab 怎么摆的就保持怎么样,
            // 不要用 Vector3.one 把它的尺寸覆盖掉。
            float scale = Mathf.Lerp(0.65f, 1f, p);
            SetNoteScale(note, scale);

            if (note.Resolved)
            {
                // 已判定的:命中往圆心缩、失误的继续飘走并淡出
                float after = Mathf.Clamp01((timer - note.ArriveAt) / 0.45f);
                float resolvedAlpha = (1f - after) * (note.Hit ? 1f : 0.4f);
                ApplyAlpha(note.Renderers, note.BaseAlpha, resolvedAlpha);
                if (note.Hit)
                {
                    SetNoteScale(note, Mathf.Lerp(scale, scale * 1.5f, after));
                }

                SyncMirrorTwin(note, resolvedAlpha);
                continue;
            }

            ApplyAlpha(note.Renderers, note.BaseAlpha, Mathf.Clamp01(p * 2.2f));
            SyncMirrorTwin(note, Mathf.Clamp01(p * 2.2f));

            if (timer < note.ArriveAt - window)
            {
                pending++;
                continue;   // 还没进判定窗口
            }

            if (timer > note.ArriveAt + window)
            {
                // 漏了
                note.Resolved = true;
                note.Hit = false;
                misses++;
                ShowJudge("漏了");
                continue;
            }

            pending++;
            if (TryJudge(note))
            {
                note.Resolved = true;
                note.Hit = true;
                hits++;
                OnHit();
            }
        }

        // 全部判完 → 收尾
        if (pending == 0 && timer > LastArriveTime() + window + 0.6f)
        {
            Finish();
        }
    }

    /// <summary>把镜像分身(左翼)摆到本体的对称位置。
    ///
    /// 和战斗里那段一样(LijiangEchoGameController:3444):位置取本体的 x 取反,
    /// scale.x 取负做水平镜像,透明度同步 —— 于是两只对称地飞向圆心拼成整鸟。
    /// 分身纯视觉,不进判定。</summary>
    private static void SyncMirrorTwin(Note note, float alpha)
    {
        if (note.MirrorTwin == null || note.Tr == null)
        {
            return;
        }

        Vector3 pos = note.Tr.localPosition;
        note.MirrorTwin.localPosition = new Vector3(-pos.x, pos.y, pos.z);

        Vector3 scale = note.Tr.localScale;
        note.MirrorTwin.localScale = new Vector3(-Mathf.Abs(scale.x), scale.y, scale.z);

        ApplyAlpha(note.TwinRenderers, note.TwinBaseAlpha, alpha);
    }

    /// <summary>音符起飞点和文字位置都跟着光圈大小走,改一个数整体等比。</summary>
    private float SpawnDistance => ringSize * spawnDistanceRatio;

    private float HintTextSize => ringSize * hintTextRatio;

    private float CurrentWindow()
    {
        return pattern == LijiangEchoPatternIntro.Pattern.Frog
            ? hitWindow * swipeWindowScale   // 挥划动作慢,窗口放宽,和战斗一致
            : hitWindow;
    }

    private float LastArriveTime()
    {
        float last = approachSeconds;
        for (int i = 0; i < notes.Count; i++)
        {
            last = Mathf.Max(last, notes[i].ArriveAt);
        }

        return last;
    }

    /// <summary>按纹样各自的打法判定。</summary>
    private bool TryJudge(Note note)
    {
        switch (pattern)
        {
            case LijiangEchoPatternIntro.Pattern.Fish:
            {
                // 单击,而且必须是对应那只手 —— 左边飞来的用左手
                return (handsPressedThisFrame & note.RequiredHand) != Hand.None;
            }

            case LijiangEchoPatternIntro.Pattern.Frog:
            {
                // 挥划:向上挑
                return SwipedUpThisFrame();
            }

            default:
            {
                // 鸟纹:两只手要同时。允许一前一后,只要间隔在同步窗口内。
                // 按下的时刻是在 SampleHands 里【每帧】记的,不是等到进判定窗口才记 ——
                // 否则先按的那只手根本没被记下来,两只手永远凑不齐。
                bool bothKnown = leftPressedAt > 0f && rightPressedAt > 0f;
                if (!bothKnown)
                {
                    return false;
                }

                bool inSync = Mathf.Abs(leftPressedAt - rightPressedAt) <= twoHandSyncWindow;
                bool fresh = Time.time - Mathf.Max(leftPressedAt, rightPressedAt) <= twoHandSyncWindow;
                if (inSync && fresh)
                {
                    leftPressedAt = -99f;   // 一对只算一次
                    rightPressedAt = -99f;
                    return true;
                }

                return false;
            }
        }
    }

    // ————————————————————————————— 蛇纹 · 长按 —————————————————————————————

    private void UpdateHold()
    {
        bool held = HandsHeld() != Hand.None;

        holdProgress += (held ? 1f : -holdDecay) * Time.deltaTime / Mathf.Max(0.1f, holdSeconds);
        holdProgress = Mathf.Clamp01(holdProgress);

        // 照搬战斗(LijiangEchoGameController:3651):变色的是【音符本身】,不是光圈,
        // 而且用的就是那两个颜色 —— 之前我把光圈染成紫金,所以看着和战斗完全两样。
        if (holdNote != null && holdNote.Renderers != null)
        {
            Color tint = Color.Lerp(HoldPurple, HoldGold, holdProgress);
            for (int i = 0; i < holdNote.Renderers.Length; i++)
            {
                SpriteRenderer sr = holdNote.Renderers[i];
                if (sr == null)
                {
                    continue;
                }

                float baseA = holdNote.BaseAlpha != null && i < holdNote.BaseAlpha.Length
                    ? holdNote.BaseAlpha[i]
                    : 1f;
                sr.color = new Color(tint.r, tint.g, tint.b, baseA);
            }
        }

        ShowJudge(holdProgress >= 1f
            ? "满了"
            : $"按住不放  {Mathf.RoundToInt(holdProgress * 100f)}%");

        if (holdProgress >= 1f)
        {
            hits = 1;
            OnHit();
            Finish();
        }
        else if (timer > holdSeconds * 3f + 4f)
        {
            misses = 1;
            Finish();
        }
    }

    // ————————————————————————————— 命中反馈 —————————————————————————————

    private void OnHit()
    {
        LijiangEchoStageKit.PlaySfx(HitSfx(pattern), 0.85f);

        // 需求:命中后光圈中央浮现对应纹样。用的还是那个音符 Prefab,
        // 免得又出现"打击时是一个样、浮现出来又是另一个样"。
        if (revealed == null)
        {
            GameObject prefab = LoadNotePrefab(pattern);
            if (prefab != null)
            {
                GameObject inst = Instantiate(prefab, root, false);
                inst.name = "命中纹样";
                inst.transform.localPosition = new Vector3(0f, 0f, -0.02f);
                inst.transform.localRotation = Quaternion.identity;
                FitNoteToRing(inst.transform);
                spawned.Add(inst);

                revealed = inst.transform;
                revealedRenderers = CacheRenderers(revealed, out float[] revealedBase);
                revealedBaseAlpha = revealedBase;
            }
        }

        revealCountdown = 0.9f;
        ShowJudge("命中");
    }

    private float revealCountdown;
    private SpriteRenderer[] revealedRenderers;
    private float[] revealedBaseAlpha;

    private void LateUpdate()
    {
        if (revealed == null)
        {
            return;
        }

        revealCountdown = Mathf.Max(0f, revealCountdown - Time.deltaTime);
        float k = revealCountdown / 0.9f;
        ApplyAlpha(revealedRenderers, revealedBaseAlpha, k);
    }

    private void Finish()
    {
        running = false;

        int total = pattern == LijiangEchoPatternIntro.Pattern.Snake
            ? 1
            : Mathf.Max(1, hits + misses);

        Action<int, int> callback = onFinished;
        onFinished = null;
        callback?.Invoke(hits, total);
    }

    /// <summary>写上方那行(判定/提示)。下方的纹样名建好之后就不动了。</summary>
    private void ShowJudge(string text)
    {
        if (judgeText != null && judgeText.text != text)
        {
            judgeText.text = text;
        }
    }

    // ————————————————————————————— 输入 —————————————————————————————

    /// <summary>每帧采一次手部输入,并记下两只手各自最近一次按下的时刻。
    ///
    /// ⚠️ 必须每帧都采,不能等到"音符进了判定窗口"才采 —— 那是鸟纹判不出来的根因:
    ///   · 按下的边沿检测(previousLeftPressed)只在窗口内更新,窗口外按的一律看不见;
    ///     手要是在窗口打开前就按住了,整个窗口都检测不到"按下"这个动作
    ///   · leftPressedAt / rightPressedAt 只在窗口内记,先按的那只手根本没被记下来,
    ///     "两只手同时"这个条件就永远凑不齐 —— 鱼纹只要一只手所以没暴露,鸟纹要两只就废了
    ///   · 一帧里如果有多个音符都在窗口内,第一个音符会把"按下"的边沿吃掉</summary>
    private void SampleHands()
    {
        handsPressedThisFrame = HandsPressedThisFrame();

        float now = Time.time;
        if ((handsPressedThisFrame & Hand.Left) != Hand.None) { leftPressedAt = now; }
        if ((handsPressedThisFrame & Hand.Right) != Hand.None) { rightPressedAt = now; }
    }

    /// <summary>这一帧有哪只手「按下」了。规则对齐战斗:
    /// 手柄扳机/握把/面键各算各的手;PC 上鼠标左键=右手、Shift+左键=左手、空格/回车=双手。</summary>
    private Hand HandsPressedThisFrame()
    {
        Hand hands = Hand.None;

        bool leftPressed = ReadPressed(XRNode.LeftHand, OVRInput.Controller.LTouch, OVRInput.Button.Three);
        bool rightPressed = ReadPressed(XRNode.RightHand, OVRInput.Controller.RTouch, OVRInput.Button.One);

        if (leftPressed && !previousLeftPressed) { hands |= Hand.Left; }
        if (rightPressed && !previousRightPressed) { hands |= Hand.Right; }

        previousLeftPressed = leftPressed;
        previousRightPressed = rightPressed;

        if (Keyboard.current != null &&
            (Keyboard.current.spaceKey.wasPressedThisFrame ||
             Keyboard.current.enterKey.wasPressedThisFrame ||
             Keyboard.current.numpadEnterKey.wasPressedThisFrame))
        {
            hands |= Hand.Both;   // 键盘 = 双手齐按,无头显时方便测双手音符
        }

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
        {
            hands |= MousePointerHand();

            // 鼠标一次只能代表一只手,所以「双手同时」在电脑上本来是打不出来的。
            // 这个音符名字就叫双击 —— 那就让真的双击(同步窗口内连点两下)顶上。
            float now = Time.time;
            if (now - lastMouseClickAt <= twoHandSyncWindow)
            {
                hands |= Hand.Both;
                lastMouseClickAt = -99f;   // 一次双击只算一次,别让第三下又凑成一对
            }
            else
            {
                lastMouseClickAt = now;
            }
        }

        return hands;
    }

    private float lastMouseClickAt = -99f;

    /// <summary>这一帧有没有手「按住」(长按用)。</summary>
    private Hand HandsHeld()
    {
        Hand hands = Hand.None;

        if (ReadPressed(XRNode.LeftHand, OVRInput.Controller.LTouch, OVRInput.Button.Three)) { hands |= Hand.Left; }
        if (ReadPressed(XRNode.RightHand, OVRInput.Controller.RTouch, OVRInput.Button.One)) { hands |= Hand.Right; }

        if (Keyboard.current != null &&
            (Keyboard.current.spaceKey.isPressed || Keyboard.current.enterKey.isPressed))
        {
            hands |= Hand.Both;
        }

        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            hands |= MousePointerHand();
        }

        return hands;
    }

    /// <summary>PC 兜底的手别映射:鼠标左键 = 右手,Shift + 左键 = 左手。
    /// 和战斗、描绘完全一致,三处规则不能打架。</summary>
    private static Hand MousePointerHand()
    {
        bool toLeft = Keyboard.current != null &&
                      (Keyboard.current.leftShiftKey.isPressed || Keyboard.current.rightShiftKey.isPressed);
        return toLeft ? Hand.Left : Hand.Right;
    }

    private static bool ReadPressed(XRNode node, OVRInput.Controller ovrController, OVRInput.Button faceButton)
    {
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(node);
        if (device.isValid)
        {
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool trigger) && trigger) { return true; }
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.gripButton, out bool grip) && grip) { return true; }
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool primary) && primary) { return true; }
        }

        return OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, ovrController)
               || OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, ovrController)
               || OVRInput.Get(faceButton, ovrController);
    }

    /// <summary>蛙纹的「上挑」。手柄速度够快且向上分量够大就算;PC 上用 ↑ 键或按住左键往上拖。</summary>
    private bool SwipedUpThisFrame()
    {
        if (Keyboard.current != null && Keyboard.current.upArrowKey.wasPressedThisFrame)
        {
            return true;
        }

        if (Mouse.current != null && Mouse.current.leftButton.isPressed)
        {
            Vector2 delta = Mouse.current.delta.ReadValue();
            if (delta.y > 6f && delta.sqrMagnitude >= 64f)
            {
                return true;
            }
        }

        if (!motionReady)
        {
            return false;
        }

        return IsUpwardSwing(leftVelocity) || IsUpwardSwing(rightVelocity);
    }

    private bool IsUpwardSwing(Vector3 velocity)
    {
        return velocity.magnitude >= swipeMinimumSpeed && velocity.y >= swipeUpwardSpeed;
    }

    /// <summary>采手柄速度。锚点名和战斗里用的是同一批,场景里没有就退化成只能用键鼠。</summary>
    private void SampleControllers()
    {
        if (leftAnchor == null)
        {
            GameObject found = GameObject.Find("LeftControllerAnchor");
            leftAnchor = found != null ? found.transform : null;
        }

        if (rightAnchor == null)
        {
            GameObject found = GameObject.Find("RightControllerAnchor");
            rightAnchor = found != null ? found.transform : null;
        }

        if (leftAnchor == null || rightAnchor == null || Time.deltaTime <= 0.0001f)
        {
            motionReady = false;
            return;
        }

        Vector3 leftPos = leftAnchor.position;
        Vector3 rightPos = rightAnchor.position;
        if (motionReady)
        {
            float inverseDelta = 1f / Time.deltaTime;
            leftVelocity = (leftPos - lastLeftPos) * inverseDelta;
            rightVelocity = (rightPos - lastRightPos) * inverseDelta;
        }

        lastLeftPos = leftPos;
        lastRightPos = rightPos;
        motionReady = true;
    }

    // ————————————————————————————— 文案与素材 —————————————————————————————

    private static string HintFor(LijiangEchoPatternIntro.Pattern pattern)
    {
        switch (pattern)
        {
            case LijiangEchoPatternIntro.Pattern.Fish: return "单击 · 左边用左手,右边用右手";
            case LijiangEchoPatternIntro.Pattern.Snake: return "按住不放";
            case LijiangEchoPatternIntro.Pattern.Frog: return "向上挥";
            default: return "双击 · 两只手同时(电脑上:空格,或连点两下)";
        }
    }

    private static string HitSfx(LijiangEchoPatternIntro.Pattern pattern)
    {
        switch (pattern)
        {
            case LijiangEchoPatternIntro.Pattern.Snake: return "snake";
            case LijiangEchoPatternIntro.Pattern.Frog: return "swipe";
            case LijiangEchoPatternIntro.Pattern.Bird: return "birds";
            default: return "water";
        }
    }
}
