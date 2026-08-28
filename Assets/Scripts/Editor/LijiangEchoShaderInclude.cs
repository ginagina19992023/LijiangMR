using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 打击纹样用到的两个 shader 是运行时 Shader.Find + new Material 创建的,没有任何 .mat 资源引用它们,
/// 打包时会被剔除 → 真机变粉。这里在编辑器加载时自动把它们塞进 Graphics 的 Always Included Shaders,
/// 保证进包。已在列表里就跳过,不重复写。也提供一个手动菜单以防万一。
/// </summary>
[InitializeOnLoad]
public static class LijiangEchoShaderInclude
{
    private static readonly string[] ShaderNames =
    {
        "LijiangEcho/WhiteSilhouette",
        "LijiangEcho/SoftGlowAdd"
    };

    static LijiangEchoShaderInclude()
    {
        // 延迟一帧,确保 shader 资源已导入完成再查找。
        EditorApplication.delayCall += EnsureAll;
    }

    [MenuItem("漓江回声/材质/把打击纹样 shader 加入 Always Included")]
    public static void EnsureAllMenu()
    {
        EnsureAll();
        EditorUtility.DisplayDialog("完成", "已确保打击纹样 shader 在 Always Included Shaders 里(打包不会变粉)。", "好");
    }

    private static void EnsureAll()
    {
        bool changed = false;
        SerializedObject so = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
        SerializedProperty arr = so.FindProperty("m_AlwaysIncludedShaders");
        if (arr == null)
        {
            return;
        }

        foreach (string name in ShaderNames)
        {
            Shader shader = Shader.Find(name);
            if (shader == null)
            {
                continue; // 还没导入,下次编辑器加载再补
            }

            bool already = false;
            for (int i = 0; i < arr.arraySize; i++)
            {
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    already = true;
                    break;
                }
            }

            if (already)
            {
                continue;
            }

            int idx = arr.arraySize;
            arr.InsertArrayElementAtIndex(idx);
            arr.GetArrayElementAtIndex(idx).objectReferenceValue = shader;
            changed = true;
        }

        if (changed)
        {
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log("[漓江回声] 已把打击纹样 shader 加入 Always Included Shaders。");
        }
    }
}
