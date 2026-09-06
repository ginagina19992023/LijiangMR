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
///   蛇纹 · 长按 —— 按住不放,光圈从紫渐变到金,金了即满
///   蛙纹 · 挥划 —— 从下方蓄势升到圆心,过判定点后抛物线跃出;要向上挥
///   鸟纹 · 双击 —— 左右各飞来一只,圆心叠合;两只手要同时打
///
/// 【为什么不直接调战斗那套】
/// 战斗的判定锁在 LijiangEchoGameController 那 5800 行里,拆出来是
/// docs/REFACTOR-STEP2-BATTLE-SPLIT.md 的活,还没做。这里按同样的规则单独实现一份,
/// 手别映射、挥划阈值、双手同时的时间窗都对齐战斗的取值,拆分完成后可以整体换掉。
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
    private const string FishArt = "select/fish_symbol";
    private const string SnakeArt = "transition/snake";
    private const string FrogArt = "select/frog_symbol";
    private const string BirdArt = "start/bird_big";

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

    [SerializeField] private Color holdFromColor = new Color(0.62f, 0.36f, 0.85f);   // 紫
    [SerializeField] private Color holdToColor = new Color(1f, 0.82f, 0.25f);        // 金

    [Header("挥划判定")]
    [Tooltip("手柄挥动速度阈值(米/秒)。对齐战斗的 swipeMinimumSpeed。")]
    [SerializeField] private float swipeMinimumSpeed = 0.34f;

    [Tooltip("其中向上的分量至少要多少。蛙纹的标准动作是【上挑】。")]
    [SerializeField] private float swipeUpwardSpeed = 0.30f;

    [Header("双手同时")]
    [Tooltip("两只手先后按下,间隔在这个时间内都算「同时」。对齐战斗的 twoHandSyncWindow。")]
    [SerializeField] private float twoHandSyncWindow = 0.35f;

    [Header("外观")]
    [SerializeField] private float ringSize = 0.62f;
    [SerializeField] private float noteSize = 0.90f;
    [SerializeField] private float spawnDistance = 1.5f;   // 音符从多远飞来
    [SerializeField] private float hintTextSize = 0.05f;

    // ——— 运行时 ———
    private Transform root;
    private readonly List<GameObject> spawned = new List<GameObject>();
    private readonly List<Note> notes = new List<Note>();
    private Transform ring;
    private SpriteRenderer ringRenderer;
    private TextMesh hint;
    private Transform revealed;          // 命中后浮现在圆心的纹样

    private LijiangEchoPatternIntro.Pattern pattern;
    private bool running;
    private float timer;
    private int hits;
    private int misses;
    private Action<int, int> onFinished;   // (命中数, 总数)

    // 长按
    private float holdProgress;

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

    private sealed class Note
    {
        public Transform Tr;
        public Vector3 From;
        public float ArriveAt;      // 抵达光圈的时刻(秒)
        public Hand RequiredHand;   // 该用哪只手(鱼纹左右分边)
        public bool Resolved;
        public bool Hit;
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
        ring = null;
        ringRenderer = null;
        hint = null;
        revealed = null;
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

    private void BuildHint()
    {
        hint = LijiangEchoStageKit.AddText(
            root, spawned, HintFor(pattern), new Vector3(0f, -ringSize * 1.15f, 0f),
            hintTextSize, Color.white, 40);
    }

    private void BuildNotes()
    {
        if (pattern == LijiangEchoPatternIntro.Pattern.Snake)
        {
            // 长按只有一个「音符」—— 就是光圈本身,不另外飞东西过来
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
                    SpawnNote(FishArt, "鱼纹音符_" + i,
                        new Vector3(fromLeft ? -spawnDistance : spawnDistance, 0.1f, 0.25f),
                        arriveAt, fromLeft ? Hand.Left : Hand.Right, 30 + i);
                    break;
                }

                case LijiangEchoPatternIntro.Pattern.Frog:
                {
                    // 从下方蓄势升上来
                    SpawnNote(FrogArt, "蛙纹音符_" + i,
                        new Vector3(0f, -spawnDistance, 0.2f),
                        arriveAt, Hand.None, 30 + i);
                    break;
                }

                default:
                {
                    // 鸟纹:左右各飞来一只,在圆心叠合 —— 所以要两只手同时打
                    SpawnNote(BirdArt, "鸟纹音符_左_" + i,
                        new Vector3(-spawnDistance, 0.35f, 0.2f), arriveAt, Hand.Both, 30 + i * 2);
                    SpawnNote(BirdArt, "鸟纹音符_右_" + i,
                        new Vector3(spawnDistance, 0.35f, 0.2f), arriveAt, Hand.None, 31 + i * 2);
                    break;
                }
            }
        }
    }

    private void SpawnNote(string art, string objectName, Vector3 from, float arriveAt, Hand hand, int order)
    {
        // 走和入场动画同一套「图案落在支点上」的生成方式,否则一缩放就偏出去
        Transform note = LijiangEchoPatternIntro.AddCenteredSprite(
            root, spawned, art, objectName, noteSize, order);
        note.localPosition = from;

        // 只有带 RequiredHand 的那一个参与判定;鸟纹右边那只只是陪着飞
        if (hand != Hand.None || pattern != LijiangEchoPatternIntro.Pattern.Bird)
        {
            notes.Add(new Note { Tr = note, From = from, ArriveAt = arriveAt, RequiredHand = hand });
        }
        else
        {
            notes.Add(new Note { Tr = note, From = from, ArriveAt = arriveAt, RequiredHand = Hand.None, Resolved = true });
        }
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

            // 越近越大一点,凑到判定点时最清楚
            float scale = Mathf.Lerp(0.65f, 1f, p);
            note.Tr.localScale = Vector3.one * scale;

            if (note.Resolved)
            {
                // 已判定的:命中往圆心缩、失误的继续飘走并淡出
                float after = Mathf.Clamp01((timer - note.ArriveAt) / 0.45f);
                LijiangEchoPatternIntro.SetAlpha(note.Tr, (1f - after) * (note.Hit ? 1f : 0.4f));
                if (note.Hit)
                {
                    note.Tr.localScale = Vector3.one * Mathf.Lerp(scale, scale * 1.5f, after);
                }

                continue;
            }

            LijiangEchoPatternIntro.SetAlpha(note.Tr, Mathf.Clamp01(p * 2.2f));

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
                ShowHint(HintFor(pattern) + "\n漏了");
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
                Hand pressed = HandsPressedThisFrame();
                return (pressed & note.RequiredHand) != Hand.None;
            }

            case LijiangEchoPatternIntro.Pattern.Frog:
            {
                // 挥划:向上挑
                return SwipedUpThisFrame();
            }

            default:
            {
                // 鸟纹:两只手要同时。允许一前一后,只要间隔在同步窗口内
                Hand pressed = HandsPressedThisFrame();
                float now = Time.time;
                if ((pressed & Hand.Left) != Hand.None) { leftPressedAt = now; }
                if ((pressed & Hand.Right) != Hand.None) { rightPressedAt = now; }

                bool bothRecent = Mathf.Abs(leftPressedAt - rightPressedAt) <= twoHandSyncWindow
                                  && now - Mathf.Min(leftPressedAt, rightPressedAt) <= twoHandSyncWindow;
                if (bothRecent && pressed != Hand.None)
                {
                    leftPressedAt = -99f;
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

        // 光圈从紫渐变到金 —— 队友说"之前就是这样的"
        if (ringRenderer != null)
        {
            Color c = Color.Lerp(holdFromColor, holdToColor, holdProgress);
            c.a = ringRenderer.color.a;
            ringRenderer.color = c;

            float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.03f * holdProgress;
            ring.localScale = Vector3.one * (Mathf.Lerp(0.9f, 1.12f, holdProgress) * pulse);
        }

        ShowHint(holdProgress >= 1f
            ? "蛇纹 · 满了"
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

        // 需求:命中后光圈中央浮现对应纹样
        if (revealed == null)
        {
            revealed = LijiangEchoPatternIntro.AddCenteredSprite(
                root, spawned, ArtFor(pattern), "命中纹样", ringSize * 0.72f, 45);
            revealed.localPosition = new Vector3(0f, 0f, -0.02f);
        }

        revealCountdown = 0.9f;
        ShowHint(LijiangEchoQrScan.PatternName(pattern) + " · 命中");
    }

    private float revealCountdown;

    private void LateUpdate()
    {
        if (revealed == null)
        {
            return;
        }

        revealCountdown = Mathf.Max(0f, revealCountdown - Time.deltaTime);
        float k = revealCountdown / 0.9f;
        LijiangEchoPatternIntro.SetAlpha(revealed, k);
        revealed.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, k);
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

    private void ShowHint(string text)
    {
        if (hint != null && hint.text != text)
        {
            hint.text = text;
        }
    }

    // ————————————————————————————— 输入 —————————————————————————————

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
        }

        return hands;
    }

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
            default: return "双击 · 两只手同时";
        }
    }

    private static string ArtFor(LijiangEchoPatternIntro.Pattern pattern)
    {
        switch (pattern)
        {
            case LijiangEchoPatternIntro.Pattern.Fish: return FishArt;
            case LijiangEchoPatternIntro.Pattern.Snake: return SnakeArt;
            case LijiangEchoPatternIntro.Pattern.Frog: return FrogArt;
            default: return BirdArt;
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
