using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 从战斗音乐自动生成谱面(时间点)的编辑器工具。
/// 原理:读取 battle_music 音频 → 计算能量包络 → 取"能量骤升"(起音/拍子)作为音符时间点,
/// 即代码里 noteTimes 注释所说的"依据音乐瞬态峰值生成"。这样谱面就是跟着音乐拍子来的。
///
/// ⚠️ 起音检测靠阈值,不同曲子松紧不同。若生成的点太多/太少,调下面 Sensitivity / MinGapSeconds
/// 再跑一次。音频判断不出"单击/双击/长按"(那是设计意图),生成的点默认全 single;
/// 类型可用需求表(chart_liusanjie.txt)另行贴到最近的点上(菜单里的"贴类型")。
/// </summary>
public static class LijiangEchoChartGenerator
{
    private const string ClipResourcePath = "LijiangEchoAudio/battle_music";
    private const string OutputPath = "Assets/Resources/LijiangEchoCharts/chart_generated.txt";
    private const string RequirementChartPath = "Assets/Resources/LijiangEchoCharts/chart_liusanjie.txt";

    // —— 可调参数 ——
    private const float Sensitivity = 1.5f;      // 越大越"挑剔"、点越少;越小点越多
    private const float MinGapSeconds = 0.16f;   // 两个音符最小间隔,防止一个鼓点出好几下
    private const int FrameSize = 1024;
    private const int HopSize = 512;
    private const float SnapWindowSeconds = 0.3f; // 贴类型时,需求点吸附到最近检测点的最大距离

    [MenuItem("漓江回声/谱面/1. 从音乐检测拍子生成谱面")]
    public static void GenerateFromMusic()
    {
        AudioClip clip = Resources.Load<AudioClip>(ClipResourcePath);
        if (clip == null)
        {
            EditorUtility.DisplayDialog("找不到音乐", "未找到 Resources/" + ClipResourcePath + "。请确认战斗音乐存在。", "好");
            return;
        }

        float[] onsets = DetectOnsets(clip, out int frameCount);
        if (onsets == null)
        {
            EditorUtility.DisplayDialog("读取失败", "无法读取音频采样(可能需要在导入设置里勾选 Decompress On Load / Load Type)。", "好");
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# 从 " + ClipResourcePath + " 自动生成(能量起音检测) —— 时间(秒),类型");
        sb.AppendLine("# 参数 Sensitivity=" + Sensitivity + " MinGap=" + MinGapSeconds + "s;点太多/少改这两个再跑。");
        sb.AppendLine("# 类型暂全 single,可用菜单「2. 把需求类型贴到最近拍子」写入单/双/长按。");
        foreach (float t in onsets)
        {
            sb.AppendLine(t.ToString("F3") + ",single");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(OutputPath)));
        File.WriteAllText(Path.GetFullPath(OutputPath), sb.ToString());
        AssetDatabase.Refresh();

        Debug.Log($"[漓江回声谱面] 从音乐检测到 {onsets.Length} 个拍子点(分析 {frameCount} 帧),已写入 {OutputPath}");
        EditorUtility.DisplayDialog(
            "已生成谱面",
            $"从音乐检测到 {onsets.Length} 个拍子点,写入:\n{OutputPath}\n\n" +
            "点太多/太少 → 改脚本里 Sensitivity / MinGapSeconds 再跑。\n" +
            "接着可用「2. 把需求类型贴到最近拍子」把单/双/长按写上去。",
            "好");
    }

    [MenuItem("漓江回声/谱面/2. 把需求类型贴到最近拍子")]
    public static void SnapRequirementTypes()
    {
        string genFull = Path.GetFullPath(OutputPath);
        string reqFull = Path.GetFullPath(RequirementChartPath);
        if (!File.Exists(genFull))
        {
            EditorUtility.DisplayDialog("缺少生成谱面", "请先执行「1. 从音乐检测拍子生成谱面」。", "好");
            return;
        }

        if (!File.Exists(reqFull))
        {
            EditorUtility.DisplayDialog("缺少需求表", "未找到 " + RequirementChartPath + "。", "好");
            return;
        }

        List<float> genTimes = new List<float>();
        foreach (string line in File.ReadAllLines(genFull))
        {
            if (TryParseRow(line, out float t, out _))
            {
                genTimes.Add(t);
            }
        }

        // 每个需求音符(时间,类型)吸附到最近的检测拍子点,给它写上类型。
        string[] genTypes = new string[genTimes.Count];
        for (int i = 0; i < genTypes.Length; i++)
        {
            genTypes[i] = "single";
        }

        int matched = 0;
        foreach (string line in File.ReadAllLines(reqFull))
        {
            if (!TryParseRow(line, out float rt, out string rtype))
            {
                continue;
            }

            int best = -1;
            float bestDist = SnapWindowSeconds;
            for (int i = 0; i < genTimes.Count; i++)
            {
                float d = Mathf.Abs(genTimes[i] - rt);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = i;
                }
            }

            if (best >= 0)
            {
                genTypes[best] = rtype;
                matched++;
            }
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# 检测拍子 + 需求类型(吸附窗口 " + SnapWindowSeconds + "s) —— 时间(秒),类型");
        for (int i = 0; i < genTimes.Count; i++)
        {
            sb.AppendLine(genTimes[i].ToString("F3") + "," + genTypes[i]);
        }

        File.WriteAllText(genFull, sb.ToString());
        AssetDatabase.Refresh();

        Debug.Log($"[漓江回声谱面] 已把 {matched} 个需求类型贴到最近拍子点,写回 {OutputPath}");
        EditorUtility.DisplayDialog("已贴类型", $"把 {matched} 个需求音符的类型(单/双/长按)吸附到了最近的拍子点。\n结果在 {OutputPath}。", "好");
    }

    /// <summary>能量-通量起音检测:返回按时间升序的起音时间点(秒)。</summary>
    private static float[] DetectOnsets(AudioClip clip, out int frameCount)
    {
        frameCount = 0;
        int channels = clip.channels;
        int sampleRate = clip.frequency;
        int totalSamples = clip.samples;
        if (totalSamples <= FrameSize)
        {
            return null;
        }

        float[] raw = new float[totalSamples * channels];
        if (!clip.GetData(raw, 0))
        {
            return null;
        }

        // 下混单声道
        float[] mono = new float[totalSamples];
        for (int i = 0; i < totalSamples; i++)
        {
            float sum = 0f;
            for (int c = 0; c < channels; c++)
            {
                sum += raw[i * channels + c];
            }

            mono[i] = sum / channels;
        }

        int frames = (totalSamples - FrameSize) / HopSize;
        frameCount = frames;
        float[] energy = new float[frames];
        for (int f = 0; f < frames; f++)
        {
            int start = f * HopSize;
            float e = 0f;
            for (int j = 0; j < FrameSize; j++)
            {
                float v = mono[start + j];
                e += v * v;
            }

            energy[f] = Mathf.Sqrt(e / FrameSize);
        }

        // 通量 = 能量正向差分(只取上升)
        float[] flux = new float[frames];
        for (int f = 1; f < frames; f++)
        {
            float d = energy[f] - energy[f - 1];
            flux[f] = d > 0f ? d : 0f;
        }

        // 局部自适应阈值 + 峰值挑选 + 最小间隔
        List<float> onsets = new List<float>();
        const int meanWin = 20;
        float lastOnset = -10f;
        for (int f = 2; f < frames - 1; f++)
        {
            int a = Mathf.Max(0, f - meanWin);
            int b = Mathf.Min(frames - 1, f + meanWin);
            float mean = 0f;
            for (int k = a; k <= b; k++)
            {
                mean += flux[k];
            }

            mean /= (b - a + 1);
            float threshold = mean * Sensitivity + 1e-4f;

            if (flux[f] > threshold && flux[f] >= flux[f - 1] && flux[f] > flux[f + 1])
            {
                float t = (f * HopSize + FrameSize * 0.5f) / sampleRate;
                if (t - lastOnset >= MinGapSeconds)
                {
                    onsets.Add(t);
                    lastOnset = t;
                }
            }
        }

        return onsets.ToArray();
    }

    private static bool TryParseRow(string line, out float time, out string type)
    {
        time = 0f;
        type = "single";
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        string s = line.Trim();
        if (s.StartsWith("#"))
        {
            return false;
        }

        string[] parts = s.Split(',');
        if (parts.Length < 1 || !float.TryParse(parts[0].Trim(), out time))
        {
            return false;
        }

        if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
        {
            type = parts[1].Trim();
        }

        return true;
    }
}
