using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 战斗音乐设置 + 诊断:一键把选的音频设为运行时用的 battle_music(替换所有同名文件、设 Decompress),
/// 并检查它能否被运行时读到、是否有采样。解决"换了音频却没声音"的常见问题。
/// </summary>
public class LijiangEchoBattleMusicWindow : EditorWindow
{
    private const string Dir = "Assets/Resources/LijiangEchoAudio";
    private const string ResourceName = "LijiangEchoAudio/battle_music";
    private AudioClip clip;
    private string report = string.Empty;

    [MenuItem("漓江回声/音频/战斗音乐（设置 + 诊断）")]
    public static void Open()
    {
        LijiangEchoBattleMusicWindow w = GetWindow<LijiangEchoBattleMusicWindow>("战斗音乐");
        w.minSize = new Vector2(460f, 320f);
        w.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "换了音乐却没声音?先检查两处:\n" +
            "1) Game 视图右上角的「🔇 Mute Audio」喇叭图标是否被点亮(静音)——静音时游戏音乐没声!\n" +
            "2) 用下面「设为战斗音乐」把音频设进去(会替换所有同名 battle_music 文件,避免重名歧义)。",
            MessageType.Info);

        clip = (AudioClip)EditorGUILayout.ObjectField("选音频", clip, typeof(AudioClip), false);
        using (new EditorGUI.DisabledScope(clip == null))
        {
            if (GUILayout.Button("设为战斗音乐(替换所有同名文件 + 设 Decompress)", GUILayout.Height(28f)))
            {
                SetAsBattleMusic();
            }
        }

        if (GUILayout.Button("诊断:运行时能读到吗?", GUILayout.Height(24f)))
        {
            Diagnose();
        }

        EditorGUILayout.Space(6f);
        EditorGUILayout.LabelField("结果", EditorStyles.boldLabel);
        EditorGUILayout.TextArea(report, GUILayout.ExpandHeight(true));
    }

    private void SetAsBattleMusic()
    {
        string src = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(src))
        {
            report = "所选不是项目资源。";
            return;
        }

        if (!AssetDatabase.IsValidFolder(Dir))
        {
            Directory.CreateDirectory(Dir);
            AssetDatabase.Refresh();
        }

        // 删掉所有现有的 battle_music.*(任何扩展名),避免重名歧义
        foreach (string existing in FindBattleMusicFiles())
        {
            if (Path.GetFullPath(existing) != Path.GetFullPath(src))
            {
                AssetDatabase.DeleteAsset(existing);
            }
        }

        string ext = Path.GetExtension(src);
        string dest = Dir + "/battle_music" + ext;
        if (Path.GetFullPath(src) != Path.GetFullPath(dest))
        {
            if (!AssetDatabase.CopyAsset(src, dest))
            {
                report = "复制失败(看 Console)。";
                return;
            }
        }

        AssetDatabase.Refresh();
        AudioImporter imp = AssetImporter.GetAtPath(dest) as AudioImporter;
        if (imp != null)
        {
            AudioImporterSampleSettings s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            imp.defaultSampleSettings = s;
            string plat = ActivePlatform();
            AudioImporterSampleSettings o = imp.ContainsSampleSettingsOverride(plat) ? imp.GetOverrideSampleSettings(plat) : imp.defaultSampleSettings;
            o.loadType = AudioClipLoadType.DecompressOnLoad;
            imp.SetOverrideSampleSettings(plat, o);
            imp.SaveAndReimport();
            AssetDatabase.ImportAsset(dest, ImportAssetOptions.ForceSynchronousImport);
        }

        Diagnose();
    }

    private void Diagnose()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        List<string> files = FindBattleMusicFiles();
        sb.AppendLine($"battle_music 文件数:{files.Count}");
        foreach (string f in files)
        {
            sb.AppendLine("  · " + f);
        }

        if (files.Count > 1)
        {
            sb.AppendLine("⚠ 有多个同名 battle_music 文件,运行时 Resources.Load 可能读错!用上面『设为战斗音乐』只保留一个。");
        }

        AudioClip loaded = Resources.Load<AudioClip>(ResourceName);
        if (loaded == null)
        {
            sb.AppendLine("❌ 运行时读不到 battle_music(Resources.Load 返回 null)。确认文件在 Resources/LijiangEchoAudio/ 下、名字就叫 battle_music。");
        }
        else
        {
            sb.AppendLine($"✅ 能读到:{loaded.name},时长 {loaded.length:F1}s,采样 {loaded.samples},声道 {loaded.channels},loadType {loaded.loadType}");
            if (loaded.samples <= 0)
            {
                sb.AppendLine("❌ 采样为 0 —— 文件可能损坏/为空,换一个音频重设。");
            }
            else
            {
                sb.AppendLine("→ 文件正常。若游戏里仍没声,基本就是 Game 视图的『🔇 Mute Audio』被点亮了,取消它即可。");
            }
        }

        report = sb.ToString();
        Debug.Log("[漓江回声战斗音乐诊断]\n" + report);
    }

    private static List<string> FindBattleMusicFiles()
    {
        List<string> result = new List<string>();
        if (!AssetDatabase.IsValidFolder(Dir))
        {
            return result;
        }

        foreach (string guid in AssetDatabase.FindAssets("battle_music", new[] { Dir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(p) == "battle_music" && Path.GetExtension(p) != ".meta")
            {
                result.Add(p);
            }
        }

        return result;
    }

    private static string ActivePlatform()
    {
        switch (EditorUserBuildSettings.activeBuildTarget)
        {
            case BuildTarget.Android: return "Android";
            case BuildTarget.iOS: return "iPhone";
            case BuildTarget.WebGL: return "WebGL";
            default: return "Standalone";
        }
    }
}
