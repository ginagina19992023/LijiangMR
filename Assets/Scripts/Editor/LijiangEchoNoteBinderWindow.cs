using UnityEditor;
using UnityEngine;

/// <summary>
/// 纹样绑定:把任意一张纹样贴图,生成成"规格化"纹样 Prefab 并绑定到 全局 / 某关卡 的某个打击类型。
/// - 全局:所有关卡该打击类型都用它(覆盖 Note_鱼/鸟/蛇/蛙)。
/// - 某关卡:只在该关卡把该类型换成它(生成 Note_level{N}_{类型}),即"单个关卡里统一替换成另一种纹样"。
/// 生成时自动紧包围盒居中 + 按较大边统一大小,保证纹样库里各纹样规格一致。
/// </summary>
public class LijiangEchoNoteBinderWindow : EditorWindow
{
    private int levelIndex; // 0=全局,1=关卡0,2=关卡1,3=关卡2
    private int typeIndex;
    private Texture2D art;
    private float size = 0.5f;
    private bool white;
    private string status = string.Empty;

    private static readonly string[] LevelLabels = { "全局(所有关卡)", "关卡0 · 蛙", "关卡1 · 鸟", "关卡2 · 鱼" };
    private static readonly string[] TypeLabels = { "单击", "双击", "长按", "挥划" };
    private static readonly string[] TypeKeys = { "single", "double", "hold", "swipe" };
    private static readonly string[] GlobalNames = { "Note_Fish", "Note_Bird", "Note_Snake", "Note_Frog" };

    [MenuItem("漓江回声/纹样/纹样绑定（把某纹样用到某关卡某类型）")]
    public static void Open()
    {
        LijiangEchoNoteBinderWindow w = GetWindow<LijiangEchoNoteBinderWindow>("纹样绑定");
        w.minSize = new Vector2(440f, 260f);
        w.Show();
    }

    private string TargetPrefabName()
    {
        return levelIndex == 0 ? GlobalNames[typeIndex] : $"Note_level{levelIndex - 1}_{TypeKeys[typeIndex]}";
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "选一张纹样贴图 → 选『绑定到』+『打击类型』→ 生成并绑定。\n" +
            "· 全局:所有关卡该类型都用它。\n" +
            "· 某关卡:只在该关卡把该类型换成它(单个关卡统一替换成另一种纹样)。\n" +
            "自动居中 + 统一大小,规格一致。", MessageType.Info);

        levelIndex = EditorGUILayout.Popup("绑定到", levelIndex, LevelLabels);
        typeIndex = EditorGUILayout.Popup("打击类型", typeIndex, TypeLabels);
        art = (Texture2D)EditorGUILayout.ObjectField("纹样贴图", art, typeof(Texture2D), false);
        size = EditorGUILayout.Slider(new GUIContent("统一大小", "所有纹样按较大边适配到这个值,保证一致"), size, 0.2f, 1.0f);
        white = EditorGUILayout.Toggle("白剪影", white);

        EditorGUILayout.LabelField("将生成:", "Resources/LijiangEchoNotes/" + TargetPrefabName() + ".prefab", EditorStyles.miniLabel);

        using (new EditorGUI.DisabledScope(art == null))
        {
            if (GUILayout.Button("生成并绑定", GUILayout.Height(28f)))
            {
                status = LijiangEchoNotePrefabTool.BuildNoteFromTexture(art, TargetPrefabName(), size, white);
                AssetDatabase.Refresh();
            }
        }

        if (levelIndex > 0)
        {
            if (GUILayout.Button("删除本关该类型覆盖(恢复用全局纹样)"))
            {
                string path = "Assets/Resources/LijiangEchoNotes/" + TargetPrefabName() + ".prefab";
                status = AssetDatabase.DeleteAsset(path) ? "已删除覆盖:" + TargetPrefabName() : "没有该覆盖(本来就用全局)。";
                AssetDatabase.Refresh();
            }
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
    }
}
