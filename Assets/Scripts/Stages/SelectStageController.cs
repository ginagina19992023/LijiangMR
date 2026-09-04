using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 选关阶段场景（Stage_Select）的控制器，对应旧 LijiangEchoGameController 里的
/// ShowSelect/UpdateSelect/UpdateSelectedCardVisual。确认选中的关卡后，
/// 通过 LijiangEchoGameFlow 桥接进入尚未拆分的旧版流程（从过场动画开始）。
/// </summary>
public class SelectStageController : MonoBehaviour
{
    private static readonly string[] LevelNames = { "蛙纹", "鸟纹", "鱼纹" };

    // 9.1 需求第 2 条:这一版只开放蛙纹关,其余两关保持变暗并禁止进入(作为后续新增关卡的预留位)。
    // 以后开放哪一关就在这里改 true —— 数组长度必须和 LevelNames 一致。
    private static readonly bool[] LevelUnlocked = { true, false, false };

    private const float LockedCardAlpha = 0.22f;   // 未开放关卡的变暗程度(越小越暗)

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

    private static readonly Vector3[] SelectNumberPositions =
    {
        new Vector3(-1.33f, -0.46f, -0.18f),
        new Vector3(0f, -0.46f, -0.18f),
        new Vector3(1.33f, -0.46f, -0.18f)
    };

    private Transform stageRoot;
    private readonly List<GameObject> spawnedObjects = new List<GameObject>();
    private readonly List<LijiangEchoStageKit.MotionItem> motionItems = new List<LijiangEchoStageKit.MotionItem>();
    private SpriteRenderer[] selectCards;
    private SpriteRenderer[] selectNumbers;
    private int selectedLevel;
    private float selectMoveCooldown;
    private bool confirmed;

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

        selectMoveCooldown -= Time.deltaTime;
        LijiangEchoStageKit.UpdateControllerInput(stageRoot);
        LijiangEchoStageKit.UpdateMotions(motionItems);

        for (int i = 0; i < SelectNumberPositions.Length; i++)
        {
            if (!IsUnlocked(i))
            {
                continue;   // 需求第 2 条:未开放的关卡不响应指向和点击
            }

            Rect cardBounds = new Rect(SelectNumberPositions[i].x - 0.58f, -0.82f, 1.16f, 1.48f);
            if (!LijiangEchoStageKit.TryGetControllerHover(stageRoot, cardBounds, out bool pointerPressed))
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
                Confirm();
                return;
            }
        }

        // 摇杆左右:跳过未开放的关卡(只有一关开放时就是原地不动)。
        int direction = LijiangEchoStageKit.ReadHorizontalStep();
        if (direction != 0 && selectMoveCooldown <= 0f)
        {
            int next = NextUnlocked(selectedLevel, direction);
            selectMoveCooldown = 0.25f;
            if (next != selectedLevel)
            {
                selectedLevel = next;
                LijiangEchoStageKit.PlaySfx("swipe", 0.34f);
                UpdateSelectedCardVisual();
            }
        }

        if (Keyboard.current != null)
        {
            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                TrySelect(0);
            }
            else if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                TrySelect(1);
            }
            else if (Keyboard.current.digit3Key.wasPressedThisFrame)
            {
                TrySelect(2);
            }
        }

        if (LijiangEchoStageKit.NonPointerConfirmPressed())
        {
            Confirm();
        }
    }

    // ————————————— 需求第 2 条:关卡开放状态 —————————————

    private static bool IsUnlocked(int level)
    {
        return level >= 0 && level < LevelUnlocked.Length && LevelUnlocked[level];
    }

    /// <summary>沿 direction 找下一个开放的关卡;没有就留在原地。</summary>
    private static int NextUnlocked(int from, int direction)
    {
        for (int i = from + direction; i >= 0 && i < LevelNames.Length; i += direction)
        {
            if (IsUnlocked(i))
            {
                return i;
            }
        }

        return from;
    }

    private void TrySelect(int level)
    {
        if (!IsUnlocked(level))
        {
            return;
        }

        selectedLevel = level;
        UpdateSelectedCardVisual();
    }

    private void Confirm()
    {
        // 兜底:任何路径都不能进未开放的关卡(摇杆/键盘/点击已各自拦过一道)。
        if (!IsUnlocked(selectedLevel))
        {
            return;
        }

        confirmed = true;
        LijiangEchoStageKit.PlaySfx("button", 0.62f);

        // 过场已拆成独立场景(Stage_Intro 已在 Build Settings)时走它;否则退回旧流程(旧主场景从过场开始)。
        // 这样"建好场景就自动生效、没建就照旧",不会因场景不存在而出错。
        if (Application.CanStreamedLevelBeLoaded("Stage_Intro"))
        {
            LijiangEchoGameFlow.Instance.SelectedLevel = selectedLevel;
            LijiangEchoGameFlow.Instance.GoToStage("Stage_Intro");
        }
        else
        {
            LijiangEchoGameFlow.Instance.EnterLegacyFlow(selectedLevel);
        }
    }

    private void BuildSelectScreen()
    {
        LijiangEchoStageKit.PlayStageLoop("ambience", 0.34f);

        AddLayer("select/select_frame", "选关紫色暗幕", Vector3.zero, LijiangEchoStageKit.MainCanvasWidth, -18, 0.025f);
        AddLayer("select/select_line", "选关连接线", new Vector3(0f, -0.02f, -0.03f), LijiangEchoStageKit.WideStripWidth, -6, 0.92f);
        AddLayer("select/select_edge", "选关两侧色块", new Vector3(0f, -0.02f, -0.04f), LijiangEchoStageKit.WideStripWidth, -5, 0.72f);

        selectCards = new SpriteRenderer[LevelCardPaths.Length];
        selectNumbers = new SpriteRenderer[LevelNumberPaths.Length];
        for (int i = 0; i < LevelCardPaths.Length; i++)
        {
            GameObject card = AddLayer(LevelCardPaths[i], "选关卡片_" + LevelNames[i], new Vector3(0f, -0.02f, -0.08f - i * 0.01f), LijiangEchoStageKit.WideStripWidth, 2 + i);
            selectCards[i] = card.GetComponent<SpriteRenderer>();

            GameObject symbol = AddLayer(LevelSymbolPaths[i], "选关纹样_" + LevelNames[i], new Vector3(0f, -0.02f, -0.13f - i * 0.01f), LijiangEchoStageKit.WideStripWidth, 8 + i, 0.92f);
            RegisterMotion(symbol, LijiangEchoStageKit.MotionKind.FloatY, 0.018f, 1.6f, i * 1.3f);

            GameObject number = AddIcon(LevelNumberPaths[i], "关卡数字_" + (i + 1), SelectNumberPositions[i], 0.18f, 18);
            selectNumbers[i] = number.GetComponent<SpriteRenderer>();
        }

        AddLayer("select/bird_left_symbol", "左侧鸟纹装饰", new Vector3(0f, -0.02f, -0.16f), LijiangEchoStageKit.WideStripWidth, 13, 0.78f);
        AddLayer("select/frog_right_symbol", "右侧蛙纹装饰", new Vector3(0f, -0.02f, -0.17f), LijiangEchoStageKit.WideStripWidth, 13, 0.78f);
        AddLayer("select/bird_left_card", "左侧鸟纹白底卡", new Vector3(0f, -0.02f, -0.18f), LijiangEchoStageKit.WideStripWidth, 14, 0.82f);
        AddLayer("select/frog_right_card", "右侧蛙纹白底卡", new Vector3(0f, -0.02f, -0.19f), LijiangEchoStageKit.WideStripWidth, 14, 0.82f);
        AddLayer("select/select_border", "选关外框", new Vector3(0f, -0.02f, -0.2f), LijiangEchoStageKit.WideStripWidth, 20, 0.92f);
        AddIcon("ui/settings", "左上设置入口", new Vector3(-2.42f, 1.05f, -0.25f), 0.24f, 30, 0.88f);

        UpdateSelectedCardVisual();
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

            // 需求第 2 条:未开放的关卡固定变暗,不随选中状态变亮,视觉上就是"进不去"。
            if (!IsUnlocked(i))
            {
                selectCards[i].color = new Color(0.55f, 0.55f, 0.6f, LockedCardAlpha);
                if (selectNumbers != null && i < selectNumbers.Length && selectNumbers[i] != null)
                {
                    selectNumbers[i].color = new Color(0.55f, 0.55f, 0.6f, LockedCardAlpha);
                    selectNumbers[i].transform.localScale = Vector3.one * 0.18f;
                }

                continue;
            }

            selectCards[i].color = selected ? Color.white : new Color(1f, 1f, 1f, 0.52f);

            if (selectNumbers != null && i < selectNumbers.Length && selectNumbers[i] != null)
            {
                selectNumbers[i].color = selected ? Color.white : new Color(1f, 1f, 1f, 0.35f);
                selectNumbers[i].transform.localScale = Vector3.one * (selected ? 0.23f : 0.18f);
            }
        }
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
