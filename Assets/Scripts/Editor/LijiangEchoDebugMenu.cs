using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 调试菜单:一键从任意阶段直接进入战斗主场景跑测,免走完整流程。
/// 原理:写 PlayerPrefs 标记 → 打开 LijiangEchoMR_Main → 进入 Play;
/// LijiangEchoGameController.Start 读该标记,直接跳到对应阶段(见 JumpToStageForDebug)。
/// 整个战斗体验只在 LijiangEchoMR_Main 里运行,故所有"单独跑测某一段"都经由此菜单。
/// </summary>
public static class LijiangEchoDebugMenu
{
    private const string MainScenePath = "Assets/Scenes/LijiangEchoMR_Main.unity";

    // 阶段序号:0=开始 1=选关 2=过场 3=描绘 4=战斗 5=结算
    private static void EnterStage(int stage, int level)
    {
        if (EditorApplication.isPlaying)
        {
            EditorApplication.isPlaying = false;
        }

        PlayerPrefs.SetInt("LJ_DebugStartStage", stage);
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

    [MenuItem("漓江回声/调试/进 开始界面")]
    private static void ToStart()
    {
        EnterStage(0, 0);
    }

    [MenuItem("漓江回声/调试/进 选关")]
    private static void ToSelect()
    {
        EnterStage(1, 0);
    }

    [MenuItem("漓江回声/调试/进 过场(关卡1)")]
    private static void ToIntro()
    {
        EnterStage(2, 0);
    }

    [MenuItem("漓江回声/调试/进 描绘(关卡1)")]
    private static void ToTrace()
    {
        EnterStage(3, 0);
    }

    [MenuItem("漓江回声/调试/进 战斗(关卡1)")]
    private static void ToBattle1()
    {
        EnterStage(4, 0);
    }

    [MenuItem("漓江回声/调试/进 战斗(关卡2)")]
    private static void ToBattle2()
    {
        EnterStage(4, 1);
    }

    [MenuItem("漓江回声/调试/进 战斗(关卡3)")]
    private static void ToBattle3()
    {
        EnterStage(4, 2);
    }

    [MenuItem("漓江回声/调试/进 结算(关卡1)")]
    private static void ToCard()
    {
        EnterStage(5, 0);
    }

    [MenuItem("漓江回声/调试/清除调试跳转标记")]
    private static void ClearFlag()
    {
        PlayerPrefs.DeleteKey("LJ_DebugStartStage");
        PlayerPrefs.DeleteKey("LJ_DebugLevel");
        PlayerPrefs.Save();
    }
}
