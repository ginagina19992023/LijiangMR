using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 选关阶段场景（Stage_Select）的控制器。选关做成【卡片左右滑动的无缝循环轮播】:
///   · 三张关卡卡片(蛙/鸟/鱼)横向并排,摁住扳机(编辑器=鼠标)左右拖 → 卡片整排跟着左右滚;
///   · 首尾相接循环——一直往左滑,卡片持续出现,内容永远是【蛙→鸟→鱼→蛙→鸟→鱼…】这个固定顺序转圈;
///   · 中间那张放大高亮=当前选中;划到边缝时那张已淡出,看不出接头;
///   · 轻点/按 A/空格/回车 = 确认中间那张;推杆/方向键 = 步进一关;键盘 1/2/3 = 直选。
/// 每张卡片=一个「组」(卡片底图+纹样+序号),整组一起滑动/缩放/淡入淡出。确认后经 LijiangEchoGameFlow 进旧版流程。
/// </summary>
public class SelectStageController : MonoBehaviour
{
    private const int LevelCount = 3;
    private const float CardWidth = 6.05f;      // 单张卡片(及其纹样)拟合宽度——与旧版一致,基本铺满画面
    private const float CardSpacing = 4.0f;     // 卡片布局间距(比卡窄,收紧间隔;越小相邻卡靠得越近/重叠越多。想再调就改这个数)
    private const float DragUnit = 2.0f;         // 拖动灵敏度:拖约 2 个单位 = 换一张卡(和布局间距解耦,避免大卡拖起来迟钝)
    private const float GroupBaseZ = -0.12f;
    private const float NumberInCardY = -0.34f;

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

    private Transform[] cardGroups;
    private SpriteRenderer[][] groupRenderers;
    private int[][] groupBaseOrders;

    private int selectedLevel;
    private bool confirmed;

    // —— 轮播滚动状态 ——
    private float scroll;         // 连续滚动量(单位:关卡);Mod(round(scroll),3) 即中心关
    private float scrollTarget;   // 松手/步进后要缓动吸附到的整数目标
    private float stepCooldown;
    private bool pressActive;
    private bool dragging;
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
                    scroll -= dx / DragUnit; // 指针右移 → 卡片右移 → scroll 减小
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
                    Confirm(); // 轻点 = 确认中心那张
                    return;
                }

                scrollTarget = Mathf.Round(scroll); // 拖动结束 → 吸附到最近一关
            }

            pressActive = false;
            dragging = false;
            scroll = Mathf.Lerp(scroll, scrollTarget, 1f - Mathf.Exp(-14f * dt));
        }

        // —— 推杆 / 方向键:步进一关(可越界循环) ——
        int dir = LijiangEchoStageKit.ReadHorizontalStep();
        if (dir != 0 && stepCooldown <= 0f)
        {
            scrollTarget = Mathf.Round(scrollTarget) + dir;
            stepCooldown = 0.22f;
        }

        // —— 键盘 1/2/3:直选,走最近方向 ——
        if (Keyboard.current != null)
        {
            int digit = Keyboard.current.digit1Key.wasPressedThisFrame ? 0
                : Keyboard.current.digit2Key.wasPressedThisFrame ? 1
                : Keyboard.current.digit3Key.wasPressedThisFrame ? 2 : -1;
            if (digit >= 0)
            {
                int cur = Mod(Mathf.RoundToInt(scroll), LevelCount);
                int diff = Mod(digit - cur + 1, LevelCount) - 1;
                scrollTarget = Mathf.Round(scroll) + diff;
            }
        }

        // —— A键 / 空格 / 回车 / 鼠标(非拖动时):确认中心那张 ——
        if (!pressActive && LijiangEchoStageKit.NonPointerConfirmPressed())
        {
            Confirm();
            return;
        }

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

        // 静止背景 + 外框(不随卡片滚动)。外框在最前,能顺带遮住滑到边缘的卡片,循环接缝更干净。
        AddLayer("select/select_frame", "选关紫色暗幕", Vector3.zero, LijiangEchoStageKit.MainCanvasWidth, -18, 0.06f);
        AddLayer("select/select_border", "选关外框", new Vector3(0f, -0.02f, -0.2f), LijiangEchoStageKit.WideStripWidth, 60, 0.9f);
        AddIcon("ui/settings", "左上设置入口", new Vector3(-2.42f, 1.05f, -0.25f), 0.24f, 62, 0.88f);

        cardGroups = new Transform[LevelCount];
        groupRenderers = new SpriteRenderer[LevelCount][];
        groupBaseOrders = new int[LevelCount][];

        for (int i = 0; i < LevelCount; i++)
        {
            Transform group = new GameObject("关卡卡片_" + LevelNames[i]).transform;
            group.SetParent(stageRoot, false);
            group.localPosition = new Vector3(0f, -0.02f, GroupBaseZ);
            group.localRotation = Quaternion.identity;
            group.localScale = Vector3.one;
            spawnedObjects.Add(group.gameObject);
            cardGroups[i] = group;

            // 卡片底图 + 纹样 + 序号,都作为「组」的子物体,一起滑动/缩放/淡入淡出。
            LijiangEchoStageKit.AddLayer(stageRoot, spawnedObjects, LevelCardPaths[i], "卡_" + LevelNames[i], Vector3.zero, CardWidth, 2, 1f, group);
            LijiangEchoStageKit.AddLayer(stageRoot, spawnedObjects, LevelSymbolPaths[i], "纹_" + LevelNames[i], new Vector3(0f, 0.02f, -0.01f), CardWidth, 4, 1f, group);

            GameObject number = AddIcon(LevelNumberPaths[i], "号_" + (i + 1), new Vector3(0f, NumberInCardY, -0.02f), 0.16f, 6);
            number.transform.SetParent(group, false);
            number.transform.localPosition = new Vector3(0f, NumberInCardY, -0.02f);

            SpriteRenderer[] rends = group.GetComponentsInChildren<SpriteRenderer>(true);
            groupRenderers[i] = rends;
            int[] orders = new int[rends.Length];
            for (int k = 0; k < rends.Length; k++)
            {
                orders[k] = rends[k].sortingOrder;
            }
            groupBaseOrders[i] = orders;
        }

        LayoutWheel();
    }

    private void LayoutWheel()
    {
        if (cardGroups == null)
        {
            return;
        }

        for (int i = 0; i < LevelCount; i++)
        {
            // v ∈ (-1.5, 1.5]:卡片 i 相对中心的槽偏移,首尾相接(循环)。
            float v = Mathf.Repeat((i - scroll) + 1.5f, LevelCount) - 1.5f;
            float av = Mathf.Abs(v);
            float focus = 1f - Mathf.Clamp01(av);              // 1=正中心, 0=一张卡以外
            float edge = Mathf.InverseLerp(1.5f, 1.02f, av);   // 接近边缝(±1.5)淡出 → 循环无缝

            Transform g = cardGroups[i];
            g.localPosition = new Vector3(v * CardSpacing, -0.02f, GroupBaseZ - focus * 0.03f);
            g.localScale = Vector3.one * Mathf.Lerp(0.9f, 1f, focus); // 大卡:中心足尺,侧卡略缩(避免放大溢出画面)

            float alpha = edge * Mathf.Lerp(0.5f, 1f, focus);
            int lift = Mathf.RoundToInt(focus * 30f);          // 中心卡整体抬到侧卡之上
            SpriteRenderer[] rends = groupRenderers[i];
            int[] baseO = groupBaseOrders[i];
            for (int k = 0; k < rends.Length; k++)
            {
                SetAlpha(rends[k], alpha);
                rends[k].sortingOrder = baseO[k] + lift;
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
