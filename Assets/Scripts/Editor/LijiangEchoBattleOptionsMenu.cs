using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 战斗选项菜单:给审核组员一个"点一下就切"的开关,【不用改代码】。
/// 菜单项带 ✓ 显示当前状态;点击即翻转并写回 Resources/LijiangEchoBattleSettings.asset
/// (资源不存在会自动创建)。运行时由 LijiangEchoGameController 读取该资源。
/// </summary>
public static class LijiangEchoBattleOptionsMenu
{
    private const string AssetPath = "Assets/Resources/" + LijiangEchoBattleSettings.ResourceName + ".asset";
    private const string MirrorMenu = "漓江回声/战斗选项/双击=镜像汇合(左右对飞)";
    private const string AutoMirrorMenu = "漓江回声/战斗选项/音符按飞入方向自动镜像(总开关)";
    private const string HandSideMenu = "漓江回声/战斗选项/左右手判定(左侧音符只响应左手)";
    private const string BothHandsMenu = "漓江回声/战斗选项/双手音符需左右手同时打击";
    private const string SelectMenu = "漓江回声/战斗选项/选中设置资源(在 Inspector 里改)";

    /// <summary>取到资源;没有就在 Resources 下创建一份默认的。</summary>
    private static LijiangEchoBattleSettings GetOrCreate()
    {
        LijiangEchoBattleSettings settings = AssetDatabase.LoadAssetAtPath<LijiangEchoBattleSettings>(AssetPath);
        if (settings != null)
        {
            return settings;
        }

        string dir = Path.GetDirectoryName(AssetPath);
        if (!AssetDatabase.IsValidFolder(dir))
        {
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }

        settings = ScriptableObject.CreateInstance<LijiangEchoBattleSettings>();
        AssetDatabase.CreateAsset(settings, AssetPath);
        AssetDatabase.SaveAssets();
        return settings;
    }

    // —— 双击镜像汇合:带 ✓ 的开关,点一下翻转 ——
    [MenuItem(MirrorMenu, false, 0)]
    private static void ToggleMirror()
    {
        LijiangEchoBattleSettings settings = GetOrCreate();
        settings.doubleNoteMirrorConverge = !settings.doubleNoteMirrorConverge;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Menu.SetChecked(MirrorMenu, settings.doubleNoteMirrorConverge);
        Debug.Log($"[漓江回声] 双击飞入样式 → {(settings.doubleNoteMirrorConverge ? "镜像汇合(左右对飞)" : "单侧飞入(默认)")}(重进战斗场景生效)");
    }

    [MenuItem(MirrorMenu, true)]
    private static bool ToggleMirrorValidate()
    {
        LijiangEchoBattleSettings settings = AssetDatabase.LoadAssetAtPath<LijiangEchoBattleSettings>(AssetPath);
        Menu.SetChecked(MirrorMenu, settings != null && settings.doubleNoteMirrorConverge);
        return true;
    }

    // —— 9.1 需求第 7 条:左右手判定(带 ✓),点一下翻转 ——
    [MenuItem(HandSideMenu, false, 2)]
    private static void ToggleHandSide()
    {
        LijiangEchoBattleSettings settings = GetOrCreate();
        settings.handSideJudge = !settings.handSideJudge;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Menu.SetChecked(HandSideMenu, settings.handSideJudge);
        Debug.Log($"[漓江回声] 左右手判定 → {(settings.handSideJudge ? "开:左侧音符只响应左手、右侧只响应右手,用错手不算命中" : "关:任意一只手都能打所有音符(旧行为)")}" +
                  ";PC 调试:鼠标左键=右手,Shift+左键=左手。重进战斗生效。");
    }

    [MenuItem(HandSideMenu, true)]
    private static bool ToggleHandSideValidate()
    {
        LijiangEchoBattleSettings settings = AssetDatabase.LoadAssetAtPath<LijiangEchoBattleSettings>(AssetPath);
        Menu.SetChecked(HandSideMenu, settings != null && settings.handSideJudge);
        return true;
    }

    // —— 9.1 需求第 7 条:双手音符必须两只手都到齐 ——
    [MenuItem(BothHandsMenu, false, 3)]
    private static void ToggleBothHands()
    {
        LijiangEchoBattleSettings settings = GetOrCreate();
        settings.doubleNoteNeedsBothHands = !settings.doubleNoteNeedsBothHands;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Menu.SetChecked(BothHandsMenu, settings.doubleNoteNeedsBothHands);
        Debug.Log($"[漓江回声] 双手音符 → {(settings.doubleNoteNeedsBothHands ? $"必须左右手在 {settings.twoHandSyncWindow:0.00}s 容差内都到齐" : "一次命中即可(旧行为)")}" +
                  ";容差在『选中设置资源』的 Inspector 里调。重进战斗生效。");
    }

    [MenuItem(BothHandsMenu, true)]
    private static bool ToggleBothHandsValidate()
    {
        LijiangEchoBattleSettings settings = AssetDatabase.LoadAssetAtPath<LijiangEchoBattleSettings>(AssetPath);
        Menu.SetChecked(BothHandsMenu, settings != null && settings.doubleNoteNeedsBothHands);
        return true;
    }

    // —— 音符按飞入方向自动镜像:总开关(带 ✓),点一下翻转 ——
    [MenuItem(AutoMirrorMenu, false, 1)]
    private static void ToggleAutoMirror()
    {
        LijiangEchoBattleSettings settings = GetOrCreate();
        settings.autoMirrorNotesByDirection = !settings.autoMirrorNotesByDirection;
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        Menu.SetChecked(AutoMirrorMenu, settings.autoMirrorNotesByDirection);
        Debug.Log($"[漓江回声] 音符按方向自动镜像(总开关)→ {(settings.autoMirrorNotesByDirection ? "开" : "关")};" +
                  "具体哪些类型参与,在『选中设置资源』的 Inspector 里勾(默认只鱼纹)。重进战斗生效。");
    }

    [MenuItem(AutoMirrorMenu, true)]
    private static bool ToggleAutoMirrorValidate()
    {
        LijiangEchoBattleSettings settings = AssetDatabase.LoadAssetAtPath<LijiangEchoBattleSettings>(AssetPath);
        Menu.SetChecked(AutoMirrorMenu, settings != null && settings.autoMirrorNotesByDirection);
        return true;
    }

    // —— 直接在 Inspector 里改:选中资源 ——
    [MenuItem(SelectMenu, false, 20)]
    private static void SelectAsset()
    {
        LijiangEchoBattleSettings settings = GetOrCreate();
        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
    }
}
