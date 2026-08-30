using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 选关阶段场景（Stage_Select）的控制器。选关做成「三槽轮换」的无缝循环滚轮：
///   · 三张满屏关卡背景图(蛙/鸟/鱼)按与中心的距离交叉淡入,中心那关最亮;
///   · 下方一排数字 token 就是滚轮上的三个「槽」,随滚动量 scroll 连续左右移动;
///   · 首尾相接循环——鱼纹再往右滑就接回蛙纹,滑过边缝时 token 已淡出,看不出接缝;
///   · 交互:摁住扳机(或鼠标)左右拖滑连续滚动,松手吸附到最近一关;轻点/按 A/空格/回车 = 确认中心关;
///     推杆/方向键 = 步进一关;键盘 1/2/3 = 直选。
/// 满屏大图只做 alpha 交叉淡入、不做位移缩放,避免 VR 里畸变。确认后经 LijiangEchoGameFlow 进旧版流程。
/// </summary>
public class SelectStageController : MonoBehaviour
{
    private const int LevelCount = 3;
    private const float SlotSpacing = 1.33f;   // 相邻槽的水平间距(与旧版 1/2/3 位置一致)
    private const float TokenY = -0.46f;
    private const float TokenZ = -0.18f;

    private static readonly string[] LevelNames = { "蛙纹", "鸟纹", "鱼纹" };

    private static readonly string[] LevelCardPaths =
    {
        "select/frog_card",
        "select/bird_card",
        "select/fish_card"
    };

    private static readonly string[] LevelSymbolPaths =
    {
        "select/frog_symbol",
        "select/bird_symbol",
        "select/fish_symbol"
    };

    private static readonly string[] LevelNumberPaths =
    {
        "ui/number_1",
        "ui/number_2",
        "ui/number_3"
    };

    private Transform stageRoot;
    private readonly List<GameObject> spawnedObjects = new List<GameObject>();
    private readonly List<LijiangEchoStageKit.MotionItem> motionItems = new List<LijiangEchoStageKit.MotionItem>();

    private SpriteRenderer[] selectCards;
    private SpriteRenderer[] selectSymbols;
    private SpriteRenderer[] selectNumbers;
    private Transform[] numberTokens;
    private Vector3[] numberTokenBaseScale;

    private int selectedLevel;
    private bool confirmed;

    // —— 滚轮状态 ——
    private float scroll;         // 连续滚动量(单位:关卡);Mod(round(scroll),3) 即中心关
    private float scrollTarget;   // 松手/步进后要缓动吸附到的整数目标
    private float stepCooldown;
    private bool pressActive;     // 本次按住是否已开始
    private bool dragging;        // 本次按住是否已判定为「拖动」(而非轻点)
    private float lastPointerX;
    private float dragDistance;
    private int lastCenteredLevel = -1;

    private IEnumerator Start()
    {
        while (LijiangEchoGameFlow.Instance == null)
        {
            yield return null;
        }

        stageRoot = LijiangEchoStageKit.PrepareStageRoot("漓江回声_选关舞台");
        BuildSelectScreen();
    }

    private void Update()
    {
        if (stageRoot == null || confirmed)
        {
            return;
        }

        float dt = Time.deltaTime;
        stepCooldown -= dt;
        LijiangEchoStageKit.UpdateControllerInput(stageRoot);
        LijiangEchoStageKit.UpdateMotions(motionItems);

        // —— 摁住左右拖滑 ——
        bool hasPointer = LijiangEchoStageKit.TryGetActivePointer(stageRoot, out Vector3 pointer, out bool held);
        if (hasPointer && held)
        {
            if (!pressActive)
            {
                pressActive = true;
                dragging = false;
                dragDistance = 0f;
                lastPointerX = pointer.x;
            }
            else
            {
                float dx = pointer.x - lastPointerX;
                dragDistance += Mathf.Abs(dx);
                if (dragDistance > 0.06f)
                {
                    dragging = true;
                }

                if (dragging)
                {
                    scroll -= dx / SlotSpacing; // 指针右移 → 内容右移 → scroll 减小
                }

                lastPointerX = pointer.x;
            }
        }
        else
        {
            if (pressActive)
            {
                if (!dragging)
                {
                    Confirm(); // 轻点(按下即松、没拖动)= 确认中心关
                    return;
                }

                scrollTarget = Mathf.Round(scroll); // 拖动结束 → 吸附到最近一关
            }

            pressActive = false;
            dragging = false;
            scroll = Mathf.Lerp(scroll, scrollTarget, 1f - Mathf.Exp(-14f * dt)); // 非拖动时缓动吸附
        }

        // —— 推杆 / 方向键:步进一关(可越界循环) ——
        int dir = LijiangEchoStageKit.ReadHorizontalStep();
        if (dir != 0 && stepCooldown <= 0f)
        {
            scrollTarget = Mathf.Round(scrollTarget) + dir;
            stepCooldown = 0.22f;
        }

        // —— 键盘 1/2/3:直选,并走最近方向(保持循环观感) ——
        if (Keyboard.current != null)
        {
            int digit = Keyboard.current.digit1Key.wasPressedThisFrame ? 0
                : Keyboard.current.digit2Key.wasPressedThisFrame ? 1
                : Keyboard.current.digit3Key.wasPressedThisFrame ? 2 : -1;
            if (digit >= 0)
            {
                int cur = Mod(Mathf.RoundToInt(scroll), LevelCount);
                int diff = Mod(digit - cur + 1, LevelCount) - 1; // -1 / 0 / +1
                scrollTarget = Mathf.Round(scroll) + diff;
            }
        }

        // —— A键 / 空格 / 回车 / 鼠标(非拖动时):确认中心关 ——
        if (!pressActive && LijiangEchoStageKit.NonPointerConfirmPressed())
        {
            Confirm();
            return;
        }

        // 中心关变化 → 更新选中 + 轻音效
        int centered = Mod(Mathf.RoundToInt(scroll), LevelCount);
        if (centered != lastCenteredLevel)
        {
            lastCenteredLevel = centered;
            LijiangEchoStageKit.PlaySfx("swipe", 0.3f);
        }

        selectedLevel = centered;
        LayoutWheel();
    }

    private void Confirm()
    {
        confirmed = true;
        selectedLevel = Mod(Mathf.RoundToInt(scroll), LevelCount);
        LijiangEchoStageKit.PlaySfx("button", 0.62f);
        LijiangEchoGameFlow.Instance.EnterLegacyFlow(selectedLevel);
    }

    private void BuildSelectScreen()
    {
        LijiangEchoStageKit.PlayStageLoop("ambience", 0.34f);

        AddLayer("select/select_frame", "选关紫色暗幕", Vector3.zero, LijiangEchoStageKit.MainCanvasWidth, -18, 0.025f);
        AddLayer("select/select_line", "选关连接线", new Vector3(0f, -0.02f, -0.03f), LijiangEchoStageKit.WideStripWidth, -6, 0.92f);
        AddLayer("select/select_edge", "选关两侧色块", new Vector3(0f, -0.02f, -0.04f), LijiangEchoStageKit.WideStripWidth, -5, 0.72f);

        selectCards = new SpriteRenderer[LevelCount];
        selectSymbols = new SpriteRenderer[LevelCount];
        selectNumbers = new SpriteRenderer[LevelCount];
        numberTokens = new Transform[LevelCount];
        numberTokenBaseScale = new Vector3[LevelCount];

        // 满屏关卡背景 + 纹样:都摆在 x=0,靠 alpha 交叉淡入(不做位移/缩放,VR 安全)。
        for (int i = 0; i < LevelCount; i++)
        {
            GameObject card = AddLayer(LevelCardPaths[i], "选关卡片_" + LevelNames[i], new Vector3(0f, -0.02f, -0.08f - i * 0.01f), LijiangEchoStageKit.WideStripWidth, 2 + i);
            selectCards[i] = card.GetComponent<SpriteRenderer>();

            GameObject symbol = AddLayer(LevelSymbolPaths[i], "选关纹样_" + LevelNames[i], new Vector3(0f, -0.02f, -0.13f - i * 0.01f), LijiangEchoStageKit.WideStripWidth, 8 + i, 0.92f);
            selectSymbols[i] = symbol.GetComponent<SpriteRenderer>();
            RegisterMotion(symbol, LijiangEchoStageKit.MotionKind.FloatY, 0.018f, 1.6f, i * 1.3f);
        }

        AddLayer("select/bird_left_symbol", "左侧鸟纹装饰", new Vector3(0f, -0.02f, -0.16f), LijiangEchoStageKit.WideStripWidth, 13, 0.78f);
        AddLayer("select/frog_right_symbol", "右侧蛙纹装饰", new Vector3(0f, -0.02f, -0.17f), LijiangEchoStageKit.WideStripWidth, 13, 0.78f);
        AddLayer("select/bird_left_card", "左侧鸟纹白底卡", new Vector3(0f, -0.02f, -0.18f), LijiangEchoStageKit.WideStripWidth, 14, 0.82f);
        AddLayer("select/frog_right_card", "右侧蛙纹白底卡", new Vector3(0f, -0.02f, -0.19f), LijiangEchoStageKit.WideStripWidth, 14, 0.82f);
        AddLayer("select/select_border", "选关外框", new Vector3(0f, -0.02f, -0.2f), LijiangEchoStageKit.WideStripWidth, 20, 0.92f);
        AddIcon("ui/settings", "左上设置入口", new Vector3(-2.42f, 1.05f, -0.25f), 0.24f, 30, 0.88f);

        // 滚轮上的数字 token:随 scroll 连续在三个槽间移动、循环。
        for (int i = 0; i < LevelCount; i++)
        {
            GameObject number = AddIcon(LevelNumberPaths[i], "关卡数字_" + (i + 1), new Vector3(0f, TokenY, TokenZ), 0.2f, 24);
            numberTokens[i] = number.transform;
            numberTokenBaseScale[i] = number.transform.localScale;
            selectNumbers[i] = number.GetComponent<SpriteRenderer>();
        }

        LayoutWheel();
    }

    private void LayoutWheel()
    {
        if (selectCards == null)
        {
            return;
        }

        for (int i = 0; i < LevelCount; i++)
        {
            // v ∈ (-1.5, 1.5]:关卡 i 相对中心的「槽偏移」,首尾相接(循环)。
            float v = Mathf.Repeat((i - scroll) + 1.5f, LevelCount) - 1.5f;
            float av = Mathf.Abs(v);
            float focus = 1f - Mathf.Clamp01(av);                 // 1=正中心, 0=一个槽以外
            float edge = Mathf.InverseLerp(1.5f, 1.05f, av);      // 接近边缝(±1.5)时淡出,循环无缝

            // 满屏背景 + 纹样:交叉淡入(中心关最亮)
            float cardAlpha = Mathf.Lerp(0.26f, 1f, focus);
            SetAlpha(selectCards[i], cardAlpha);
            if (selectSymbols != null && i < selectSymbols.Length)
            {
                SetAlpha(selectSymbols[i], cardAlpha * 0.95f);
            }

            // 数字 token:随 v 连续平移 + 中心放大 + 边缝淡出
            if (numberTokens != null && numberTokens[i] != null)
            {
                numberTokens[i].localPosition = new Vector3(v * SlotSpacing, TokenY, TokenZ);
                numberTokens[i].localScale = numberTokenBaseScale[i] * Mathf.Lerp(0.72f, 1.18f, focus);
            }

            if (selectNumbers != null && i < selectNumbers.Length)
            {
                SetAlpha(selectNumbers[i], Mathf.Lerp(0.4f, 1f, focus) * edge);
            }
        }
    }

    private static void SetAlpha(SpriteRenderer renderer, float alpha)
    {
        if (renderer == null)
        {
            return;
        }

        Color c = renderer.color;
        c.a = alpha;
        renderer.color = c;
    }

    private static int Mod(int value, int modulus)
    {
        int r = value % modulus;
        return r < 0 ? r + modulus : r;
    }

    private GameObject AddLayer(string resourcePath, string objectName, Vector3 localPosition, float targetWidth, int order, float alpha = 1f)
    {
        return LijiangEchoStageKit.AddLayer(stageRoot, spawnedObjects, resourcePath, objectName, localPosition, targetWidth, order, alpha);
    }

    private GameObject AddIcon(string resourcePath, string objectName, Vector3 visibleCenter, float targetHeight, int order, float alpha = 1f)
    {
        return LijiangEchoStageKit.AddIcon(stageRoot, spawnedObjects, resourcePath, objectName, visibleCenter, targetHeight, order, alpha);
    }

    private void RegisterMotion(GameObject item, LijiangEchoStageKit.MotionKind kind, float amplitude, float speed, float phase)
    {
        LijiangEchoStageKit.RegisterMotion(motionItems, item, kind, amplitude, speed, phase);
    }
}
