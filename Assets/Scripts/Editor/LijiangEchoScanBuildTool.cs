using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// 出「扫码功能」测试包用的小工具。
///
/// 为什么需要它:Build Settings 里现在只有 Bootstrap 一个场景,Scanplay 根本不在包里,
/// 直接 Build 上头显进的还是正式流程,扫码那套压根跑不到。手动去改场景列表容易忘了改回来,
/// 所以做成两条菜单:出测试包 / 恢复正式列表。
///
/// 出包前会先跑一遍自检(见 CheckReadiness),把常见的坑列出来 —— 平台没切、
/// 场景没搭、纹理压缩不是 ASTC 之类。
/// </summary>
public static class LijiangEchoScanBuildTool
{
    private const string Root = "漓江回声/5 调试/扫码/";
    private const string ScanScenePath = "Assets/Scenes/Scanplay.unity";
    private const string MainScenePath = "Assets/Scenes/Bootstrap.unity";
    private const string BackupKey = "LijiangEcho.ScanBuild.SceneBackup";

    // ————————————————————————————— 自检 —————————————————————————————

    [MenuItem(Root + "① 出包前自检", false, 40)]
    private static void CheckReadinessMenu()
    {
        List<string> problems = new List<string>();
        List<string> ok = new List<string>();
        CheckReadiness(problems, ok);

        string body = "【就绪】\n" + string.Join("\n", ok);
        if (problems.Count > 0)
        {
            body += "\n\n【要处理】\n" + string.Join("\n", problems);
        }
        else
        {
            body += "\n\n没有发现问题,可以出包。";
        }

        Debug.Log("[漓江回声] 扫码出包自检\n" + body);
        EditorUtility.DisplayDialog("扫码出包自检", body, "知道了");
    }

    private static void CheckReadiness(List<string> problems, List<string> ok)
    {
        // 1. 平台
        if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
        {
            ok.Add("· 平台已经是 Android");
        }
        else
        {
            problems.Add("· 平台还不是 Android —— File → Build Settings → Android → Switch Platform"
                + "(第一次切要重新导入资源,可能要等十几分钟)");
        }

        // 2. 场景文件在不在
        if (File.Exists(ScanScenePath))
        {
            ok.Add("· Scanplay 场景文件存在");
        }
        else
        {
            problems.Add("· 找不到 " + ScanScenePath);
        }

        // 3. 场景里搭好了没 —— 直接读场景文件文本,不用打开场景
        if (File.Exists(ScanScenePath))
        {
            string text = File.ReadAllText(ScanScenePath);
            bool hasScript = text.Contains("MonoBehaviour");
            if (hasScript)
            {
                ok.Add("· Scanplay 里已经有脚本(应该跑过「一键搭好当前场景」了)");
            }
            else
            {
                problems.Add("· Scanplay 还是个空场景 —— 先打开它,跑一次"
                    + "「漓江回声/5 调试/扫码/一键搭好当前场景」并保存");
            }
        }

        // 4. 纹理压缩
        if (EditorUserBuildSettings.androidBuildSubtarget == MobileTextureSubtarget.ASTC)
        {
            ok.Add("· 纹理压缩是 ASTC");
        }
        else
        {
            problems.Add("· 纹理压缩不是 ASTC(Quest 上建议 ASTC)——"
                + "Build Settings 里改,或用下面那条「出扫码测试包」会自动设");
        }

        // 5. 包名
        string id = PlayerSettings.GetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android);
        if (!string.IsNullOrEmpty(id) && !id.Contains("Unity-Technologies"))
        {
            ok.Add("· 包名:" + id);
        }
        else
        {
            problems.Add("· 包名还是 Unity 模板默认值,装机可能冲突");
        }
    }

    // ————————————————————————————— 出包 —————————————————————————————

    [MenuItem(Root + "② 出扫码测试包(只装 Scanplay)", false, 41)]
    private static void BuildScanApk()
    {
        List<string> problems = new List<string>();
        List<string> ok = new List<string>();
        CheckReadiness(problems, ok);

        if (problems.Count > 0)
        {
            bool go = EditorUtility.DisplayDialog("扫码出包",
                "自检发现这些问题:\n\n" + string.Join("\n", problems) + "\n\n还要继续吗?",
                "继续出包", "先去处理");
            if (!go)
            {
                return;
            }
        }

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            EditorUtility.DisplayDialog("扫码出包",
                "平台还不是 Android,没法出包。\n\nFile → Build Settings → Android → Switch Platform", "好");
            return;
        }

        string apk = EditorUtility.SaveFilePanel("保存扫码测试包", "", "漓江回声_扫码测试.apk", "apk");
        if (string.IsNullOrEmpty(apk))
        {
            return;
        }

        // 把原来的场景列表存起来,出完包能一键还原
        BackupSceneList();

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(ScanScenePath, true)
        };
        EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScanScenePath },
            locationPathName = apk,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        RestoreSceneList();

        if (report.summary.result == BuildResult.Succeeded)
        {
            string message = $"出包成功:{apk}\n"
                + $"大小 {report.summary.totalSize / 1024 / 1024} MB\n\n"
                + "装机:\n"
                + $"  adb install -r \"{apk}\"\n\n"
                + "戴上头显,在「未知来源 / 应用库」里找「漓江回声MR」启动。\n"
                + "把打印的二维码放进视野即可(lijiang:fish / snake / frog / bird)。";
            Debug.Log("[漓江回声] " + message);
            EditorUtility.DisplayDialog("扫码测试包已生成", message, "好");
            EditorUtility.RevealInFinder(apk);
        }
        else
        {
            Debug.LogError($"[漓江回声] 出包失败:{report.summary.result},错误 {report.summary.totalErrors} 个。"
                + "详见 Console 上面的报错。场景列表已还原。");
        }
    }

    [MenuItem(Root + "③ 恢复正式场景列表", false, 42)]
    private static void RestoreSceneListMenu()
    {
        RestoreSceneList();
        Debug.Log("[漓江回声] 场景列表已恢复。");
    }

    private static void BackupSceneList()
    {
        List<string> paths = new List<string>();
        foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
        {
            if (scene != null && scene.enabled)
            {
                paths.Add(scene.path);
            }
        }

        if (paths.Count > 0)
        {
            SessionState.SetString(BackupKey, string.Join(";", paths));
        }
    }

    private static void RestoreSceneList()
    {
        string saved = SessionState.GetString(BackupKey, string.Empty);
        string[] paths = string.IsNullOrEmpty(saved)
            ? new[] { MainScenePath }   // 没有备份就退回正式入口
            : saved.Split(';');

        List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene>();
        foreach (string path in paths)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
            }
        }

        if (scenes.Count > 0)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
