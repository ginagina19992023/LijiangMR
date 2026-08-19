using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 一次性场景搬迁工具：把 LijiangEchoMR_Main.unity 里的 Meta Building Blocks XR Rig
/// （Camera Rig / 透视 / 手部追踪 / 眼动追踪 / 方向光）整体搬到 Bootstrap.unity，
/// 让 Rig 在切阶段场景时常驻、不重新初始化。用 SceneManager.MoveGameObjectToScene
/// 完成搬迁，而不是手改场景 YAML，以正确保留 Building Blocks 的嵌套 Prefab 引用。
/// </summary>
public static class LijiangEchoSceneSplitTool
{
    private const string MainScenePath = "Assets/Scenes/LijiangEchoMR_Main.unity";
    private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";

    private static readonly string[] RigRootNames =
    {
        "TrackingSpace",
        "[BuildingBlock] Camera Rig",
        "[BuildingBlock] Passthrough",
        "[BuildingBlock] Hand Tracking left",
        "[BuildingBlock] Hand Tracking right",
        "[BuildingBlock] Eye Gaze Left",
        "[BuildingBlock] Eye Gaze Right",
        "Directional Light"
    };

    [MenuItem("漓江回声/拆分场景/搬迁 XR Rig 到 Bootstrap 场景")]
    public static void MoveRigToBootstrap()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("[漓江回声] 请退出 Play 模式后再执行场景搬迁。");
            return;
        }

        bool confirmed = EditorUtility.DisplayDialog(
            "搬迁 XR Rig",
            "将把 " + MainScenePath + " 里的 Camera Rig / 透视 / 手部追踪 / 眼动追踪 / 方向光整体搬到 " +
            BootstrapScenePath + "，并保存两个场景。执行前会先打开这两个场景（未保存的更改请先自行保存）。是否继续？",
            "继续",
            "取消");
        if (!confirmed)
        {
            return;
        }

        Scene mainScene = EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        Scene bootstrapScene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Additive);

        int movedCount = 0;
        foreach (string rootName in RigRootNames)
        {
            GameObject root = FindRootInScene(mainScene, rootName);
            if (root == null)
            {
                Debug.LogWarning("[漓江回声] 主场景里没找到根物体：" + rootName + "，已跳过。");
                continue;
            }

            SceneManager.MoveGameObjectToScene(root, bootstrapScene);
            movedCount++;
        }

        EditorSceneManager.SaveScene(mainScene);
        EditorSceneManager.SaveScene(bootstrapScene);

        Debug.Log($"[漓江回声] 已搬迁 {movedCount} 个根物体到 Bootstrap.unity，并保存了两个场景。");
        EditorUtility.DisplayDialog(
            "搬迁完成",
            $"已搬迁 {movedCount} 个根物体到 Bootstrap.unity。\n\n" +
            "接下来请打开 File > Build Profiles（或 Build Settings），确认场景顺序为：\n" +
            "Bootstrap → Stage_Start → Stage_Select → LijiangEchoMR_Main，\n" +
            "然后从 Bootstrap.unity 进入 Play 模式测试完整流程。",
            "好");
    }

    private static GameObject FindRootInScene(Scene scene, string rootName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == rootName)
            {
                return root;
            }
        }

        return null;
    }
}
