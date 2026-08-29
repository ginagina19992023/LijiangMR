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

    // —— 数据模型:逐音符(时间, 类型) ——
    private readonly List<float> noteTimes = new List<float>();
    private readonly List<string> noteTypes = new List<string>();
    private int selected = -1;

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
    private bool autoPreview = true; // 松开播放头即试听
    private bool follow = true;      // 播放时视图跟随播放头

    // —— 播放状态 ——
    private bool isPlaying;

    private string status = "点「检测拍子」或「读回已有谱面」开始。";

    private static readonly string[] TypeOptions = { "single", "double", "hold", "swipe" };
    private static readonly string[] TypeLabels = { "单击 single", "双击 double", "长按 hold", "挥划 swipe" };

    // 源谱(载入编辑):三关专属谱 + 全局生成谱 + 需求谱。
    private static readonly string[] SourceLabels =
    {
        "本关·蛙纹 (level0)", "本关·鸟纹 (level1)", "本关·鱼纹 (level2)",
        "全局 chart_generated", "需求 chart_liusanjie"
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
            default: return LijiangEchoChartGenerator.RequirementChartPath;
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

        if (AudioPreview.Playing())
        {
            playhead = Mathf.Clamp(AudioPreview.Pos() / (float)sampleRate, 0f, clipLength);
            if (follow)
            {
                EnsureVisible(playhead);
            }

            Repaint();
        }
        else
        {
            isPlaying = false;
            Repaint();
        }
    }

    private void OnGUI()
    {
        DrawToolbar();
        EditorGUILayout.Space(2f);
        DrawTimeline();
        EditorGUILayout.Space(4f);
        DrawSelectedEditor();
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
                if (GUILayout.Button(new GUIContent("💾 保存到该场景", "把当前音符表写成该战斗场景的谱面(带 types:explicit)"), GUILayout.MaxWidth(130f)))
                {
                    SaveToTarget();
                }

                if (GUILayout.Button(new GUIContent("▶ 保存并试玩", "保存到该关卡 → 直接进 Play 进入战斗:边听音乐边看纹样飞入、可打点验证。停止 Play 回到编辑"), GUILayout.MaxWidth(110f)))
                {
                    SaveAndPlaytest();
                }
            }
        }
    }

    /// <summary>保存当前谱面到目标关卡,并直接进入 Play 的战斗阶段试玩(真实场景+音乐+可打点)。</summary>
    private void SaveAndPlaytest()
    {
        SaveToTarget();

        int level = targetIndex <= 2 ? targetIndex : 0;
        PlayerPrefs.SetInt("LJ_DebugStartStage", 4); // 4 = 战斗(见 JumpToStageForDebug)
        PlayerPrefs.SetInt("LJ_DebugLevel", level);
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
        SortModel();
        string path = TargetPath(targetIndex);
        LijiangEchoChartGenerator.WriteChartExplicit(noteTimes, noteTypes, path);
        status = $"已把 {noteTimes.Count} 个音符保存到「{TargetLabels[targetIndex]}」({path},带 types:explicit,运行时该关卡所见即所得)。";
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
        for (int i = 0; i < noteTimes.Count; i++)
        {
            float x = TimeToX(noteTimes[i]);
            if (x < scroll.x - 6f || x > scroll.x + position.width + 6f)
            {
                continue;
            }

            Color c = TypeColor(noteTypes[i]);
            bool sel = i == selected;
            float w = sel ? 3f : 1.6f;
            EditorGUI.DrawRect(new Rect(x - w * 0.5f, content.y + 2f, w, content.height - 4f), c);
            // 顶部小方块作为可点句柄
            Rect handle = new Rect(x - 5f, content.y + 2f, 10f, 10f);
            EditorGUI.DrawRect(handle, sel ? Color.white : c);
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
            // 先判是否点到某个音符句柄(优先选中音符)
            int hit = FindNoteNear(e.mousePosition.x, 7f);
            bool onPlayheadTop = Mathf.Abs(e.mousePosition.x - TimeToX(playhead)) < 8f && e.mousePosition.y < content.y + 14f;

            if (hit >= 0 && !onPlayheadTop)
            {
                selected = hit;
                GUI.changed = true;
                e.Use();
            }
            else
            {
                // 否则移动播放头
                draggingPlayhead = true;
                playhead = XToTime(e.mousePosition.x);
                StopPreviewSilently();
                e.Use();
            }
        }
        else if (e.type == EventType.MouseDrag && draggingPlayhead)
        {
            playhead = XToTime(e.mousePosition.x);
            e.Use();
            Repaint();
        }
        else if (e.type == EventType.MouseUp && draggingPlayhead)
        {
            draggingPlayhead = false;
            if (autoPreview)
            {
                PlayFrom(playhead);
            }

            e.Use();
        }
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
                        noteTypes[selected] = TypeOptions[next];
                    }

                    if (GUILayout.Button("对齐最近检测点", GUILayout.MaxWidth(120f)))
                    {
                        SnapSelectedToOnset();
                    }

                    if (GUILayout.Button("删除", GUILayout.MaxWidth(60f)))
                    {
                        noteTimes.RemoveAt(selected);
                        noteTypes.RemoveAt(selected);
                        selected = Mathf.Clamp(selected, -1, noteTimes.Count - 1);
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

        int startSample = Mathf.Clamp(Mathf.RoundToInt(t * sampleRate), 0, Mathf.Max(0, clip.samples - 1));
        AudioPreview.Play(clip, startSample);
        isPlaying = true;
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

            foreach (string n in names)
            {
                MethodInfo m = Util.GetMethod(n, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, sig, null);
                if (m != null)
                {
                    return m;
                }
            }

            return null;
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
