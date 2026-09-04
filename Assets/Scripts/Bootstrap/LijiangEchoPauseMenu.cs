using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 全局暂停菜单(9.1 需求第 6 条的收尾)。
///
/// 【为什么要有它】暂停菜单原本写死在 LijiangEchoGameController 里,而那个控制器只存在于
/// 旧主场景。拆场景之后 Stage_Start / Stage_Select / Stage_Intro 里【根本没有暂停菜单】——
/// 这是拆分时留下的缺口。菜单是盖在所有阶段之上的覆盖层,不属于任何一个阶段,
/// 所以它该挂在常驻的 Bootstrap 上,由 LijiangEchoGameFlow 在 Awake 里自动补挂,
/// Unity 那边不需要任何操作。
///
/// 【和旧菜单的关系】旧主场景在跑时(LijiangEchoGameController.LegacyOwnsPauseMenu),
/// 这里主动让位,由旧的那套处理,避免同一个按键弹出两套菜单。旧代码一行没动。
///
/// 【可编辑 Prefab】Resources/LijiangEchoMenu/PauseMenu 存在就实例化它,和旧菜单共用同一个
/// Prefab;没有就用代码生成。点击判定按图标【实际所在位置和包围盒】算,Prefab 里怎么摆就点哪。
/// </summary>
public class LijiangEchoPauseMenu : MonoBehaviour
{
    private const string MenuPrefabPath = "LijiangEchoMenu/PauseMenu";

    private const float IconSize = 0.58f;
    private const float IconY = 0.12f;
    private const float LabelY = -0.34f;
    private const float HitPadX = 0.14f;         // 横向放宽,VR 里好点
    private const float HitExtendDown = 0.42f;   // 向下延伸罩住图标底下的文字标签

    private struct ButtonSpec
    {
        public string IconPath;
        public string Label;
        public float X;
    }

    private static readonly ButtonSpec[] Buttons =
    {
        new ButtonSpec { IconPath = "ui/home",  Label = "主页", X = -1.50f },
        new ButtonSpec { IconPath = "ui/music", Label = "音乐", X = -0.50f },
        new ButtonSpec { IconPath = "ui/skip",  Label = "跳过", X =  0.50f },
        new ButtonSpec { IconPath = "ui/back",  Label = "返回", X =  1.50f }
    };

    private Transform menuRoot;
    private readonly List<GameObject> spawnedObjects = new List<GameObject>();
    private readonly List<SpriteRenderer> iconRenderers = new List<SpriteRenderer>();
    private readonly List<Rect> hitRects = new List<Rect>();

    // 悬停高亮必须在【作者摆好的缩放和颜色】基础上做增量。
    // 曾经直接写 localScale = Vector3.one,把 Prefab 里图标 0.6 的缩放整个抹掉 ——
    // 菜单一打开四个图标被放大 1.67 倍,挤成一团,手调过的视觉居中偏移也跟着被放大。
    private readonly List<Vector3> iconBaseScales = new List<Vector3>();
    private readonly List<Color> iconBaseColors = new List<Color>();

    private bool open;
    private bool muted;
    private bool pressPrev;
    private bool clickArmed;
    private int hoverIndex = -1;

    private void Update()
    {
        // 旧主场景在跑 → 它自己有菜单,这里完全不插手。
        if (LijiangEchoGameController.LegacyOwnsPauseMenu)
        {
            if (open)
            {
                Close();
            }

            return;
        }

        if (MenuKeyPressed())
        {
            if (open)
            {
                Close();
            }
            else
            {
                Open();
            }
        }

        if (open)
        {
            UpdateInteraction();
        }
    }

    private static bool MenuKeyPressed()
    {
        bool keyboard = Keyboard.current != null &&
                        (Keyboard.current.escapeKey.wasPressedThisFrame ||
                         Keyboard.current.mKey.wasPressedThisFrame);
        bool controller = OVRInput.GetDown(OVRInput.Button.Two) ||
                          OVRInput.GetDown(OVRInput.Button.Four) ||
                          OVRInput.GetDown(OVRInput.Button.Start);
        return keyboard || controller;
    }

    // ————————————————————————————— 开 / 关 —————————————————————————————

    private void Open()
    {
        open = true;
        pressPrev = true;      // 开菜单那一下如果还按着,不要被当成对某个按钮的点击
        clickArmed = false;
        hoverIndex = -1;

        // 暂停:冻结所有阶段控制器的 Update(它们都吃 Time.deltaTime)+ 静音环境。
        // 本组件自己不依赖 deltaTime,timeScale=0 下照常响应。
        Time.timeScale = 0f;
        AudioListener.pause = true;

        menuRoot = LijiangEchoStageKit.PrepareStageRoot("漓江回声_暂停菜单");
        Build();
        RebuildHitRects();
    }

    private void Close()
    {
        open = false;
        clickArmed = false;
        hoverIndex = -1;
        spawnedObjects.Clear();
        iconRenderers.Clear();
        hitRects.Clear();

        Time.timeScale = 1f;
        AudioListener.pause = false;

        if (menuRoot != null)
        {
            Destroy(menuRoot.gameObject);
            menuRoot = null;
        }

        LijiangEchoStageKit.HideControllerPointers();
    }

    private void OnDisable()
    {
        // 组件被关掉/销毁时别把游戏留在暂停状态。
        if (open)
        {
            Time.timeScale = 1f;
            AudioListener.pause = false;
            open = false;
        }
    }

    // ————————————————————————————— 搭建 —————————————————————————————

    private void Build()
    {
        // 和旧菜单共用同一个 Prefab:有就实例化,没有就代码生成。
        GameObject prefab = Resources.Load<GameObject>(MenuPrefabPath);
        if (prefab != null)
        {
            GameObject instance = Instantiate(prefab, menuRoot);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            // 不动 localScale:Prefab 根上的整体缩放要保留。
            spawnedObjects.Add(instance);

            for (int i = 0; i < Buttons.Length; i++)
            {
                Transform found = FindDeepChild(instance.transform, "菜单" + Buttons[i].Label);
                iconRenderers.Add(found != null ? found.GetComponent<SpriteRenderer>() : null);
            }

            return;
        }

        LijiangEchoStageKit.AddLayer(menuRoot, spawnedObjects, "transition/purple_frame", "暂停暗幕",
            Vector3.zero, LijiangEchoStageKit.MainCanvasWidth, 80, 0.32f);
        LijiangEchoStageKit.AddLayer(menuRoot, spawnedObjects, "ui/card_back", "暂停面板",
            new Vector3(0f, 0.04f, -0.64f), 3.75f, 82, 0.78f);

        for (int i = 0; i < Buttons.Length; i++)
        {
            ButtonSpec spec = Buttons[i];
            GameObject icon = LijiangEchoStageKit.AddIcon(menuRoot, spawnedObjects, spec.IconPath,
                "菜单" + spec.Label, new Vector3(spec.X, IconY, -0.7f), IconSize, 86, 0.96f);
            iconRenderers.Add(icon != null ? icon.GetComponent<SpriteRenderer>() : null);

            LijiangEchoStageKit.AddText(menuRoot, spawnedObjects, spec.Label,
                new Vector3(spec.X, LabelY, -0.72f), 0.024f, Color.white, 90);
        }
    }

    /// <summary>判定区按图标实际所在位置和包围盒算,所以 Prefab 里怎么摆就点哪。
    /// 顺便记下作者摆好的缩放和颜色,悬停高亮只在这个基础上做增量,不覆盖。</summary>
    private void RebuildHitRects()
    {
        hitRects.Clear();
        iconBaseScales.Clear();
        iconBaseColors.Clear();

        for (int i = 0; i < iconRenderers.Count; i++)
        {
            SpriteRenderer renderer = iconRenderers[i];
            iconBaseScales.Add(renderer != null ? renderer.transform.localScale : Vector3.one);
            iconBaseColors.Add(renderer != null ? renderer.color : Color.white);

            if (renderer == null)
            {
                hitRects.Add(new Rect(0f, 0f, 0f, 0f));   // 占位,保持下标对应
                continue;
            }

            Vector3 center = menuRoot.InverseTransformPoint(renderer.bounds.center);
            Vector3 extents = menuRoot.InverseTransformVector(renderer.bounds.extents);
            float halfX = Mathf.Abs(extents.x) + HitPadX;
            float halfY = Mathf.Abs(extents.y);

            hitRects.Add(new Rect(
                center.x - halfX,
                center.y - halfY - HitExtendDown,
                halfX * 2f,
                halfY * 2f + HitExtendDown));
        }
    }

    private static Transform FindDeepChild(Transform root, string objectName)
    {
        if (root.name == objectName)
        {
            return root;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeepChild(root.GetChild(i), objectName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    // ————————————————————————————— 交互 —————————————————————————————

    private void UpdateInteraction()
    {
        LijiangEchoStageKit.UpdateControllerInput(menuRoot);

        bool pressing = LijiangEchoStageKit.TryGetActivePointer(menuRoot, out Vector3 point, out bool held) && held;

        // 按下即"待命",直到真的点中某个按钮或松手才作废 —— 边缘帧不会丢点击。
        if (pressing && !pressPrev)
        {
            clickArmed = true;
        }

        if (!pressing)
        {
            clickArmed = false;
        }

        pressPrev = pressing;

        int hover = HitButton(point);
        UpdateHoverVisual(hover);

        if (hover < 0 || !clickArmed)
        {
            return;
        }

        clickArmed = false;
        switch (hover)
        {
            case 0: ActionHome(); break;
            case 1: ActionMusic(); break;
            case 2: ActionSkip(); break;
            case 3: Close(); break;      // 返回 = 关菜单继续
        }
    }

    private int HitButton(Vector3 localPoint)
    {
        Vector2 p = new Vector2(localPoint.x, localPoint.y);
        for (int i = 0; i < hitRects.Count; i++)
        {
            if (hitRects[i].width > 0f && hitRects[i].Contains(p))
            {
                return i;
            }
        }

        return -1;
    }

    private void UpdateHoverVisual(int hover)
    {
        if (hover != hoverIndex)
        {
            hoverIndex = hover;
            if (hover >= 0)
            {
                LijiangEchoStageKit.PlaySfx("swipe", 0.22f);
            }
        }

        for (int i = 0; i < iconRenderers.Count; i++)
        {
            SpriteRenderer renderer = iconRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            // 在作者摆好的缩放/颜色上做增量,不覆盖(否则 Prefab 里调的大小和视觉居中全被抹掉)。
            bool on = i == hover;
            Vector3 baseScale = i < iconBaseScales.Count ? iconBaseScales[i] : Vector3.one;
            Color baseColor = i < iconBaseColors.Count ? iconBaseColors[i] : Color.white;

            renderer.transform.localScale = baseScale * (on ? 1.18f : 1f);
            float dim = on ? 1f : 0.82f;
            renderer.color = new Color(baseColor.r * dim, baseColor.g * dim, baseColor.b * dim, baseColor.a);
        }
    }

    // ————————————————————————————— 四个动作 —————————————————————————————

    private void ActionHome()
    {
        LijiangEchoStageKit.PlaySfx("button", 0.6f);
        Close();
        if (LijiangEchoGameFlow.Instance != null)
        {
            LijiangEchoGameFlow.Instance.GoToStage("Stage_Start");
        }
    }

    private void ActionMusic()
    {
        muted = !muted;
        AudioListener.volume = muted ? 0f : 1f;   // 菜单保持打开,方便再点回来
        LijiangEchoStageKit.PlaySfx("button", 0.6f);
    }

    /// <summary>跳过当前阶段。阶段控制器实现 <see cref="ILijiangEchoSkippableStage"/> 才有效;
    /// 开始界面/选关这类没有"下一步"可跳的阶段,点了只关菜单。</summary>
    private void ActionSkip()
    {
        LijiangEchoStageKit.PlaySfx("button", 0.6f);
        ILijiangEchoSkippableStage skippable = FindSkippableStage();
        Close();
        skippable?.SkipStage();
    }

    private static ILijiangEchoSkippableStage FindSkippableStage()
    {
        MonoBehaviour[] all = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] is ILijiangEchoSkippableStage skippable && all[i].isActiveAndEnabled)
            {
                return skippable;
            }
        }

        return null;
    }
}

/// <summary>阶段控制器实现它,暂停菜单的「跳过」才能跳过这个阶段。</summary>
public interface ILijiangEchoSkippableStage
{
    void SkipStage();
}
