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
    private const float HitHalfXMin = 0.18f;     // 判定半宽下限:按钮挨得再近也还点得到
    private const float HitHalfXMax = 0.60f;     // 上限:按钮拉得再开,也别宽到吃掉隔壁
    private const float HitHalfY = 0.34f;        // 纵向半高(图标那一行)
    private const float HitExtendDown = 0.42f;   // 再向下延伸罩住图标底下的文字标签

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

    /// <summary>判定区按图标【实际所在位置】算,但宽度由【相邻按钮的间距】推出来,不用包围盒。
    ///
    /// 为什么不用 renderer.bounds:这些图标贴图内容不居中、四周带大片透明留白,
    /// bounds 包含全部留白,一撑开主页那个框就能盖住整排按钮 ——
    /// 而命中取第一个匹配,结果就是「点哪儿都回主页」。
    /// 改成按间距推宽度:每个框最多伸到与相邻按钮的中点,结构上不可能重叠,
    /// 而且 Prefab 里你把按钮拉多开,判定区就跟着多宽。
    ///
    /// 顺便记下作者摆好的缩放和颜色,悬停高亮只在这个基础上做增量,不覆盖。</summary>
    private void RebuildHitRects()
    {
        hitRects.Clear();
        iconBaseScales.Clear();
        iconBaseColors.Clear();

        // 先取各图标在菜单局部坐标下的中心。
        int count = iconRenderers.Count;
        Vector3[] centers = new Vector3[count];
        bool[] valid = new bool[count];
        for (int i = 0; i < count; i++)
        {
            SpriteRenderer renderer = iconRenderers[i];
            iconBaseScales.Add(renderer != null ? renderer.transform.localScale : Vector3.one);
            iconBaseColors.Add(renderer != null ? renderer.color : Color.white);

            valid[i] = renderer != null;
            if (valid[i])
            {
                centers[i] = menuRoot.InverseTransformPoint(renderer.bounds.center);
            }
        }

        for (int i = 0; i < count; i++)
        {
            if (!valid[i])
            {
                hitRects.Add(new Rect(0f, 0f, 0f, 0f));   // 占位,保持下标对应
                continue;
            }

            // 到最近邻居的一半距离(留一点缝),再夹到合理区间。
            float nearest = float.MaxValue;
            for (int j = 0; j < count; j++)
            {
                if (j == i || !valid[j])
                {
                    continue;
                }

                nearest = Mathf.Min(nearest, Mathf.Abs(centers[j].x - centers[i].x));
            }

            float halfX = nearest >= float.MaxValue
                ? HitHalfXMax
                : Mathf.Clamp(nearest * 0.5f - 0.02f, HitHalfXMin, HitHalfXMax);

            hitRects.Add(new Rect(
                centers[i].x - halfX,
                centers[i].y - HitHalfY - HitExtendDown,
                halfX * 2f,
                HitHalfY * 2f + HitExtendDown));
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

    /// <summary>命中取【中心最近的】那个,不是第一个匹配到的。
    /// 万一判定框还有重叠,也不会像以前那样一律落到下标 0(主页)。</summary>
    private int HitButton(Vector3 localPoint)
    {
        Vector2 p = new Vector2(localPoint.x, localPoint.y);
        int best = -1;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < hitRects.Count; i++)
        {
            Rect rect = hitRects[i];
            if (rect.width <= 0f || !rect.Contains(p))
            {
                continue;
            }

            float distance = Mathf.Abs(p.x - rect.center.x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
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
