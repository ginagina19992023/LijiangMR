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

    [Header("蛇:缠绕")]
    [SerializeField] private float snakeApproachRatio = 0.35f; // 前 35% 时间用来"游过来"
    [SerializeField] private float snakeCoilRadius = 0.72f;

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
        switch (pattern)
        {
            case Pattern.Bird: BuildBird(); break;
            case Pattern.Fish: BuildFish(); break;
            case Pattern.Snake: BuildSnake(); break;
            default: BuildFrog(); break;
        }

        running = true;
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
        snake = null;
        lastFrogCallAt = -1f;
        spawned.Clear();

        if (root != null)
        {
            Destroy(root.gameObject);
            root = null;
        }
    }

    private void Update()
    {
        if (!running || root == null)
        {
            return;
        }

        timer += Time.deltaTime;
        float t = Mathf.Clamp01(timer / Mathf.Max(0.01f, duration));

        UpdateRing(t);
        switch (pattern)
        {
            case Pattern.Bird: UpdateBird(t); break;
            case Pattern.Fish: UpdateFish(t); break;
            case Pattern.Snake: UpdateSnake(t); break;
            default: UpdateFrog(t); break;
        }

        if (timer >= duration)
        {
            Action callback = onComplete;
            running = false;
            callback?.Invoke();
        }
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
        LijiangEchoStageKit.PlaySfx("birds", 0.5f);

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

    private void BuildFish()
    {
        LijiangEchoStageKit.PlaySfx("water", 0.45f);

        // 从光圈外面跃起、跳进圈里的
        for (int i = 0; i < fishLeapCount; i++)
        {
            float side = i % 2 == 0 ? -1f : 1f;
            float spread = 0.75f + i * 0.18f;
            Transform fish = AddCreature(FishArt, "入场鱼_跃入_" + i, fishSize, 30 + i);

            actors.Add(new Actor
            {
                Tr = fish,
                Sr = fish.GetComponentInChildren<SpriteRenderer>(true),
                BaseScale = 1f,
                From = new Vector3(side * spread, -0.45f, 0.12f),   // 圈外、偏下
                To = Vector3.zero,                                   // 跳进圈心
                StartAt = 0.12f + i * 0.18f,
                Span = 0.42f,
                Peeking = false
            });
        }

        // 在圈旁探头的:头旁边有一圈更小的白色涟漪
        for (int i = 0; i < fishPeekCount; i++)
        {
            float side = i % 2 == 0 ? 1f : -1f;
            Vector3 spot = new Vector3(side * 0.62f, -0.10f - i * 0.12f, 0.08f);

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

            if (!a.Peeking)
            {
                float p = Mathf.Clamp01((t - a.StartAt) / a.Span);
                if (p <= 0f)
                {
                    SetAlpha(a.Tr, 0f);
                    continue;
                }

                // 抛物线:水平匀速,竖直先上后下,落进圈心
                Vector3 pos = LeapPoint(a.From, a.To, p, fishLeapHeight);
                a.Tr.localPosition = pos;

                // 头朝抛物线的切线方向:起跳时朝上、落下时朝下,而不是一直平着
                float s = a.BaseScale * Mathf.Lerp(1f, 0.55f, p);   // 入水时缩小,像沉进去
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
                // 跃入圈心
                Vector3 start = a.From + new Vector3(0f, 0.18f, 0f);
                float h = fishLeapHeight * 0.7f;
                a.Tr.localPosition = LeapPoint(start, Vector3.zero, leave, h);

                float s = a.BaseScale * Mathf.Lerp(1f, 0.5f, leave);
                FaceAlong(a.Tr, LeapTangent(start, Vector3.zero, leave, h), s, fishFacingDegrees, fishMirrorWhenLeft);
                SetAlpha(a.Tr, Mathf.Clamp01(1f - Mathf.Pow(leave, 3f)));
                SetAlpha(a.Ripple, 0f);
            }
        }
    }

    // ————————————————————————————— 蛇:缠绕 —————————————————————————————

    private Transform snake;

    private void BuildSnake()
    {
        // 需求:先响「嘶嘶」声,音效先于画面
        LijiangEchoStageKit.PlaySfx("snake", 0.7f);

        // 反馈:「感觉你没有按照节来切而是给他切成千层了」。
        // 说得对 —— 之前这里根本没切,是把【整条蛇】复制了 7 份首尾排开,
        // 于是屏幕上就是七条一模一样的蛇叠成千层。transition/snake 本身就已经是
        // 一条完整的蛇(头在左、身子拱起、尾巴收细),不需要拼节,一条就够。
        snake = AddCreature(SnakeArt, "入场蛇", snakeHeadSize, 34);
    }

    /// <summary>蛇:整条从右边扭动着游过来 → 缠上光圈 → 顺时针沿圈转一圈 → 消失。
    ///
    /// 只有一条蛇,不再拼节。「扭动」用整体的正弦摆动 + 轻微的摇头来表现。</summary>
    private void UpdateSnake(float t)
    {
        if (snake == null)
        {
            return;
        }

        float approach = Mathf.Clamp01(t / Mathf.Max(0.01f, snakeApproachRatio));
        float coil = Mathf.Clamp01((t - snakeApproachRatio) / Mathf.Max(0.01f, 1f - snakeApproachRatio));

        Vector3 pos;
        float faceDegrees;

        // 缠绕从光圈【底部】起步,不是右侧:底部的顺时针切线正好是"朝左",
        // 和游过来的方向接得上,不会在切换的那一帧突然扭 90°。
        const float coilStartAngle = -Mathf.PI * 0.5f;

        if (coil <= 0f)
        {
            // 从右下方圈外游过来,上下正弦摆动 = 扭动的身子
            Vector3 from = new Vector3(1.55f, -snakeCoilRadius - 0.16f, 0.1f);
            Vector3 to = new Vector3(0f, -snakeCoilRadius, 0f);
            pos = Vector3.Lerp(from, to, Mathf.SmoothStep(0f, 1f, approach));
            pos.y += Mathf.Sin(timer * 6f) * 0.09f;

            // 朝左游,摆动时头跟着上下点一点
            faceDegrees = 180f + Mathf.Sin(timer * 6f) * 12f - snakeFacingDegrees;
        }
        else
        {
            // 顺时针沿光圈转一圈:角度递减(Unity 里 y 向上,递减即顺时针)
            float angle = coilStartAngle - coil * Mathf.PI * 2f;
            pos = new Vector3(
                Mathf.Cos(angle) * snakeCoilRadius,
                Mathf.Sin(angle) * snakeCoilRadius,
                Mathf.Sin(angle * 2f) * 0.06f);   // 缠绕感:一半在圈前一半在圈后

            // 顺时针绕行的切线 = 半径方向再转 -90°
            faceDegrees = angle * Mathf.Rad2Deg - 90f - snakeFacingDegrees;
        }

        snake.localPosition = pos;
        snake.localRotation = Quaternion.Euler(0f, 0f, faceDegrees);
        snake.localScale = Vector3.one;

        float visible = coil <= 0f ? approach : 1f;
        SetAlpha(snake, visible * FadeOutTail(t));
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
        Vector3 padLeftPos = new Vector3(-0.95f, -0.22f, 0.10f);
        Vector3 onPadLeft = new Vector3(-0.95f, -0.05f, 0.10f);
        Vector3 center = Vector3.zero;
        Vector3 padNextPos = new Vector3(0.98f, -0.05f, -0.06f);
        Vector3 offScreen = new Vector3(2.2f, 0.55f, -0.35f);

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
            Vector3 from = padLeftPos + new Vector3(-0.25f, -0.95f, 0f);
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
        LijiangEchoStageKit.PlaySfx("swipe", 0.42f);   // 暂用挥划音;有蛙叫素材后换掉
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

    /// <summary>抛物线跳跃上的一点:水平匀速,竖直叠一个 sin 拱形。</summary>
    private static Vector3 LeapPoint(Vector3 from, Vector3 to, float p, float height)
    {
        Vector3 pos = Vector3.Lerp(from, to, p);
        pos.y += Mathf.Sin(Mathf.Clamp01(p) * Mathf.PI) * height;
        return pos;
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

        Color c = sr.color;
        c.a = Mathf.Clamp01(alpha);
        sr.color = c;
    }
}
