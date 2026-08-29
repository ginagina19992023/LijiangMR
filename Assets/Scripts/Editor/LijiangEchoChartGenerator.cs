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
    // 预览窗口(LijiangEchoChartWindow)也会用到这些常量/方法,故设为 internal。
    internal const string ClipResourcePath = "LijiangEchoAudio/battle_music";
    internal const string OutputPath = "Assets/Resources/LijiangEchoCharts/chart_generated.txt";
    internal const string RequirementChartPath = "Assets/Resources/LijiangEchoCharts/chart_liusanjie.txt";
    internal const string ChartFolder = "Assets/Resources/LijiangEchoCharts/";

    // 战斗关卡名(与运行时 levelNames 对应:0 蛙纹 / 1 鸟纹 / 2 鱼纹)。
    internal static readonly string[] LevelNames = { "蛙纹", "鸟纹", "鱼纹" };

    /// <summary>某关卡专属谱面文件路径:chart_level{N}.txt。运行时会优先读它。</summary>
    internal static string ChartPathForLevel(int level)
    {
        return ChartFolder + "chart_level" + Mathf.Clamp(level, 0, LevelNames.Length - 1) + ".txt";
    }

    // —— 可调参数(菜单命令用这些默认值;预览窗口用滑条实时传参) ——
    private const float Sensitivity = 1.5f;      // 越大越"挑剔"、点越少;越小点越多
    private const float MinGapSeconds = 0.16f;   // 两个音符最小间隔,防止一个鼓点出好几下
    private const int FrameSize = 1024;
    private const int HopSize = 512;
    internal const float SnapWindowSeconds = 0.3f; // 贴类型时,需求点吸附到最近检测点的最大距离

    [MenuItem("漓江回声/谱面/1. 从音乐检测拍子生成谱面")]
    public static void GenerateFromMusic()
    {
        AudioClip clip = Resources.Load<AudioClip>(ClipResourcePath);
        if (clip == null)
        {
            EditorUtility.DisplayDialog("找不到音乐", "未找到 Resources/" + ClipResourcePath + "。请确认战斗音乐存在。", "好");
            return;
        }

        float[] onsets = DetectOnsets(clip, Sensitivity, MinGapSeconds, out int frameCount);
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

    /// <summary>把检测到的时间点写成 chart_generated.txt(类型默认 single)。预览窗口"写入"复用。</summary>
    internal static void WriteChart(float[] onsets, float sensitivity, float minGap)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# 从 " + ClipResourcePath + " 自动生成(能量起音检测) —— 时间(秒),类型");
        sb.AppendLine("# 参数 Sensitivity=" + sensitivity + " MinGap=" + minGap + "s;点太多/少改参数再跑。");
        sb.AppendLine("# 类型暂全 single,可用「2. 把需求类型贴到最近拍子」写入单/双/长按。");
        foreach (float t in onsets)
        {
            sb.AppendLine(t.ToString("F3") + ",single");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(OutputPath)));
        File.WriteAllText(Path.GetFullPath(OutputPath), sb.ToString());
        AssetDatabase.Refresh();
    }

    /// <summary>能量-通量起音检测:返回按时间升序的起音时间点(秒)。sensitivity/minGap 可由预览窗口传入。</summary>
    internal static float[] DetectOnsets(AudioClip clip, float sensitivity, float minGap, out int frameCount)
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
            float threshold = mean * sensitivity + 1e-4f;

            if (flux[f] > threshold && flux[f] >= flux[f - 1] && flux[f] > flux[f + 1])
            {
                float t = (f * HopSize + FrameSize * 0.5f) / sampleRate;
                if (t - lastOnset >= minGap)
                {
                    onsets.Add(t);
                    lastOnset = t;
                }
            }
        }

        return onsets.ToArray();
    }

    /// <summary>
    /// 编辑器保存:把逐个音符的(时间,类型)写成 chart_generated.txt,带「# types:explicit」头,
    /// 运行时据此只认显式类型、不再取模自动 swipe(所见即所得)。按时间升序写出。
    /// </summary>
    internal static void WriteChartExplicit(List<float> times, List<string> types)
    {
        WriteChartExplicit(times, types, OutputPath);
    }

    /// <summary>同上,但可指定目标文件(用于"应用到某个战斗场景"→ chart_level{N}.txt)。</summary>
    internal static void WriteChartExplicit(List<float> times, List<string> types, string targetPath)
    {
        // 组装成对并按时间排序
        List<KeyValuePair<float, string>> rows = new List<KeyValuePair<float, string>>();
        for (int i = 0; i < times.Count; i++)
        {
            string type = (types != null && i < types.Count && !string.IsNullOrWhiteSpace(types[i]))
                ? types[i].Trim().ToLowerInvariant()
                : "single";
            rows.Add(new KeyValuePair<float, string>(times[i], type));
        }

        rows.Sort((a, b) => a.Key.CompareTo(b.Key));

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# 漓江回声谱面(编辑器保存) —— 时间(秒),类型");
        sb.AppendLine("# types:explicit  ← 有此头:运行时逐音符只认下面写的类型(single/double/hold/swipe),不再自动生成。");
        sb.AppendLine("# 类型:single=单击 double=双击 hold=长按 swipe=挥划");
        foreach (KeyValuePair<float, string> row in rows)
        {
            sb.AppendLine(row.Key.ToString("F3") + "," + row.Value);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetPath)));
        File.WriteAllText(Path.GetFullPath(targetPath), sb.ToString());
        AssetDatabase.Refresh();
    }

    /// <summary>编辑器打开时:尝试读回已有 chart_generated.txt 的(时间,类型),供继续编辑。没有则返回 false。</summary>
    internal static bool TryLoadChartRows(out List<float> times, out List<string> types)
    {
        return TryLoadChartRows(OutputPath, out times, out types);
    }

    /// <summary>从指定谱面文件读回(时间,类型),供编辑器"选源谱"载入。没有/为空返回 false。</summary>
    internal static bool TryLoadChartRows(string sourcePath, out List<float> times, out List<string> types)
    {
        times = new List<float>();
        types = new List<string>();
        if (string.IsNullOrEmpty(sourcePath))
        {
            return false;
        }

        string full = Path.GetFullPath(sourcePath);
        if (!File.Exists(full))
        {
            return false;
        }

        foreach (string line in File.ReadAllLines(full))
        {
            if (TryParseRow(line, out float t, out string type))
            {
                times.Add(t);
                types.Add(type);
            }
        }

        return times.Count > 0;
    }

    /// <summary>
    /// 为编辑器时间轴生成波形包络:把整曲下混单声道后,按 buckets 段各取 RMS(0..1)。
    /// 让拖动播放条时能"看到这段音乐是强是弱"。读采样失败返回 null。
    /// </summary>
    internal static float[] BuildWaveformEnvelope(AudioClip clip, int buckets)
    {
        if (clip == null || buckets <= 0)
        {
            return null;
        }

        int channels = Mathf.Max(1, clip.channels);
        int totalSamples = clip.samples;
        if (totalSamples <= 0)
        {
            return null;
        }

        float[] raw = new float[totalSamples * channels];
        if (!clip.GetData(raw, 0))
        {
            return null;
        }

        float[] env = new float[buckets];
        float peak = 1e-5f;
        for (int b = 0; b < buckets; b++)
        {
            long s0 = (long)totalSamples * b / buckets;
            long s1 = (long)totalSamples * (b + 1) / buckets;
            if (s1 <= s0)
            {
                s1 = s0 + 1;
            }

            double sum = 0.0;
            long count = 0;
            // 段内降采样,最多取 ~512 个样本估 RMS,省时。
            long stride = Mathf.Max(1, (int)((s1 - s0) / 512));
            for (long s = s0; s < s1 && s < totalSamples; s += stride)
            {
                float v = 0f;
                for (int c = 0; c < channels; c++)
                {
                    v += raw[s * channels + c];
                }

                v /= channels;
                sum += (double)v * v;
                count++;
            }

            float rms = count > 0 ? Mathf.Sqrt((float)(sum / count)) : 0f;
            env[b] = rms;
            if (rms > peak)
            {
                peak = rms;
            }
        }

        // 归一化到 0..1
        for (int b = 0; b < buckets; b++)
        {
            env[b] = Mathf.Clamp01(env[b] / peak);
        }

        return env;
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
