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

    // —— 直接在 Inspector 里改:选中资源 ——
    [MenuItem(SelectMenu, false, 20)]
    private static void SelectAsset()
    {
        LijiangEchoBattleSettings settings = GetOrCreate();
        Selection.activeObject = settings;
        EditorGUIUtility.PingObject(settings);
    }
}
