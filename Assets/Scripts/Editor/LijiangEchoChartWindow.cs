using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 谱面时间轴编辑器:菜单「漓江回声/谱面/0. 打开预览窗口」。
///
/// 能力:
///  · 从战斗音乐检测拍子(灵敏度/最小间隔滑条),或读回已有 chart_generated.txt 继续编辑;
///  · 时间轴上带「波形包络」背景,拖动播放头到任意位置,松手即试听那一段音乐,
///    播放时播放头会跟着走 —— 直观看到"这段音乐对应哪些打击点";
///  · 点选任意音符,改它的类型(单击 single / 双击 double / 长按 hold / 挥划 swipe),可增删;
///  · 保存写出带「# types:explicit」头的谱面,运行时逐音符只认你设的类型(所见即所得)。
///
/// 前置:选中 Resources/LijiangEchoAudio/battle_music → Load Type = Decompress On Load
///       + 勾 Preload Audio Data → Apply(检测/波形/试听都要读采样)。
/// </summary>
public class LijiangEchoChartWindow : EditorWindow
{
    // —— 检测参数 ——
    private float sensitivity = 1.5f;
    private float minGap = 0.16f;
    private int targetBeatCount = 64; // 目标拍子数(按数量生成/切分)
    private int bandIndex; // 检测频段:0全频 1低频鼓点 2中频管乐
    private static readonly string[] BandLabels = { "全频", "低频·鼓点", "中频·管乐" };

    private void BandRange(out float lowHz, out float highHz)
    {
        switch (bandIndex)
        {
            case 1: lowHz = 20f; highHz = 150f; break;   // 鼓点(底鼓/军鼓的低频冲击)
            case 2: lowHz = 300f; highHz = 2000f; break; // 管乐/唢呐等中频谐波
            default: lowHz = 0f; highHz = 0f; break;     // 全频
        }
    }

    // —— 数据模型:图层(每层一组拍点),可分离/合并/切换可见 ——
    private sealed class NoteLayer
    {
        public string name = "图层";
        public Color color = Color.white;
        public bool visible = true;
        public readonly List<float> times = new List<float>();
        public readonly List<string> types = new List<string>();
    }

    private readonly List<NoteLayer> layers = new List<NoteLayer>();
    private int activeLayer;

    // 现有编辑逻辑都用 noteTimes/noteTypes → 指向"当前图层",零改动即作用于当前层。
    private List<float> noteTimes => layers[activeLayer].times;
    private List<string> noteTypes => layers[activeLayer].types;
    private int selected = -1;

    private static readonly Color[] LayerPalette =
    {
        new Color(0.95f, 0.95f, 0.95f), new Color(1f, 0.6f, 0.25f), new Color(0.4f, 0.8f, 1f),
        new Color(0.5f, 0.9f, 0.5f), new Color(1f, 0.5f, 0.8f), new Color(0.8f, 0.7f, 1f)
    };

    private void EnsureLayers()
    {
        if (layers.Count == 0)
        {
            layers.Add(new NoteLayer { name = "主图层", color = LayerPalette[0] });
        }

        activeLayer = Mathf.Clamp(activeLayer, 0, layers.Count - 1);
    }

    // —— 源谱(从哪张读入编辑) / 目标战斗场景(保存应用到哪张) ——
    private int sourceIndex;
    private int targetIndex;

    // —— 音频 / 检测 / 波形 ——
    private AudioClip clip;
    private float clipLength;
    private int sampleRate = 44100;
    private float[] onsets;        // 最近一次检测结果(秒),作为浅色参考点
    private float[] waveform;      // 波形包络 0..1
    private const int WaveformBuckets = 3000;

    // —— 时间轴视图 ——
    private float pixelsPerSecond = 80f;
    private Vector2 scroll;
    private float playhead;        // 播放头时间(秒)
    private bool draggingPlayhead;
    private int draggingNote = -1;         // 正在拖动的音符下标(-1=没拖)
    private bool noteDragUndoRecorded;     // 本次拖动是否已记过一次撤销
    private bool autoPreview = true; // 松开播放头即试听
    private bool follow = true;      // 播放时视图跟随播放头

    // —— 播放状态(播放头按真实时间推进,不依赖会被节拍声污染的 AudioUtil.Pos()) ——
    private bool isPlaying;
    private double playStartRealtime;
    private float playStartHead;

    // —— 节拍试听:播放/拖动时播放头经过音符就叠一声(按类型),不进 Play 也能听谱对齐 ——
    private bool metronome; // 默认关(叠音走 AudioUtil,可能干扰音乐;需要时手动开)
    private bool musicMuted; // 只放拍子(静音音乐)——不放音乐、只按拍点响,避免音乐/点击叠音冲突
    private float lastClickPlayhead = -1f;
    private float clickVolume = 0.9f; // 提示音响度
    private AudioClip genSingle, genHold, genSwipe; // 程序生成的清脆"咔哒",响度可控
    private float genVolume = -1f;

    private string status = "点「检测拍子」或「读回已有谱面」开始。";

    private static readonly string[] TypeOptions = { "single", "double", "hold", "swipe" };
    private static readonly string[] TypeLabels = { "单击 single", "双击 double", "长按 hold", "挥划 swipe" };
    private static readonly string[] TypeShort = { "单击", "双击", "长按", "挥划" };

    // —— 批量/自动类型 面板(作用于当前图层) ——
    private bool showBatch = true;
    private int batchAllType;               // "整层全设为"的目标类型
    private int batchFrom = 1;              // 替换/删除的源类型(默认双击)
    private int batchTo;                    // 替换目标类型
    private readonly bool[] ratioOn = { true, true, true, false };  // 比例分配勾选哪些类型
    private readonly float[] ratioWeight = { 6f, 3f, 1f, 1f };      // 各类型相对权重

    // —— 多选(当前图层内的音符下标) ——
    private readonly HashSet<int> selection = new HashSet<int>();

    // —— 撤销 / 重做(自定义栈:快照全部图层) ——
    private sealed class LayerSnap { public string name; public Color color; public bool visible; public List<float> times; public List<string> types; }
    private sealed class Snapshot { public List<LayerSnap> layers; public int active; }
    private readonly List<Snapshot> undoStack = new List<Snapshot>();
    private readonly List<Snapshot> redoStack = new List<Snapshot>();

    // 源谱(载入编辑):三关专属谱 + 全局生成谱 + 需求谱。
    private static readonly string[] SourceLabels =
    {
        "本关·蛙纹 (level0)", "本关·鸟纹 (level1)", "本关·鱼纹 (level2)",
        "全局 chart_generated", "需求 chart_liusanjie", "草稿 chart_draft"
    };

    // 目标战斗场景(保存应用到):三关专属谱 + 全局默认。
    private static readonly string[] TargetLabels =
    {
        "蛙纹关卡 (level0)", "鸟纹关卡 (level1)", "鱼纹关卡 (level2)", "全局默认 (chart_generated)"
    };

    private static string SourcePath(int i)
    {
        switch (i)
        {
            case 0: return LijiangEchoChartGenerator.ChartPathForLevel(0);
            case 1: return LijiangEchoChartGenerator.ChartPathForLevel(1);
            case 2: return LijiangEchoChartGenerator.ChartPathForLevel(2);
            case 3: return LijiangEchoChartGenerator.OutputPath;
            case 4: return LijiangEchoChartGenerator.RequirementChartPath;
            default: return LijiangEchoChartGenerator.DraftPath; // 草稿
        }
    }

    private static string TargetPath(int i)
    {
        return i <= 2 ? LijiangEchoChartGenerator.ChartPathForLevel(i) : LijiangEchoChartGenerator.OutputPath;
    }

    [MenuItem("漓江回声/谱面/0. 打开预览窗口")]
    public static void Open()
    {
        LijiangEchoChartWindow w = GetWindow<LijiangEchoChartWindow>("谱面编辑器");
        w.minSize = new Vector2(720f, 460f);
        w.Show();
    }

    private void OnEnable()
    {
        EnsureLayers();
        EnsureClip();
        // 尝试读回已有谱面,方便接着编辑
        if (LijiangEchoChartGenerator.TryLoadChartRows(out List<float> t, out List<string> ty))
        {
            noteTimes.Clear();
            noteTypes.Clear();
            noteTimes.AddRange(t);
            noteTypes.AddRange(ty);
            status = $"读回已有谱面 {noteTimes.Count} 个音符,可继续编辑。";
        }

        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
        StopPreview();
    }

    private void EnsureClip()
    {
        if (clip != null)
        {
            return;
        }

        clip = Resources.Load<AudioClip>(LijiangEchoChartGenerator.ClipResourcePath);
        if (clip != null)
        {
            clipLength = clip.length;
            sampleRate = Mathf.Max(1, clip.frequency);
        }
    }

    private void OnEditorUpdate()
    {
        if (!isPlaying)
        {
            return;
        }

        // 播放头按真实时间推进(不读 AudioUtil.Pos(),因为节拍声会污染它导致卡在第一拍)。
        float prev = playhead;
        float newHead = playStartHead + (float)(EditorApplication.timeSinceStartup - playStartRealtime);
        if (newHead >= clipLength)
        {
            playhead = clipLength;
            StopPreview();
            Repaint();
            return;
        }

        playhead = Mathf.Clamp(newHead, 0f, clipLength);
        if (metronome || musicMuted) // 只放拍子模式下始终响拍
        {
            PlayMetronomeClicks(lastClickPlayhead >= 0f ? lastClickPlayhead : prev, playhead);
            lastClickPlayhead = playhead;
        }

        if (follow)
        {
            EnsureVisible(playhead);
        }

        Repaint();
    }

    private void OnGUI()
    {
        EnsureLayers();
        HandleUndoRedoKeys();
        DrawToolbar();
        EditorGUILayout.Space(2f);
        DrawLayersBar();
        DrawTimelineToolbar();
        DrawTimeline();
        EditorGUILayout.Space(4f);
        DrawSelectedEditor();
        DrawBatchTools();
        EditorGUILayout.Space(4f);
        DrawSourceTargetBar();
        DrawBottomBar();
    }

    // ======================= 源谱 / 目标战斗场景 =======================
    private void DrawSourceTargetBar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.Label("源谱", GUILayout.MaxWidth(30f));
            sourceIndex = EditorGUILayout.Popup(sourceIndex, SourceLabels, GUILayout.MaxWidth(180f));
            if (GUILayout.Button(new GUIContent("📂 载入源谱", "把所选源谱读进编辑器(替换当前音符)"), GUILayout.MaxWidth(110f)))
            {
                LoadFromSource();
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label("应用到", GUILayout.MaxWidth(44f));
            targetIndex = EditorGUILayout.Popup(targetIndex, TargetLabels, GUILayout.MaxWidth(180f));
            using (new EditorGUI.DisabledScope(noteTimes.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("📝 存草稿", "先存成草稿(不写到关卡),之后「源谱」选『草稿』载入,确定后再保存到关卡"), GUILayout.MaxWidth(80f)))
                {
                    SaveDraft();
                }

                if (GUILayout.Button(new GUIContent("💾 保存到该场景", "把当前音符表写成该战斗场景的谱面(带 types:explicit);覆盖前会自动备份旧谱面"), GUILayout.MaxWidth(130f)))
                {
                    SaveToTarget();
                }

                if (GUILayout.Button(new GUIContent("▶ 保存并试玩", "保存到该关卡 → 直接进 Play 进入战斗(从头):边听音乐边看纹样飞入、可打点验证。停止 Play 回到编辑"), GUILayout.MaxWidth(110f)))
                {
                    SaveAndPlaytest();
                }

                if (GUILayout.Button(new GUIContent($"▶ 从播放头({playhead:F1}s)试玩", "保存 → 进 Play 战斗并从当前播放头时间起播(跳过倒计时),直接看该时刻的打击效果"), GUILayout.MaxWidth(150f)))
                {
                    SaveAndPlaytest(playhead);
                }
            }
        }
    }

    /// <summary>保存当前谱面到目标关卡,并直接进入 Play 的战斗阶段试玩;startTime&gt;=0 时从该秒起播(跳过倒计时)。</summary>
    private void SaveAndPlaytest(float startTime = -1f)
    {
        SaveToTarget();

        int level = targetIndex <= 2 ? targetIndex : 0;
        PlayerPrefs.SetInt("LJ_DebugStartStage", 4); // 4 = 战斗(见 JumpToStageForDebug)
        PlayerPrefs.SetInt("LJ_DebugLevel", level);
        if (startTime >= 0f)
        {
            PlayerPrefs.SetFloat("LJ_DebugBattleStartTime", startTime); // 从播放头起播
        }
        else
        {
            PlayerPrefs.DeleteKey("LJ_DebugBattleStartTime");
        }

        PlayerPrefs.Save();

        // 战斗只在 LijiangEchoMR_Main 里 bootstrap;若当前不是它,先(询问保存后)打开它再 Play。
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            status = "已取消(未保存当前场景)。";
            return;
        }

        string mainPath = FindScenePath("LijiangEchoMR_Main");
        if (!string.IsNullOrEmpty(mainPath) && SceneManager.GetActiveScene().path != mainPath)
        {
            EditorSceneManager.OpenScene(mainPath, OpenSceneMode.Single);
        }

        StopPreview();
        EditorApplication.EnterPlaymode();
        status = "已保存并进入战斗试玩:听音乐/看纹样/打点验证。停止 Play 即回到本编辑器。";
    }

    private static string FindScenePath(string sceneName)
    {
        foreach (string guid in AssetDatabase.FindAssets(sceneName + " t:Scene"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == sceneName)
            {
                return path;
            }
        }

        return null;
    }

    private void LoadFromSource()
    {
        string path = SourcePath(sourceIndex);
        if (LijiangEchoChartGenerator.TryLoadChartRows(path, out List<float> t, out List<string> ty))
        {
            RecordUndo();
            noteTimes.Clear();
            noteTypes.Clear();
            noteTimes.AddRange(t);
            noteTypes.AddRange(ty);
            selected = -1;
            status = $"已从「{SourceLabels[sourceIndex]}」载入 {noteTimes.Count} 个音符。";
        }
        else
        {
            status = $"源谱「{SourceLabels[sourceIndex]}」不存在或为空({path})。";
        }
    }

    private void SaveToTarget()
    {
        CollectVisibleNotes(out List<float> t, out List<string> ty);
        string path = TargetPath(targetIndex);
        string bak = LijiangEchoChartGenerator.BackupChartFile(path); // 覆盖前先备份
        LijiangEchoChartGenerator.WriteChartExplicit(t, ty, path);
        status = $"已保存到「{TargetLabels[targetIndex]}」:{TypeCountSummary(ty)}。{(bak != null ? "旧谱面已备份→" + bak : "")}";
    }

    /// <summary>先存成草稿(chart_draft),之后再「保存到该场景」应用到关卡。</summary>
    private void SaveDraft()
    {
        CollectVisibleNotes(out List<float> t, out List<string> ty);
        LijiangEchoChartGenerator.BackupChartFile(LijiangEchoChartGenerator.DraftPath);
        LijiangEchoChartGenerator.WriteChartExplicit(t, ty, LijiangEchoChartGenerator.DraftPath);
        status = $"已存为草稿({TypeCountSummary(ty)})。「源谱」选『草稿』可再载入;确定后再「保存到该场景」应用。";
    }

    private static string TypeCountSummary(List<string> ty)
    {
        int d = 0, h = 0, sw = 0;
        foreach (string s in ty)
        {
            if (s == "double") d++;
            else if (s == "hold") h++;
            else if (s == "swipe") sw++;
        }

        return $"{ty.Count} 拍(单{ty.Count - d - h - sw} 双{d} 长{h} 划{sw})";
    }

    /// <summary>合并所有可见图层的拍点,按时间升序,去掉 20ms 内的重复。</summary>
    private void CollectVisibleNotes(out List<float> times, out List<string> types)
    {
        List<KeyValuePair<float, string>> rows = new List<KeyValuePair<float, string>>();
        foreach (NoteLayer L in layers)
        {
            if (!L.visible)
            {
                continue;
            }

            for (int i = 0; i < L.times.Count; i++)
            {
                string ty = (i < L.types.Count && !string.IsNullOrWhiteSpace(L.types[i])) ? L.types[i] : "single";
                rows.Add(new KeyValuePair<float, string>(L.times[i], ty));
            }
        }

        rows.Sort((a, b) => a.Key.CompareTo(b.Key));
        times = new List<float>();
        types = new List<string>();
        float last = -999f;
        foreach (KeyValuePair<float, string> r in rows)
        {
            if (r.Key - last < 0.02f)
            {
                continue; // 去重叠
            }

            times.Add(r.Key);
            types.Add(r.Value);
            last = r.Key;
        }
    }

    // ======================= 图层:检测到不同图层 / 切换可见 / 分离 / 合并 =======================
    private void DrawLayersBar()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            GUILayout.Label("图层（保存时合并所有『显示』的层）", EditorStyles.boldLabel);
            for (int i = 0; i < layers.Count; i++)
            {
                NoteLayer layer = layers[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool isActive = i == activeLayer;
                    bool pick = GUILayout.Toggle(isActive, GUIContent.none, EditorStyles.radioButton, GUILayout.Width(16f));
                    if (pick && !isActive)
                    {
                        activeLayer = i;
                        selected = -1;
                    }

                    layer.color = EditorGUILayout.ColorField(GUIContent.none, layer.color, false, false, false, GUILayout.Width(38f));
                    layer.name = EditorGUILayout.TextField(layer.name, GUILayout.Width(120f));
                    layer.visible = GUILayout.Toggle(layer.visible, "显示", GUILayout.Width(50f));
                    GUILayout.Label($"{layer.times.Count} 拍", GUILayout.Width(50f));
                    if (isActive)
                    {
                        GUILayout.Label("← 编辑中", GUILayout.Width(60f));
                    }

                    GUILayout.FlexibleSpace();
                    using (new EditorGUI.DisabledScope(layers.Count <= 1))
                    {
                        if (GUILayout.Button("删", GUILayout.Width(30f)))
                        {
                            RecordUndo();
                            layers.RemoveAt(i);
                            activeLayer = Mathf.Clamp(activeLayer, 0, layers.Count - 1);
                            selected = -1;
                            break;
                        }
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("＋ 新建空图层", GUILayout.Height(20f)))
                {
                    AddEmptyLayer();
                }

                if (GUILayout.Button(new GUIContent($"检测→新图层（{BandLabels[bandIndex]}）", "用上方【频段+灵敏度+最小间隔】检测全部起音,放进一个新图层(不动其它层)"), GUILayout.Height(20f)))
                {
                    DetectToNewLayer();
                }

                if (GUILayout.Button(new GUIContent("合并可见图层→新层", "把所有可见图层的拍点合成一个新图层"), GUILayout.Height(20f)))
                {
                    MergeVisibleLayers();
                }

                if (GUILayout.Button(new GUIContent("从文件导入→新图层", "读取 time,type 每行的拍点文件(如 Python 脚本 lijiang_beatmap.py 的输出)成一个新图层"), GUILayout.Height(20f)))
                {
                    ImportLayerFromFile();
                }
            }

            EditorGUILayout.LabelField(
                $"「检测→新图层」用当前设置生成：频段={BandLabels[bandIndex]}、灵敏度={sensitivity:F1}、最小间隔={minGap:F2}s(检测全部起音)。改上方设置再点即可。",
                EditorStyles.wordWrappedMiniLabel);
        }
    }

    private void ImportLayerFromFile()
    {
        string path = EditorUtility.OpenFilePanel("选择拍点文件(每行 时间,类型)", Application.dataPath, "txt");
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        List<float> t = new List<float>();
        List<string> ty = new List<string>();
        foreach (string line in System.IO.File.ReadAllLines(path))
        {
            string s = line.Trim();
            if (s.Length == 0 || s.StartsWith("#"))
            {
                continue;
            }

            string[] p = s.Split(',');
            if (p.Length < 1 || !float.TryParse(p[0].Trim(), out float tt))
            {
                continue;
            }

            t.Add(tt);
            ty.Add(p.Length >= 2 && !string.IsNullOrWhiteSpace(p[1]) ? p[1].Trim().ToLowerInvariant() : "single");
        }

        if (t.Count == 0)
        {
            status = "文件里没有可解析的拍点(应为每行 时间,类型)。";
            return;
        }

        NoteLayer L = AddEmptyLayer();
        L.name = System.IO.Path.GetFileNameWithoutExtension(path);
        for (int i = 0; i < t.Count; i++)
        {
            L.times.Add(t[i]);
            L.types.Add(ty[i]);
        }

        status = $"已从「{L.name}」导入 {t.Count} 个拍子到新图层。";
    }

    private NoteLayer AddEmptyLayer()
    {
        RecordUndo();
        NoteLayer L = new NoteLayer
        {
            name = "图层 " + (layers.Count + 1),
            color = LayerPalette[layers.Count % LayerPalette.Length]
        };
        layers.Add(L);
        activeLayer = layers.Count - 1;
        selected = -1;
        return L;
    }

    private void DetectToNewLayer()
    {
        EnsureClip();
        if (clip == null)
        {
            status = "先在「音频」栏选一首音频。";
            return;
        }

        if (!ClipReady())
        {
            PrepareAudio();
        }

        if (!ClipReady())
        {
            status = "此音频无法读采样,换一首或转 WAV。";
            return;
        }

        BandRange(out float lowHz, out float highHz);
        float[] t = LijiangEchoChartGenerator.DetectOnsetsBand(clip, sensitivity, minGap, lowHz, highHz, out int _, out float[] _s);
        waveform = LijiangEchoChartGenerator.BuildWaveformEnvelope(clip, WaveformBuckets);
        if (t == null)
        {
            status = "读采样失败。";
            return;
        }

        NoteLayer L = AddEmptyLayer();
        L.name = BandLabels[bandIndex] + " 拍";
        foreach (float x in t)
        {
            L.times.Add(x);
            L.types.Add("single");
        }

        status = $"已把「{BandLabels[bandIndex]}」检测到的 {t.Length} 个拍子放进新图层。可切换到它编辑,或隐藏其它层单独看。";
    }

    private void MergeVisibleLayers()
    {
        RecordUndo();
        CollectVisibleNotes(out List<float> t, out List<string> ty);
        NoteLayer L = new NoteLayer { name = "合并层", color = LayerPalette[layers.Count % LayerPalette.Length] };
        for (int i = 0; i < t.Count; i++)
        {
            L.times.Add(t[i]);
            L.types.Add(ty[i]);
        }

        layers.Add(L);
        activeLayer = layers.Count - 1;
        selected = -1;
        status = $"已把可见图层合并成一个新图层({t.Count} 拍)。";
    }

    // ======================= 顶部工具条 =======================
    // ======================= 音频:选择 / 准备格式 / 设为战斗音乐 =======================
    private void DrawAudioBar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.Label("音频", GUILayout.MaxWidth(30f));
            AudioClip picked = (AudioClip)EditorGUILayout.ObjectField(clip, typeof(AudioClip), false, GUILayout.MaxWidth(240f));
            if (picked != clip)
            {
                clip = picked;
                waveform = null;
                onsets = null;
                if (clip != null)
                {
                    clipLength = clip.length;
                    sampleRate = Mathf.Max(1, clip.frequency);
                    status = $"已选音频「{clip.name}」。若检测/试听没采样,点「准备音频」。";
                }
            }

            using (new EditorGUI.DisabledScope(clip == null))
            {
                if (GUILayout.Button(new GUIContent("准备音频", "设为 Decompress On Load + 预加载;mp3/其它格式都能检测/试听"), GUILayout.MaxWidth(80f)))
                {
                    PrepareAudio();
                }

                if (GUILayout.Button(new GUIContent("设为战斗音乐", "复制到 Resources/LijiangEchoAudio/battle_music,让运行时用这首"), GUILayout.MaxWidth(100f)))
                {
                    SetAsBattleMusic();
                }
            }

            if (GUILayout.Button(new GUIContent("诊断音频", "检查编辑器音频接口(播放/停止/节拍声)是否可用"), GUILayout.MaxWidth(70f)))
            {
                status = "音频接口:" + AudioPreview.Diag();
                Debug.Log("[漓江回声] " + status);
            }
        }
    }

    /// <summary>把选中音频的导入设置改成 Decompress On Load + 预加载,让 GetData(检测/波形/试听)能读采样。</summary>
    private void PrepareAudio()
    {
        string path = AssetDatabase.GetAssetPath(clip);
        AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (importer == null)
        {
            status = "这不是项目里的可导入音频资源。";
            return;
        }

        ForceDecompressOnLoad(importer);

        clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip != null)
        {
            clipLength = clip.length;
            sampleRate = Mathf.Max(1, clip.frequency);
        }

        waveform = null;
        onsets = null;
        status = ClipReady()
            ? "已设为 Decompress On Load(并清除各平台 Override)。点「检测拍子」开始。"
            : "已改导入设置,但当前平台仍非 Decompress。请在音频 Inspector 顶部各平台标签页取消勾选『Override』后再 Apply。";
    }

    /// <summary>
    /// 强制把音频设为 Decompress On Load。关键:先清掉各平台的 Override —— 用户反映"改 Default 点 Apply
    /// 又跳回 Streaming",正是某平台标签页勾了 Override(clip.loadType 取当前平台值,Default 被覆盖)。
    /// </summary>
    private static void ForceDecompressOnLoad(AudioImporter importer)
    {
        // 1) Default 设为 Decompress
        AudioImporterSampleSettings def = importer.defaultSampleSettings;
        def.loadType = AudioClipLoadType.DecompressOnLoad; // GetData(检测/波形/试听)需要整段解码
        importer.defaultSampleSettings = def;

        // 2) 关键(用户实测):当前激活平台必须有一个"显式 Override = Decompress"才生效,光改 Default 不够。
        //    所以主动给当前激活平台建立/设置 Override(不存在就创建),loadType 设为 Decompress。
        string active = AudioPlatformName(EditorUserBuildSettings.activeBuildTarget);
        if (!string.IsNullOrEmpty(active))
        {
            AudioImporterSampleSettings o = importer.ContainsSampleSettingsOverride(active)
                ? importer.GetOverrideSampleSettings(active)
                : importer.defaultSampleSettings;
            o.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.SetOverrideSampleSettings(active, o); // 建立/覆盖当前平台的 Override=Decompress
        }

        // 3) 强制同步重导入,确保重新加载到的 AudioClip.loadType 立即刷新
        string path = importer.assetPath;
        importer.SaveAndReimport();
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
    }

    /// <summary>把 BuildTarget 映射到 AudioImporter 平台 Override 用的名字(尽量覆盖常见平台)。</summary>
    private static string AudioPlatformName(BuildTarget target)
    {
        switch (target)
        {
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
            case BuildTarget.StandaloneOSX:
            case BuildTarget.StandaloneLinux64:
                return "Standalone";
            case BuildTarget.Android:
                return "Android";
            case BuildTarget.iOS:
                return "iPhone";
            case BuildTarget.WebGL:
                return "WebGL";
            default:
                return target.ToString();
        }
    }

    /// <summary>把选中音频复制成 Resources/LijiangEchoAudio/battle_music(运行时读的名字),并准备好格式。</summary>
    private void SetAsBattleMusic()
    {
        string src = AssetDatabase.GetAssetPath(clip);
        if (string.IsNullOrEmpty(src))
        {
            status = "音频不是项目资源,无法设为战斗音乐。";
            return;
        }

        const string destDir = "Assets/Resources/LijiangEchoAudio";
        if (!AssetDatabase.IsValidFolder(destDir))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
            {
                AssetDatabase.CreateFolder("Assets", "Resources");
            }

            AssetDatabase.CreateFolder("Assets/Resources", "LijiangEchoAudio");
        }

        string ext = System.IO.Path.GetExtension(src);
        string destDirNorm = destDir + "/";
        if (System.IO.Path.GetFileNameWithoutExtension(src) == "battle_music" && src.Replace("\\", "/").StartsWith(destDirNorm))
        {
            status = "这已经是战斗音乐了(battle_music)。";
            return;
        }

        List<string> existing = new List<string>();
        foreach (string guid in AssetDatabase.FindAssets("battle_music t:AudioClip", new[] { destDir }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(p) == "battle_music")
            {
                existing.Add(p);
            }
        }

        if (!EditorUtility.DisplayDialog("设为战斗音乐",
            $"把「{clip.name}{ext}」复制成 Resources/LijiangEchoAudio/battle_music{ext},让运行时用这首。" +
            (existing.Count > 0 ? "\n\n会先删除现有的 battle_music。" : ""), "确定", "取消"))
        {
            return;
        }

        foreach (string p in existing)
        {
            AssetDatabase.DeleteAsset(p);
        }

        string dest = destDir + "/battle_music" + ext;
        if (!AssetDatabase.CopyAsset(src, dest))
        {
            status = "复制失败,请看 Console。";
            return;
        }

        AssetDatabase.Refresh();
        AudioImporter importer = AssetImporter.GetAtPath(dest) as AudioImporter;
        if (importer != null)
        {
            ForceDecompressOnLoad(importer);
        }

        clip = AssetDatabase.LoadAssetAtPath<AudioClip>(dest);
        if (clip != null)
        {
            clipLength = clip.length;
            sampleRate = Mathf.Max(1, clip.frequency);
        }

        waveform = null;
        onsets = null;
        status = $"已设为战斗音乐 battle_music{ext}(并准备好格式)。运行时会用这首;记得据它重新检测/编辑谱面。";
    }

    private void DrawToolbar()
    {
        EnsureClip();
        DrawAudioBar();
        EditorGUILayout.HelpBox(
            "流程:① 上方「音频」栏选一首音乐(mp3/wav/ogg 都行) → ② 点「检测拍子」(会自动准备格式) → " +
            "③「用检测点重建」把拍子变成音符 → ④ 点时间轴上的音符改类型/增删 → " +
            "⑤(可选)「贴需求类型」→ ⑥ 右下「应用到」选关卡 →「保存到该场景」。" +
            "换游戏音乐:选好音频点「设为战斗音乐」。",
            MessageType.None);
        if (clip != null && !ClipReady())
        {
            EditorGUILayout.HelpBox($"当前音频「{clip.name}」未准备(非 Decompress On Load),暂不显示波形。" +
                "点「准备音频」或直接「检测拍子」即可自动准备。", MessageType.Warning);
        }

        if (clip == null)
        {
            EditorGUILayout.HelpBox("在上面「音频」栏选一个项目里的音频(mp3/wav/ogg 都行),再点「检测拍子」(会自动准备)。", MessageType.Info);
            return;
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            sensitivity = EditorGUILayout.Slider(new GUIContent("灵敏度", "越大点越少"), sensitivity, 0.5f, 4f, GUILayout.MaxWidth(220f));
            minGap = EditorGUILayout.Slider(new GUIContent("最小间隔", "两音符最近间隔(秒)"), minGap, 0.05f, 1f, GUILayout.MaxWidth(220f));
            GUILayout.Label(new GUIContent("频段", "只扒某类乐器:低频≈鼓点,中频≈管乐"), GUILayout.MaxWidth(30f));
            bandIndex = EditorGUILayout.Popup(bandIndex, BandLabels, GUILayout.MaxWidth(90f));
            if (GUILayout.Button("检测拍子", GUILayout.Height(22f)))
            {
                Detect();
            }

            if (GUILayout.Button(new GUIContent("用检测点重建", "把当前音符表替换为检测到的拍子(全设单击)"), GUILayout.Height(22f)))
            {
                RebuildFromOnsets();
            }
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            GUILayout.Label(new GUIContent("目标拍子数", "按这个数量生成音符"), GUILayout.MaxWidth(70f));
            targetBeatCount = Mathf.Clamp(EditorGUILayout.IntField(targetBeatCount, GUILayout.MaxWidth(70f)), 1, 5000);
            if (GUILayout.Button(new GUIContent("均匀切N个", "按时长等分成 N 个音符(不看音乐,规整节奏)"), GUILayout.Height(22f)))
            {
                RebuildEvenly();
            }

            if (GUILayout.Button(new GUIContent("取最强N个拍子", "检测起音后只保留最强的 N 个(跟着音乐)"), GUILayout.Height(22f)))
            {
                DetectTopN();
            }
        }

        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            if (GUILayout.Button(isPlaying ? "⏸ 停止" : "▶ 从播放头播放", GUILayout.Height(22f), GUILayout.MaxWidth(150f)))
            {
                if (isPlaying)
                {
                    StopPreview();
                }
                else
                {
                    PlayFrom(playhead);
                }
            }

            if (GUILayout.Button("⏮ 回开头", GUILayout.Height(22f), GUILayout.MaxWidth(90f)))
            {
                StopPreview();
                playhead = 0f;
                scroll.x = 0f;
            }

            GUILayout.Label($"播放头 {playhead:F2}s / {clipLength:F1}s", GUILayout.MaxWidth(160f));
            autoPreview = GUILayout.Toggle(autoPreview, "松手即试听", GUILayout.MaxWidth(90f));
            metronome = GUILayout.Toggle(metronome, new GUIContent("节拍声", "播放时播放头经过音符就叠一声(按类型)。若音乐被打断请关掉"), GUILayout.MaxWidth(60f));
            musicMuted = GUILayout.Toggle(musicMuted, new GUIContent("只放拍子", "静音音乐、只按拍点响——避免音乐/点击叠音冲突,最可靠"), GUILayout.MaxWidth(70f));
            using (new EditorGUI.DisabledScope(!metronome))
            {
                GUILayout.Label(new GUIContent("响度", "拍子提示音的音量"), GUILayout.MaxWidth(30f));
                clickVolume = GUILayout.HorizontalSlider(clickVolume, 0.1f, 1f, GUILayout.Width(70f));
            }
            follow = GUILayout.Toggle(follow, "跟随", GUILayout.MaxWidth(55f));
            GUILayout.FlexibleSpace();
            GUILayout.Label("缩放", GUILayout.MaxWidth(32f));
            pixelsPerSecond = GUILayout.HorizontalSlider(pixelsPerSecond, 20f, 400f, GUILayout.Width(160f));
        }
    }

    // ======================= 时间轴 =======================
    private void DrawTimeline()
    {
        if (clip == null || clipLength <= 0f)
        {
            GUILayoutUtility.GetRect(100f, 140f, GUILayout.ExpandWidth(true));
            return;
        }

        float height = 150f;
        Rect view = GUILayoutUtility.GetRect(100f, height, GUILayout.ExpandWidth(true));
        float contentWidth = Mathf.Max(view.width, clipLength * pixelsPerSecond);

        scroll = GUI.BeginScrollView(view, scroll, new Rect(0f, 0f, contentWidth, height - 16f));
        Rect content = new Rect(0f, 0f, contentWidth, height - 16f);
        EditorGUI.DrawRect(content, new Color(0.11f, 0.11f, 0.13f));

        DrawWaveform(content);
        DrawGrid(content);
        DrawOnsetRefs(content);
        DrawNotes(content);
        DrawPlayhead(content);
        HandleTimelineInput(content);

        GUI.EndScrollView();
    }

    private float TimeToX(float t)
    {
        return t * pixelsPerSecond;
    }

    private float XToTime(float x)
    {
        return Mathf.Clamp(x / pixelsPerSecond, 0f, clipLength);
    }

    private void EnsureVisible(float t)
    {
        float x = TimeToX(t);
        float viewW = position.width - 24f;
        if (x < scroll.x + 40f)
        {
            scroll.x = Mathf.Max(0f, x - 40f);
        }
        else if (x > scroll.x + viewW - 40f)
        {
            scroll.x = x - viewW + 40f;
        }
    }

    /// <summary>音频是否已可读采样(Decompress On Load)。压缩/流式音频直接 GetData 会刷红错误,故先判断。</summary>
    private bool ClipReady()
    {
        return clip != null && clip.loadType == AudioClipLoadType.DecompressOnLoad;
    }

    private void DrawWaveform(Rect content)
    {
        // 未准备好(非 Decompress On Load)时绝不调 GetData,避免 Unity 刷红错误。
        if (waveform == null && ClipReady())
        {
            waveform = LijiangEchoChartGenerator.BuildWaveformEnvelope(clip, WaveformBuckets);
        }

        if (waveform == null)
        {
            return;
        }

        float midY = content.y + content.height * 0.5f;
        float halfH = content.height * 0.42f;
        Color wav = new Color(0.35f, 0.55f, 0.75f, 0.55f);
        for (int b = 0; b < waveform.Length; b++)
        {
            float t = (b + 0.5f) / waveform.Length * clipLength;
            float x = TimeToX(t);
            if (x < scroll.x - 2f || x > scroll.x + position.width)
            {
                continue; // 只画可见范围,省时
            }

            float h = waveform[b] * halfH;
            EditorGUI.DrawRect(new Rect(x, midY - h, 1f, h * 2f), wav);
        }
    }

    private void DrawGrid(Rect content)
    {
        // 每秒一条参考线;缩放小时每 5 秒标一次时间
        int stepLabel = pixelsPerSecond < 40f ? 5 : (pixelsPerSecond < 90f ? 2 : 1);
        Color grid = new Color(1f, 1f, 1f, 0.06f);
        GUIStyle mini = EditorStyles.miniLabel;
        for (int s = 0; s <= Mathf.CeilToInt(clipLength); s++)
        {
            float x = TimeToX(s);
            if (x < scroll.x - 2f || x > scroll.x + position.width)
            {
                continue;
            }

            EditorGUI.DrawRect(new Rect(x, content.y, 1f, content.height), grid);
            if (s % stepLabel == 0)
            {
                GUI.Label(new Rect(x + 2f, content.y, 40f, 14f), s + "s", mini);
            }
        }
    }

    private void DrawOnsetRefs(Rect content)
    {
        if (onsets == null)
        {
            return;
        }

        Color c = new Color(1f, 0.85f, 0.35f, 0.25f); // 检测参考点:很淡
        foreach (float t in onsets)
        {
            float x = TimeToX(t);
            if (x < scroll.x - 2f || x > scroll.x + position.width)
            {
                continue;
            }

            EditorGUI.DrawRect(new Rect(x, content.yMax - 10f, 1f, 10f), c);
        }
    }

    private void DrawNotes(Rect content)
    {
        // 先画其它可见图层(底层,细线,用各自图层色),便于对照不同乐器的拍点。
        for (int li = 0; li < layers.Count; li++)
        {
            if (li == activeLayer || !layers[li].visible)
            {
                continue;
            }

            NoteLayer L = layers[li];
            Color lc = L.color;
            lc.a = 0.55f;
            for (int i = 0; i < L.times.Count; i++)
            {
                float lx = TimeToX(L.times[i]);
                if (lx < scroll.x - 6f || lx > scroll.x + position.width + 6f)
                {
                    continue;
                }

                EditorGUI.DrawRect(new Rect(lx - 0.5f, content.y + 12f, 1f, content.height - 16f), lc);
            }
        }

        // 当前图层(可选中、按类型上色),画在最上面。
        for (int i = 0; i < noteTimes.Count; i++)
        {
            float x = TimeToX(noteTimes[i]);
            if (x < scroll.x - 6f || x > scroll.x + position.width + 6f)
            {
                continue;
            }

            Color c = TypeColor(noteTypes[i]);
            bool inSel = selection.Contains(i);
            bool focus = i == selected;
            float w = (inSel || focus) ? 3f : 1.6f;
            EditorGUI.DrawRect(new Rect(x - w * 0.5f, content.y + 2f, w, content.height - 4f), c);
            // 顶部句柄:选中=白;当前编辑焦点=更大的白框
            Rect handle = new Rect(x - 5f, content.y + 2f, 10f, 10f);
            EditorGUI.DrawRect(handle, inSel ? Color.white : c);
            if (focus)
            {
                EditorGUI.DrawRect(new Rect(x - 6f, content.y + 1f, 12f, 3f), Color.white); // 焦点顶部白条
            }
        }
    }

    private void DrawPlayhead(Rect content)
    {
        float x = TimeToX(playhead);
        EditorGUI.DrawRect(new Rect(x - 1f, content.y, 2f, content.height), new Color(0.95f, 0.3f, 0.3f, 0.95f));
        // 顶部三角句柄
        Rect handle = new Rect(x - 6f, content.y, 12f, 12f);
        EditorGUI.DrawRect(handle, new Color(0.95f, 0.3f, 0.3f, 1f));
    }

    private void HandleTimelineInput(Rect content)
    {
        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 0 && content.Contains(e.mousePosition))
        {
            StopPreviewSilently(); // 任何按下先停,重播/重拖不叠旧音乐
            int hit = FindNoteNear(e.mousePosition.x, 8f);
            bool onPlayheadTop = Mathf.Abs(e.mousePosition.x - TimeToX(playhead)) < 8f && e.mousePosition.y < content.y + 14f;

            if (hit >= 0 && !onPlayheadTop)
            {
                if (e.control || e.command || e.shift)
                {
                    ToggleSelect(hit); // Ctrl/Shift 点 = 补选/取消选(多选)
                }
                else
                {
                    SelectSingle(hit);
                    draggingNote = hit;          // 单点音符 = 可拖动它改时间
                    noteDragUndoRecorded = false;
                }

                GUI.changed = true;
                e.Use();
            }
            else if (e.clickCount >= 2)
            {
                // 空白处双击 = 在此时间新增一个音符
                AddNoteAt(XToTime(e.mousePosition.x));
                e.Use();
            }
            else
            {
                draggingPlayhead = true;
                playhead = XToTime(e.mousePosition.x);
                e.Use();
            }
        }
        else if (e.type == EventType.MouseDrag && draggingNote >= 0)
        {
            if (!noteDragUndoRecorded)
            {
                RecordUndo(); // 真正开始拖了才记撤销(避免单点也记)
                noteDragUndoRecorded = true;
            }

            if (draggingNote < noteTimes.Count)
            {
                noteTimes[draggingNote] = XToTime(e.mousePosition.x);
            }

            GUI.changed = true;
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseDrag && draggingPlayhead)
        {
            playhead = XToTime(e.mousePosition.x);
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseUp)
        {
            if (draggingNote >= 0)
            {
                if (noteDragUndoRecorded)
                {
                    status = "已拖动音符到新时间。";
                }

                draggingNote = -1;
                e.Use();
            }
            else if (draggingPlayhead)
            {
                draggingPlayhead = false;
                if (autoPreview)
                {
                    PlayFrom(playhead);
                }

                e.Use();
            }
        }
    }

    // ======================= 时间轴工具栏(像视频编辑器) =======================
    private void DrawTimelineToolbar()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            if (GUILayout.Button(new GUIContent("＋ 加音符", "在当前播放头处新增一个音符(也可在时间轴空白处双击)"), EditorStyles.toolbarButton, GUILayout.Width(80f)))
            {
                AddNoteAt(playhead);
            }

            using (new EditorGUI.DisabledScope(selection.Count == 0 && selected < 0))
            {
                if (GUILayout.Button(new GUIContent("删除选中", "删除选中的音符(时间轴上点/ Ctrl 多选)"), EditorStyles.toolbarButton, GUILayout.Width(70f)))
                {
                    DeleteSelectedOrFocused();
                }
            }

            using (new EditorGUI.DisabledScope(undoStack.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("↶ 撤销", "Ctrl+Z"), EditorStyles.toolbarButton, GUILayout.Width(58f)))
                {
                    Undo();
                }
            }

            using (new EditorGUI.DisabledScope(redoStack.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("↷ 重做", "Ctrl+Y"), EditorStyles.toolbarButton, GUILayout.Width(58f)))
                {
                    Redo();
                }
            }

            GUILayout.Space(10f);
            GUILayout.Label("拖动音符=改时间 · 双击空白=加音符 · Ctrl点=多选 · 拖顶部=移动播放头", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            GUILayout.Label($"当前层 {noteTimes.Count} 拍 · 已选 {(selection.Count > 0 ? selection.Count : (selected >= 0 ? 1 : 0))}", EditorStyles.miniLabel);
        }
    }

    private void DeleteSelectedOrFocused()
    {
        if (selection.Count == 0 && selected >= 0)
        {
            selection.Add(selected);
        }

        DeleteSelected();
    }

    private int FindNoteNear(float x, float pxTolerance)
    {
        int best = -1;
        float bestD = pxTolerance;
        for (int i = 0; i < noteTimes.Count; i++)
        {
            float d = Mathf.Abs(TimeToX(noteTimes[i]) - x);
            if (d < bestD)
            {
                bestD = d;
                best = i;
            }
        }

        return best;
    }

    // ======================= 选中音符编辑 =======================
    private void DrawSelectedEditor()
    {
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            using (new EditorGUI.DisabledScope(selected < 0 || selected >= noteTimes.Count))
            {
                if (GUILayout.Button("◀ 上一个", GUILayout.MaxWidth(80f)))
                {
                    selected = Mathf.Max(0, selected - 1);
                    EnsureVisible(noteTimes[selected]);
                }

                if (GUILayout.Button("下一个 ▶", GUILayout.MaxWidth(80f)))
                {
                    selected = Mathf.Min(noteTimes.Count - 1, selected + 1);
                    EnsureVisible(noteTimes[selected]);
                }

                if (selected >= 0 && selected < noteTimes.Count)
                {
                    GUILayout.Label($"#{selected}", GUILayout.MaxWidth(44f));

                    float nt = EditorGUILayout.FloatField(new GUIContent("时间", "该音符时间(秒)"), noteTimes[selected], GUILayout.MaxWidth(160f));
                    if (!Mathf.Approximately(nt, noteTimes[selected]))
                    {
                        noteTimes[selected] = Mathf.Clamp(nt, 0f, clipLength);
                    }

                    int cur = Mathf.Max(0, Array.IndexOf(TypeOptions, noteTypes[selected]));
                    int next = EditorGUILayout.Popup("类型", cur, TypeLabels, GUILayout.MaxWidth(200f));
                    if (next != cur)
                    {
                        RecordUndo();
                        noteTypes[selected] = TypeOptions[next];
                    }

                    if (GUILayout.Button("对齐最近检测点", GUILayout.MaxWidth(120f)))
                    {
                        SnapSelectedToOnset();
                    }

                    if (GUILayout.Button("删除", GUILayout.MaxWidth(60f)))
                    {
                        RecordUndo();
                        noteTimes.RemoveAt(selected);
                        noteTypes.RemoveAt(selected);
                        selected = Mathf.Clamp(selected, -1, noteTimes.Count - 1);
                        selection.Clear();
                    }
                }
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button(new GUIContent("＋ 在播放头加音符", "在当前播放头处新增一个单击音符"), GUILayout.MaxWidth(150f)))
            {
                AddNoteAt(playhead);
            }
        }
    }

    // ======================= 批量 / 自动类型(作用于当前图层) =======================
    private void DrawBatchTools()
    {
        showBatch = EditorGUILayout.Foldout(showBatch, "批量 / 自动类型（作用于当前图层）", true);
        if (!showBatch)
        {
            return;
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            // 0) 撤销/重做 + 多选(全选/选类型/删除选中)
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(undoStack.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("↶ 撤销", "Ctrl+Z"), GUILayout.MaxWidth(60f)))
                    {
                        Undo();
                    }
                }

                using (new EditorGUI.DisabledScope(redoStack.Count == 0))
                {
                    if (GUILayout.Button(new GUIContent("↷ 重做", "Ctrl+Y"), GUILayout.MaxWidth(60f)))
                    {
                        Redo();
                    }
                }

                GUILayout.Space(12f);
                if (GUILayout.Button("全选本层", GUILayout.MaxWidth(70f)))
                {
                    SelectAllInLayer();
                }

                if (GUILayout.Button(new GUIContent($"选中「{TypeShort[batchFrom]}」", "选中当前图层里所有『把』左边那种类型(下方那个下拉)"), GUILayout.MaxWidth(90f)))
                {
                    SelectByType(batchFrom);
                }

                using (new EditorGUI.DisabledScope(selection.Count == 0))
                {
                    if (GUILayout.Button($"删除选中({selection.Count})", GUILayout.MaxWidth(110f)))
                    {
                        DeleteSelected();
                    }
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label($"已选 {selection.Count}(时间轴上 Ctrl+点 补选)", EditorStyles.miniLabel);
            }

            // 1) 整层全设为某类型(如:鼓点层→全双击)
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("整层全设为", GUILayout.MaxWidth(70f));
                batchAllType = EditorGUILayout.Popup(batchAllType, TypeLabels, GUILayout.MaxWidth(130f));
                if (GUILayout.Button("应用", GUILayout.MaxWidth(60f)))
                {
                    SetAllType(batchAllType);
                }

                GUILayout.Label("← 例:把鼓点层整层设成双击", EditorStyles.miniLabel);
            }

            // 2) 按比例随机分配类型(勾选类型 + 权重)
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("按比例分配", GUILayout.MaxWidth(70f));
                for (int t = 0; t < 4; t++)
                {
                    ratioOn[t] = GUILayout.Toggle(ratioOn[t], TypeShort[t], GUILayout.MaxWidth(46f));
                    using (new EditorGUI.DisabledScope(!ratioOn[t]))
                    {
                        ratioWeight[t] = Mathf.Max(0f, EditorGUILayout.FloatField(ratioWeight[t], GUILayout.MaxWidth(40f)));
                    }
                }

                if (GUILayout.Button("随机分配到当前图层", GUILayout.MaxWidth(150f)))
                {
                    DistributeByRatio();
                }
            }

            // 3) 按类型批量替换 / 删除
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("把", GUILayout.MaxWidth(18f));
                batchFrom = EditorGUILayout.Popup(batchFrom, TypeLabels, GUILayout.MaxWidth(110f));
                GUILayout.Label("→", GUILayout.MaxWidth(16f));
                batchTo = EditorGUILayout.Popup(batchTo, TypeLabels, GUILayout.MaxWidth(110f));
                if (GUILayout.Button("批量替换", GUILayout.MaxWidth(80f)))
                {
                    ReplaceType(batchFrom, batchTo);
                }

                if (GUILayout.Button(new GUIContent("批量删除该类型", "删除当前图层里所有『把』左边那种类型的音符"), GUILayout.MaxWidth(120f)))
                {
                    DeleteType(batchFrom);
                }
            }
        }
    }

    private void SetAllType(int typeIdx)
    {
        RecordUndo();
        string ty = TypeOptions[Mathf.Clamp(typeIdx, 0, 3)];
        for (int i = 0; i < noteTypes.Count; i++)
        {
            noteTypes[i] = ty;
        }

        status = $"当前图层 {noteTypes.Count} 个音符已全设为「{TypeShort[typeIdx]}」。";
    }

    private void DistributeByRatio()
    {
        List<int> en = new List<int>();
        float sum = 0f;
        for (int t = 0; t < 4; t++)
        {
            if (ratioOn[t] && ratioWeight[t] > 0f)
            {
                en.Add(t);
                sum += ratioWeight[t];
            }
        }

        if (en.Count == 0 || sum <= 0f)
        {
            status = "至少勾一个类型且权重>0。";
            return;
        }

        RecordUndo();
        System.Random rng = new System.Random();
        for (int i = 0; i < noteTypes.Count; i++)
        {
            double r = rng.NextDouble() * sum;
            double acc = 0;
            int chosen = en[0];
            foreach (int t in en)
            {
                acc += ratioWeight[t];
                if (r <= acc)
                {
                    chosen = t;
                    break;
                }
            }

            noteTypes[i] = TypeOptions[chosen];
        }

        status = $"已按比例随机分配当前图层 {noteTypes.Count} 个音符的类型。再点一次会重新随机。";
    }

    private void ReplaceType(int from, int to)
    {
        RecordUndo();
        string f = TypeOptions[from];
        string t = TypeOptions[to];
        int n = 0;
        for (int i = 0; i < noteTypes.Count; i++)
        {
            if (noteTypes[i] == f)
            {
                noteTypes[i] = t;
                n++;
            }
        }

        status = $"已把当前图层 {n} 个「{TypeShort[from]}」替换为「{TypeShort[to]}」。";
    }

    private void DeleteType(int typeIdx)
    {
        RecordUndo();
        string f = TypeOptions[typeIdx];
        int n = 0;
        for (int i = noteTimes.Count - 1; i >= 0; i--)
        {
            if (i < noteTypes.Count && noteTypes[i] == f)
            {
                noteTimes.RemoveAt(i);
                noteTypes.RemoveAt(i);
                n++;
            }
        }

        selected = -1;
        status = $"已删除当前图层 {n} 个「{TypeShort[typeIdx]}」音符。";
    }

    // ======================= 撤销 / 重做 =======================
    private void HandleUndoRedoKeys()
    {
        Event e = Event.current;
        if (e.type != EventType.KeyDown || !(e.control || e.command))
        {
            return;
        }

        if (e.keyCode == KeyCode.Z && !e.shift)
        {
            Undo();
            e.Use();
        }
        else if (e.keyCode == KeyCode.Y || (e.keyCode == KeyCode.Z && e.shift))
        {
            Redo();
            e.Use();
        }
    }

    private Snapshot Capture()
    {
        Snapshot s = new Snapshot { active = activeLayer, layers = new List<LayerSnap>() };
        foreach (NoteLayer L in layers)
        {
            s.layers.Add(new LayerSnap
            {
                name = L.name, color = L.color, visible = L.visible,
                times = new List<float>(L.times), types = new List<string>(L.types)
            });
        }

        return s;
    }

    /// <summary>在任何"改数据"的操作前调用:把当前状态压入撤销栈。</summary>
    private void RecordUndo()
    {
        undoStack.Add(Capture());
        if (undoStack.Count > 80)
        {
            undoStack.RemoveAt(0);
        }

        redoStack.Clear();
    }

    private void Restore(Snapshot s)
    {
        layers.Clear();
        foreach (LayerSnap ls in s.layers)
        {
            NoteLayer L = new NoteLayer { name = ls.name, color = ls.color, visible = ls.visible };
            L.times.AddRange(ls.times);
            L.types.AddRange(ls.types);
            layers.Add(L);
        }

        EnsureLayers();
        activeLayer = Mathf.Clamp(s.active, 0, layers.Count - 1);
        selected = -1;
        selection.Clear();
    }

    private void Undo()
    {
        if (undoStack.Count == 0)
        {
            status = "没有可撤销的操作。";
            return;
        }

        redoStack.Add(Capture());
        Snapshot s = undoStack[undoStack.Count - 1];
        undoStack.RemoveAt(undoStack.Count - 1);
        Restore(s);
        status = "已撤销(Ctrl+Z)。";
    }

    private void Redo()
    {
        if (redoStack.Count == 0)
        {
            status = "没有可重做的操作。";
            return;
        }

        undoStack.Add(Capture());
        Snapshot s = redoStack[redoStack.Count - 1];
        redoStack.RemoveAt(redoStack.Count - 1);
        Restore(s);
        status = "已重做(Ctrl+Y)。";
    }

    // ======================= 多选 =======================
    private void SelectSingle(int i)
    {
        selected = i;
        selection.Clear();
        if (i >= 0)
        {
            selection.Add(i);
        }
    }

    private void ToggleSelect(int i)
    {
        if (i < 0)
        {
            return;
        }

        if (!selection.Remove(i))
        {
            selection.Add(i);
            selected = i;
        }
        else if (selected == i)
        {
            selected = -1;
        }
    }

    private void SelectAllInLayer()
    {
        selection.Clear();
        for (int i = 0; i < noteTimes.Count; i++)
        {
            selection.Add(i);
        }

        selected = noteTimes.Count == 1 ? 0 : -1;
        status = $"已选中当前图层全部 {selection.Count} 个音符。";
    }

    private void SelectByType(int typeIdx)
    {
        string ty = TypeOptions[typeIdx];
        selection.Clear();
        for (int i = 0; i < noteTypes.Count; i++)
        {
            if (noteTypes[i] == ty)
            {
                selection.Add(i);
            }
        }

        selected = -1;
        status = $"已选中当前图层全部「{TypeShort[typeIdx]}」共 {selection.Count} 个。";
    }

    private void DeleteSelected()
    {
        if (selection.Count == 0)
        {
            status = "没有选中的音符。";
            return;
        }

        RecordUndo();
        List<int> idx = new List<int>(selection);
        idx.Sort();
        for (int k = idx.Count - 1; k >= 0; k--)
        {
            int i = idx[k];
            if (i >= 0 && i < noteTimes.Count)
            {
                noteTimes.RemoveAt(i);
                noteTypes.RemoveAt(i);
            }
        }

        int n = idx.Count;
        selection.Clear();
        selected = -1;
        status = $"已删除选中的 {n} 个音符。";
    }

    // ======================= 底部:保存 / 贴类型 =======================
    private void DrawBottomBar()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("读回已有谱面", GUILayout.Height(26f)))
            {
                if (LijiangEchoChartGenerator.TryLoadChartRows(out List<float> t, out List<string> ty))
                {
                    noteTimes.Clear();
                    noteTypes.Clear();
                    noteTimes.AddRange(t);
                    noteTypes.AddRange(ty);
                    selected = -1;
                    status = $"读回 {noteTimes.Count} 个音符。";
                }
                else
                {
                    status = "没有可读回的 chart_generated.txt。";
                }
            }

            using (new EditorGUI.DisabledScope(noteTimes.Count == 0))
            {
                if (GUILayout.Button(new GUIContent("贴需求类型", "把需求表 chart_liusanjie 的类型吸附到最近音符(写入全局 chart_generated)"), GUILayout.Height(26f)))
                {
                    LijiangEchoChartGenerator.SnapRequirementTypes();
                    status = "已把需求类型写入全局 chart_generated.txt;把「源谱」选为「全局」再点「载入源谱」查看。";
                }
            }
        }

        int d = 0, h = 0, sw = 0;
        for (int i = 0; i < noteTypes.Count; i++)
        {
            if (noteTypes[i] == "double") d++;
            else if (noteTypes[i] == "hold") h++;
            else if (noteTypes[i] == "swipe") sw++;
        }

        EditorGUILayout.LabelField($"音符 {noteTimes.Count}(单{noteTimes.Count - d - h - sw} 双{d} 长{h} 划{sw})   ·   {status}",
            EditorStyles.wordWrappedMiniLabel);
    }

    // ======================= 操作 =======================
    private void Detect()
    {
        EnsureClip();
        if (clip == null)
        {
            status = "找不到音乐,请在上面「音频」栏选一首。";
            return;
        }

        if (!ClipReady())
        {
            PrepareAudio(); // 未准备好就自动设为 Decompress On Load(省得你先点一次)
        }

        if (!ClipReady())
        {
            status = "此音频仍无法读采样(格式异常),换一首或手动设 Decompress On Load。";
            return;
        }

        BandRange(out float lowHz, out float highHz);
        onsets = LijiangEchoChartGenerator.DetectOnsetsBand(clip, sensitivity, minGap, lowHz, highHz, out int _, out float[] _s);
        waveform = LijiangEchoChartGenerator.BuildWaveformEnvelope(clip, WaveformBuckets);
        if (onsets == null)
        {
            status = "读采样失败:请把音频导入设置改成 Decompress On Load 再 Apply。";
            return;
        }

        status = $"检测到 {onsets.Length} 个拍子(频段:{BandLabels[bandIndex]},浅色参考点)。可「用检测点重建」或手动加音符对齐它们。";
    }

    /// <summary>按时长把整曲均匀切成 targetBeatCount 个音符(全单击)。</summary>
    private void RebuildEvenly()
    {
        EnsureClip();
        if (clip == null || clipLength <= 0f)
        {
            status = "先在「音频」栏选一首音频。";
            return;
        }

        float[] t = LijiangEchoChartGenerator.EvenlySpacedBeats(clipLength, targetBeatCount);
        RecordUndo();
        noteTimes.Clear();
        noteTypes.Clear();
        foreach (float x in t)
        {
            noteTimes.Add(x);
            noteTypes.Add("single");
        }

        selected = -1;
        status = $"已按时长均匀切成 {noteTimes.Count} 个音符(全单击),逐个改类型后保存。";
    }

    /// <summary>检测起音,只保留最强的 targetBeatCount 个,直接生成音符(全单击)。</summary>
    private void DetectTopN()
    {
        EnsureClip();
        if (clip == null)
        {
            status = "先在「音频」栏选一首音频。";
            return;
        }

        if (!ClipReady())
        {
            PrepareAudio();
        }

        if (!ClipReady())
        {
            status = "此音频无法读采样,换一首或转成 WAV。";
            return;
        }

        BandRange(out float lowHz, out float highHz);
        onsets = LijiangEchoChartGenerator.DetectTopOnsets(clip, sensitivity, minGap, targetBeatCount, lowHz, highHz, out int _);
        waveform = LijiangEchoChartGenerator.BuildWaveformEnvelope(clip, WaveformBuckets);
        if (onsets == null)
        {
            status = "读采样失败:请把音频设为 Decompress On Load。";
            return;
        }

        noteTimes.Clear();
        noteTypes.Clear();
        foreach (float x in onsets)
        {
            noteTimes.Add(x);
            noteTypes.Add("single");
        }

        selected = -1;
        status = $"已取最强 {noteTimes.Count} 个拍子(全单击);拍子偏少可调低灵敏度/最小间隔再试。";
    }

    private void RebuildFromOnsets()
    {
        if (onsets == null || onsets.Length == 0)
        {
            status = "先点「检测拍子」。";
            return;
        }

        if (!EditorUtility.DisplayDialog("用检测点重建", $"用 {onsets.Length} 个检测拍子替换当前 {noteTimes.Count} 个音符(全设单击)?", "重建", "取消"))
        {
            return;
        }

        RecordUndo();
        noteTimes.Clear();
        noteTypes.Clear();
        foreach (float t in onsets)
        {
            noteTimes.Add(t);
            noteTypes.Add("single");
        }

        selected = -1;
        status = $"已用 {noteTimes.Count} 个检测点重建(全单击),逐个改类型后保存。";
    }

    private void AddNoteAt(float t)
    {
        RecordUndo();
        noteTimes.Add(Mathf.Clamp(t, 0f, clipLength));
        noteTypes.Add("single");
        SortModel();
        selected = noteTimes.IndexOf(Mathf.Clamp(t, 0f, clipLength));
        status = "新增单击音符;可改类型。";
    }

    private void SnapSelectedToOnset()
    {
        if (onsets == null || onsets.Length == 0 || selected < 0)
        {
            return;
        }

        float t = noteTimes[selected];
        float best = t;
        float bestD = float.MaxValue;
        foreach (float o in onsets)
        {
            float d = Mathf.Abs(o - t);
            if (d < bestD)
            {
                bestD = d;
                best = o;
            }
        }

        noteTimes[selected] = best;
    }

    private void SortModel()
    {
        // 记住当前选中音符的时间,排序后恢复选中
        float selTime = (selected >= 0 && selected < noteTimes.Count) ? noteTimes[selected] : -1f;
        List<KeyValuePair<float, string>> rows = new List<KeyValuePair<float, string>>();
        for (int i = 0; i < noteTimes.Count; i++)
        {
            rows.Add(new KeyValuePair<float, string>(noteTimes[i], noteTypes[i]));
        }

        rows.Sort((a, b) => a.Key.CompareTo(b.Key));
        noteTimes.Clear();
        noteTypes.Clear();
        foreach (KeyValuePair<float, string> r in rows)
        {
            noteTimes.Add(r.Key);
            noteTypes.Add(r.Value);
        }

        if (selTime >= 0f)
        {
            selected = noteTimes.IndexOf(selTime);
        }
    }

    private static Color TypeColor(string type)
    {
        switch (type)
        {
            case "double": return new Color(1f, 0.6f, 0.25f);   // 橙(鸟纹)
            case "hold": return new Color(0.7f, 0.45f, 1f);     // 紫(长按)
            case "swipe": return new Color(0.35f, 0.9f, 0.7f);  // 青绿(挥划)
            default: return new Color(0.95f, 0.95f, 0.95f);     // 白(单击)
        }
    }

    // ======================= 音频试听 =======================
    private void PlayFrom(float t)
    {
        EnsureClip();
        if (clip == null)
        {
            return;
        }

        AudioPreview.Stop(); // 先停掉一切(上一次音乐 + 残留节拍声),避免重叠
        if (!musicMuted)
        {
            int startSample = Mathf.Clamp(Mathf.RoundToInt(t * sampleRate), 0, Mathf.Max(0, clip.samples - 1));
            AudioPreview.Play(clip, startSample);
        }

        isPlaying = true;
        playStartHead = Mathf.Clamp(t, 0f, clipLength);
        playStartRealtime = EditorApplication.timeSinceStartup;
        playhead = playStartHead;
        lastClickPlayhead = t; // 从这里开始记节拍,避免把之前的音符一次性响出来
    }

    /// <summary>播放头从 from 走到 to 之间,每个(可见图层的)音符叠一声点击,按类型选音效。</summary>
    private void PlayMetronomeClicks(float from, float to)
    {
        if (to <= from)
        {
            return;
        }

        foreach (NoteLayer L in layers)
        {
            if (!L.visible)
            {
                continue;
            }

            for (int i = 0; i < L.times.Count; i++)
            {
                float t = L.times[i];
                if (t > from && t <= to)
                {
                    AudioPreview.PlayOverlay(ClickClip(i < L.types.Count ? L.types[i] : "single"));
                }
            }
        }
    }

    private AudioClip ClickClip(string type)
    {
        if (genSingle == null || !Mathf.Approximately(genVolume, clickVolume))
        {
            genVolume = clickVolume;
            genSingle = MakeTickAsset("tick_single", 1200f, 0.045f, clickVolume, false);
            genHold = MakeTickAsset("tick_hold", 600f, 0.075f, clickVolume, false);
            genSwipe = MakeTickAsset("tick_swipe", 0f, 0.06f, clickVolume, true);
        }

        switch (type)
        {
            case "hold": return genHold;
            case "swipe": return genSwipe;
            default: return genSingle; // 单击/双击
        }
    }

    /// <summary>
    /// 生成短"咔哒"提示音并写成真实 WAV 素材再导入(运行时 AudioClip.Create 的 clip AudioUtil 播不出来,必须真素材)。
    /// </summary>
    private static AudioClip MakeTickAsset(string name, float freq, float dur, float amp, bool noise)
    {
        const int sr = 44100;
        int n = Mathf.Max(8, (int)(sr * dur));
        float[] data = new float[n];
        System.Random rng = new System.Random(20260830);
        for (int i = 0; i < n; i++)
        {
            float env = Mathf.Exp(-9f * i / n);
            float s = noise ? (float)(rng.NextDouble() * 2.0 - 1.0) : Mathf.Sin(2f * Mathf.PI * freq * i / sr);
            data[i] = Mathf.Clamp(s * env * amp, -1f, 1f);
        }

        const string dir = "Assets/Resources/LijiangEchoNotes";
        System.IO.Directory.CreateDirectory(dir);
        string path = dir + "/" + name + ".wav";
        System.IO.File.WriteAllBytes(path, EncodeWavMono16(data, sr));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }

    private static byte[] EncodeWavMono16(float[] data, int sampleRate)
    {
        int n = data.Length;
        int dataBytes = n * 2;
        using (System.IO.MemoryStream ms = new System.IO.MemoryStream(44 + dataBytes))
        using (System.IO.BinaryWriter bw = new System.IO.BinaryWriter(ms))
        {
            bw.Write(new char[] { 'R', 'I', 'F', 'F' });
            bw.Write(36 + dataBytes);
            bw.Write(new char[] { 'W', 'A', 'V', 'E' });
            bw.Write(new char[] { 'f', 'm', 't', ' ' });
            bw.Write(16);
            bw.Write((short)1);
            bw.Write((short)1);
            bw.Write(sampleRate);
            bw.Write(sampleRate * 2);
            bw.Write((short)2);
            bw.Write((short)16);
            bw.Write(new char[] { 'd', 'a', 't', 'a' });
            bw.Write(dataBytes);
            for (int i = 0; i < n; i++)
            {
                bw.Write((short)(Mathf.Clamp(data[i], -1f, 1f) * 32767f));
            }

            bw.Flush();
            return ms.ToArray();
        }
    }

    private void StopPreview()
    {
        AudioPreview.Stop();
        isPlaying = false;
    }

    private void StopPreviewSilently()
    {
        AudioPreview.Stop();
        isPlaying = false;
    }

    /// <summary>反射调用 UnityEditor.AudioUtil 的编辑器试听接口(不同 Unity 版本方法名不同,逐一尝试)。</summary>
    private static class AudioPreview
    {
        private static readonly Type Util = typeof(AudioImporter).Assembly.GetType("UnityEditor.AudioUtil");
        private static readonly MethodInfo Play3 = Find(new[] { "PlayPreviewClip", "PlayClip" }, new[] { typeof(AudioClip), typeof(int), typeof(bool) });
        private static readonly MethodInfo Play1 = Find(new[] { "PlayPreviewClip", "PlayClip" }, new[] { typeof(AudioClip) });
        private static readonly MethodInfo StopAll = Find(new[] { "StopAllPreviewClips", "StopAllClips" }, Type.EmptyTypes);
        private static readonly MethodInfo PosM = Find(new[] { "GetPreviewClipSamplePosition", "GetClipSamplePosition" }, Type.EmptyTypes);
        private static readonly MethodInfo PlayingM = Find(new[] { "IsPreviewClipPlaying", "IsClipPlaying" }, Type.EmptyTypes);

        private static MethodInfo Find(string[] names, Type[] sig)
        {
            if (Util == null)
            {
                return null;
            }

            const BindingFlags F = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (string n in names)
            {
                MethodInfo m = Util.GetMethod(n, F, null, sig, null);
                if (m != null)
                {
                    return m;
                }
            }

            // 兜底:忽略签名,按名字找(不同 Unity 版本参数可能不同)。
            foreach (string n in names)
            {
                try
                {
                    MethodInfo m = Util.GetMethod(n, F);
                    if (m != null)
                    {
                        return m;
                    }
                }
                catch
                {
                    // AmbiguousMatchException:有多个重载,跳过按名兜底。
                }
            }

            return null;
        }

        public static string Diag()
        {
            return $"AudioUtil={(Util != null)}  Play3={(Play3 != null)}  Play1={(Play1 != null)}  " +
                   $"StopAll={(StopAll != null)}  Pos={(PosM != null)}  Playing={(PlayingM != null)}";
        }

        public static void Play(AudioClip c, int startSample)
        {
            try
            {
                if (Play3 != null)
                {
                    Play3.Invoke(null, new object[] { c, startSample, false });
                }
                else if (Play1 != null)
                {
                    Play1.Invoke(null, new object[] { c });
                }
            }
            catch
            {
                // 反射失败:静默(不同版本接口不一致时不至于崩窗口)
            }
        }

        /// <summary>叠加播放一个短音(节拍声):不 StopAll,尽量与音乐同时响(部分 Unity 版本支持多路预览)。</summary>
        public static void PlayOverlay(AudioClip c)
        {
            if (c == null)
            {
                return;
            }

            try
            {
                if (Play3 != null)
                {
                    Play3.Invoke(null, new object[] { c, 0, false });
                }
                else if (Play1 != null)
                {
                    Play1.Invoke(null, new object[] { c });
                }
            }
            catch
            {
            }
        }

        public static void Stop()
        {
            try { StopAll?.Invoke(null, null); }
            catch { }
        }

        public static int Pos()
        {
            try { return PosM != null ? (int)PosM.Invoke(null, null) : 0; }
            catch { return 0; }
        }

        public static bool Playing()
        {
            try { return PlayingM != null && (bool)PlayingM.Invoke(null, null); }
            catch { return false; }
        }
    }
}
