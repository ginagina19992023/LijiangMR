using UnityEditor;
using UnityEngine;

/// <summary>
/// 谱面预览/调节窗口:菜单「漓江回声/谱面/0. 打开预览窗口」。
/// 用滑条实时调"灵敏度 / 最小间隔",点「检测预览」在时间轴上看到所有拍子点(还没写文件),
/// 满意了再点「写入谱面」生成 chart_generated.txt;需要类型再点「贴需求类型」。
/// 这样不用改脚本、不用反复盲跑,所见即所得。
/// </summary>
public class LijiangEchoChartWindow : EditorWindow
{
    private float sensitivity = 1.5f;
    private float minGap = 0.16f;

    private float[] onsets;       // 上次检测结果(秒)
    private float clipLength;     // 音频总长(秒),用于时间轴比例
    private string status = "点「检测预览」开始。";

    [MenuItem("漓江回声/谱面/0. 打开预览窗口")]
    public static void Open()
    {
        LijiangEchoChartWindow w = GetWindow<LijiangEchoChartWindow>("谱面预览");
        w.minSize = new Vector2(460f, 300f);
        w.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("从战斗音乐检测拍子 · 预览调节", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "前置:选 Resources/" + LijiangEchoChartGenerator.ClipResourcePath +
            " → Load Type = Decompress On Load + 勾 Preload Audio Data → Apply。",
            MessageType.Info);

        EditorGUILayout.Space(4f);
        sensitivity = EditorGUILayout.Slider(new GUIContent("灵敏度 Sensitivity", "越大越挑剔、点越少;越小点越多"), sensitivity, 0.5f, 4f);
        minGap = EditorGUILayout.Slider(new GUIContent("最小间隔(秒)", "两个音符最近间隔,防一个鼓点出好几下"), minGap, 0.05f, 0.5f);

        EditorGUILayout.Space(4f);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("① 检测预览", GUILayout.Height(28f)))
            {
                Detect();
            }

            using (new EditorGUI.DisabledScope(onsets == null || onsets.Length == 0))
            {
                if (GUILayout.Button("② 写入谱面", GUILayout.Height(28f)))
                {
                    LijiangEchoChartGenerator.WriteChart(onsets, sensitivity, minGap);
                    status = "已写入 " + onsets.Length + " 个拍子到 chart_generated.txt。";
                }

                if (GUILayout.Button("③ 贴需求类型", GUILayout.Height(28f)))
                {
                    LijiangEchoChartGenerator.SnapRequirementTypes();
                    status = "已把需求表类型贴到最近拍子(见 chart_generated.txt)。";
                }
            }
        }

        EditorGUILayout.Space(6f);
        DrawTimeline();

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField(status, EditorStyles.wordWrappedMiniLabel);
        if (onsets != null)
        {
            EditorGUILayout.LabelField($"拍子数:{onsets.Length}    时长:{clipLength:F1}s    平均间隔:{(onsets.Length > 1 ? clipLength / onsets.Length : 0f):F2}s",
                EditorStyles.miniLabel);
        }
    }

    private void Detect()
    {
        AudioClip clip = Resources.Load<AudioClip>(LijiangEchoChartGenerator.ClipResourcePath);
        if (clip == null)
        {
            status = "找不到 Resources/" + LijiangEchoChartGenerator.ClipResourcePath;
            onsets = null;
            return;
        }

        clipLength = clip.length;
        onsets = LijiangEchoChartGenerator.DetectOnsets(clip, sensitivity, minGap, out int _);
        if (onsets == null)
        {
            status = "读采样失败:请把该音频导入设置改成 Decompress On Load 再 Apply。";
            return;
        }

        status = $"检测到 {onsets.Length} 个拍子点(还没写文件,可继续调滑条再检测)。";
    }

    /// <summary>把所有拍子点画成时间轴上的竖线,直观看疏密。</summary>
    private void DrawTimeline()
    {
        Rect rect = GUILayoutUtility.GetRect(100f, 60f, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(0.12f, 0.12f, 0.14f));

        if (onsets == null || onsets.Length == 0 || clipLength <= 0f)
        {
            EditorGUI.LabelField(rect, "  (时间轴:检测后在这里显示每个拍子)", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        // 每秒一条淡淡的刻度参考线
        Color grid = new Color(1f, 1f, 1f, 0.06f);
        for (int s = 1; s < clipLength; s++)
        {
            float gx = rect.x + (s / clipLength) * rect.width;
            EditorGUI.DrawRect(new Rect(gx, rect.y, 1f, rect.height), grid);
        }

        // 拍子点:金色竖线
        Color tick = new Color(1f, 0.85f, 0.35f, 0.95f);
        foreach (float t in onsets)
        {
            float x = rect.x + Mathf.Clamp01(t / clipLength) * rect.width;
            EditorGUI.DrawRect(new Rect(x, rect.y + 4f, 1.5f, rect.height - 8f), tick);
        }
    }
}
