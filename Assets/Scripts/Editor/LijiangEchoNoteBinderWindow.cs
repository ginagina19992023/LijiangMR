using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 纹样绑定总表:一个窗口里【看清 + 替换】每个打击类型用的是哪个纹样 Prefab,【不用改代码】。
///
/// 运行时怎么找纹样(见 LijiangEchoGameController.LoadNotePrefab):
///   先找本关专属  Resources/LijiangEchoNotes/Note_level{关卡}_{类型}
///   没有再用全局  Resources/LijiangEchoNotes/Note_{鱼/鸟/蛇/蛙}
///   再没有才回退运行时代码生成。
/// 所以"替换某关某类型的纹样" = 在 LijiangEchoNotes 下放一个对应名字的 Prefab;删掉它就回退用全局。
///
/// 本窗口三件事:
///   ① 总表:每个类型 × (全局 / 关卡0/1/2) 当前绑的是哪个 Prefab,带缩略图,一眼看清。
///   ② 替换:把【已有的某个 Prefab】直接绑到某格(复制成对应名字);或从一张贴图新建再绑。
///   ③ 看效果:一键进对应关卡的战斗试玩。
/// </summary>
public class LijiangEchoNoteBinderWindow : EditorWindow
{
    private const string OutFolder = "Assets/Resources/LijiangEchoNotes";
    private const string MainScenePath = "Assets/Scenes/LijiangEchoMR_Main.unity";

    private static readonly string[] TypeLabels = { "单击 · 鱼", "双击 · 鸟", "长按 · 蛇", "挥划 · 蛙" };
    private static readonly string[] TypeKeys = { "single", "double", "hold", "swipe" };
    private static readonly string[] GlobalNames = { "Note_Fish", "Note_Bird", "Note_Snake", "Note_Frog" };
    private static readonly string[] LevelLabels = { "全局(所有关卡)", "关卡0", "关卡1", "关卡2" };

    // —— 替换操作区的状态 ——
    private int targetLevel;              // 0=全局,1..3=关卡0/1/2
    private int targetType;               // 0..3
    private int sourceMode;               // 0=用已有 Prefab,1=用贴图新建
    private GameObject sourcePrefab;      // 已有 Prefab 来源
    private Texture2D sourceArt;          // 贴图来源
    private float newSize = 0.5f;
    private bool newWhite;
    private string status = string.Empty;
    private Vector2 scroll;

    [MenuItem("漓江回声/纹样/纹样绑定总表（看清+替换每个类型的纹样）")]
    public static void Open()
    {
        LijiangEchoNoteBinderWindow w = GetWindow<LijiangEchoNoteBinderWindow>("纹样绑定总表");
        w.minSize = new Vector2(520f, 520f);
        w.Show();
    }

    // Note_Fish/Bird/Snake/Frog(全局)
    private static string GlobalPath(int type) => OutFolder + "/" + GlobalNames[type] + ".prefab";

    // Note_level{N}_{类型}(某关卡覆盖)
    private static string LevelOverridePath(int level0Based, int type) =>
        OutFolder + "/Note_level" + level0Based + "_" + TypeKeys[type] + ".prefab";

    // targetLevel: 0=全局→写全局名;1..3=关卡→写 Note_level{N}_{类型}
    private string TargetPath()
    {
        return targetLevel == 0 ? GlobalPath(targetType) : LevelOverridePath(targetLevel - 1, targetType);
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.HelpBox(
            "运行时找纹样的顺序:本关专属 Note_level{关}_{类型} → 全局 Note_鱼/鸟/蛇/蛙 → 代码兜底。\n" +
            "所以某格填了 Prefab 就用它,某关没填就自动用全局。下面总表一眼看清,替换区可直接绑已有 Prefab。",
            MessageType.Info);

        DrawOverviewTable();

        EditorGUILayout.Space(10f);
        DrawReplaceSection();

        EditorGUILayout.Space(6f);
        if (!string.IsNullOrEmpty(status))
        {
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        EditorGUILayout.EndScrollView();
    }

    // ① —— 当前绑定总表 ——
    private void DrawOverviewTable()
    {
        EditorGUILayout.LabelField("① 当前绑定总表", EditorStyles.boldLabel);

        for (int type = 0; type < TypeKeys.Length; type++)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                GameObject global = AssetDatabase.LoadAssetAtPath<GameObject>(GlobalPath(type));

                using (new EditorGUILayout.HorizontalScope())
                {
                    Texture thumb = global != null ? AssetPreview.GetAssetPreview(global) : null;
                    Rect r = GUILayoutUtility.GetRect(48f, 48f, GUILayout.Width(48f), GUILayout.Height(48f));
                    if (thumb != null)
                    {
                        GUI.DrawTexture(r, thumb, ScaleMode.ScaleToFit);
                    }
                    else
                    {
                        GUI.Box(r, global != null ? "..." : "无");
                    }

                    using (new EditorGUILayout.VerticalScope())
                    {
                        EditorGUILayout.LabelField(TypeLabels[type], EditorStyles.boldLabel);
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField("全局", GUILayout.Width(32f));
                            // 只读展示:显示当前绑的全局 Prefab(点它可在 Project 里定位),拖放无效(每帧重读)
                            EditorGUILayout.ObjectField(global, typeof(GameObject), false);
                            if (global == null)
                            {
                                EditorGUILayout.LabelField("(缺全局 Prefab,会走代码兜底)", EditorStyles.miniLabel);
                            }
                        }
                    }
                }

                // 三个关卡的覆盖情况:有覆盖显示 Prefab,没有显示"用全局"
                for (int lv = 0; lv < 3; lv++)
                {
                    GameObject over = AssetDatabase.LoadAssetAtPath<GameObject>(LevelOverridePath(lv, type));
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField("关卡" + lv, GUILayout.Width(48f));
                        if (over != null)
                        {
                            EditorGUILayout.ObjectField(over, typeof(GameObject), false);
                            if (GUILayout.Button("删覆盖", GUILayout.Width(56f)))
                            {
                                AssetDatabase.DeleteAsset(LevelOverridePath(lv, type));
                                AssetDatabase.Refresh();
                                status = $"已删除 关卡{lv}·{TypeLabels[type]} 的覆盖 → 恢复用全局。";
                            }
                        }
                        else
                        {
                            EditorGUILayout.LabelField("用全局 " + GlobalNames[type], EditorStyles.miniLabel);
                        }
                    }
                }
            }
        }
    }

    // ② —— 替换 / 绑定操作 ——
    private void DrawReplaceSection()
    {
        EditorGUILayout.LabelField("② 替换 / 绑定到某格", EditorStyles.boldLabel);

        using (new EditorGUILayout.HorizontalScope())
        {
            targetLevel = EditorGUILayout.Popup("目标关卡", targetLevel, LevelLabels);
            targetType = EditorGUILayout.Popup("目标类型", targetType, TypeLabels);
        }

        EditorGUILayout.LabelField("将写入:", "Resources/LijiangEchoNotes/" +
            System.IO.Path.GetFileName(TargetPath()), EditorStyles.miniLabel);

        sourceMode = GUILayout.Toolbar(sourceMode, new[] { "用已有 Prefab", "用贴图新建" });

        if (sourceMode == 0)
        {
            sourcePrefab = (GameObject)EditorGUILayout.ObjectField(
                new GUIContent("来源 Prefab", "把这个已有的纹样 Prefab 复制成目标格,直接生效"),
                sourcePrefab, typeof(GameObject), false);

            using (new EditorGUI.DisabledScope(sourcePrefab == null))
            {
                if (GUILayout.Button("把此 Prefab 绑定到目标格", GUILayout.Height(28f)))
                {
                    BindExisting();
                }
            }
        }
        else
        {
            sourceArt = (Texture2D)EditorGUILayout.ObjectField("纹样贴图", sourceArt, typeof(Texture2D), false);
            newSize = EditorGUILayout.Slider(new GUIContent("统一大小", "按较大边适配,保证各纹样规格一致"), newSize, 0.2f, 1.0f);
            newWhite = EditorGUILayout.Toggle("白剪影", newWhite);

            using (new EditorGUI.DisabledScope(sourceArt == null))
            {
                if (GUILayout.Button("从贴图新建并绑定到目标格", GUILayout.Height(28f)))
                {
                    status = LijiangEchoNotePrefabTool.BuildNoteFromTexture(
                        sourceArt, System.IO.Path.GetFileNameWithoutExtension(TargetPath()), newSize, newWhite);
                    AssetDatabase.Refresh();
                }
            }
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("③ 看效果", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("进战斗试玩:", GUILayout.Width(80f));
            for (int lv = 0; lv < 3; lv++)
            {
                if (GUILayout.Button("关卡" + lv))
                {
                    EnterBattle(lv);
                }
            }
        }
    }

    /// <summary>把已有 Prefab 复制成目标格的名字(运行时按名加载,即完成替换)。</summary>
    private void BindExisting()
    {
        string srcPath = AssetDatabase.GetAssetPath(sourcePrefab);
        if (string.IsNullOrEmpty(srcPath) || !srcPath.EndsWith(".prefab"))
        {
            status = "来源不是一个 Prefab 资源(请拖入 Project 里的 .prefab)。";
            return;
        }

        string dst = TargetPath();
        if (srcPath == dst)
        {
            status = "来源就是目标本身,无需绑定。";
            return;
        }

        if (!AssetDatabase.IsValidFolder(OutFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            AssetDatabase.CreateFolder("Assets/Resources", "LijiangEchoNotes");
        }

        bool overwrite = AssetDatabase.LoadAssetAtPath<GameObject>(dst) != null;
        if (overwrite && !EditorUtility.DisplayDialog("覆盖确认",
                $"目标 {System.IO.Path.GetFileName(dst)} 已存在,将被『{sourcePrefab.name}』的副本覆盖。\n继续?", "覆盖", "取消"))
        {
            return;
        }

        if (overwrite)
        {
            AssetDatabase.DeleteAsset(dst);
        }

        bool ok = AssetDatabase.CopyAsset(srcPath, dst);
        AssetDatabase.Refresh();
        status = ok
            ? $"已绑定:{System.IO.Path.GetFileName(dst)} ← 复制自『{sourcePrefab.name}』。重进战斗生效。"
            : "复制失败(检查目标目录/权限)。";
    }

    /// <summary>写调试标记并进入指定关卡的战斗(和「漓江回声/调试/进 战斗」同机制)。</summary>
    private static void EnterBattle(int level)
    {
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
        }

        PlayerPrefs.SetInt("LJ_DebugStartStage", 4); // 4=战斗
        PlayerPrefs.SetInt("LJ_DebugLevel", level);
        PlayerPrefs.Save();

        if (SceneManager.GetActiveScene().path != MainScenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        }

        EditorApplication.isPlaying = true;
    }
}
