using Meta.XR.MRUtilityKit;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 一键把当前场景(通常是 Scanplay)搭成「点 Play 就能跑」的扫码场景。
///
/// 反馈:「当前新场景不能点一下 play 就跑」。这个功能要三样东西同时在场:
///   OVRCameraRig(MRUK 硬性依赖) + MRUK(系统级二维码追踪) + 我们自己的扫码脚本。
/// 手搭容易漏,所以做成一条菜单。
///
/// 电脑上没有头显也能跑:Play 之后按 1/2/3/4 就当作扫到了鱼/蛇/蛙/鸟。
/// </summary>
public static class LijiangEchoScanSceneSetup
{
    private const string Root = "漓江回声/5 调试/扫码/";

    [MenuItem(Root + "一键搭好当前场景(Play 即可跑)", false, 0)]
    private static void SetupScene()
    {
        bool rigAdded = EnsureCameraRig();
        bool mrukAdded = EnsureMruk();
        GameObject scanner = EnsureScanner();
        int cameras = ApplyBackdropToCameras();
        int rings = NormalizeRingSizes();

        Selection.activeGameObject = scanner;
        EditorUtility.SetDirty(scanner);

        // 存盘。不存的话改动只活在内存里,一切场景/重开 Unity 就白干了 ——
        // 而且从外面看场景文件还是空的,很容易以为"这菜单没生效"。
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        bool saved = !string.IsNullOrEmpty(scene.path) && EditorSceneManager.SaveScene(scene);

        string report =
            $"· OVRCameraRig:{(rigAdded ? "已添加" : "已存在 / 未找到预制件")}\n"
            + $"· MRUK:{(mrukAdded ? "已添加并打开二维码追踪" : "已存在")}\n"
            + $"· 扫码脚本:{scanner.name}\n"
            + $"· 黑底:已设到 {cameras} 台相机(纯色清屏,Alpha=0)\n"
            + $"· 光圈大小:刷了 {rings} 个存着旧值的组件 → {LijiangEchoPatternIntro.DefaultRingSize}\n"
            + $"· 场景:{(saved ? "已保存" : "未保存 —— 记得 Ctrl+S")}";

        Debug.Log("[漓江回声] 扫码场景已就绪。\n" + report
            + "\n\n直接点 Play:电脑上按 1/2/3/4 = 鱼/蛇/蛙/鸟,演出出现在相机正前方。"
            + "\n打包上头显:把打印的二维码放进视野即可(lijiang:fish / snake / frog / bird)。"
            + "\n入场动画演完后接打击:鱼纹用对应那只手单击、蛇纹按住、蛙纹向上挥、鸟纹双手同时。");

        EditorUtility.DisplayDialog("漓江回声 · 扫码场景已就绪", report + "\n\n可以直接点 Play 了。", "好");
    }

    /// <summary>给场景里的相机铺上黑底(纯色清屏 + Alpha 0)。
    ///
    /// 电脑上看得见 —— 纹样在黑底上才看得清;头显上看不见 —— Passthrough 按 Alpha
    /// 合成,0 就是"这里全给真实世界"。和其他场景、和 LijiangEchoMrValidation 的
    /// 要求都是同一套设置。存进场景,Play 之前在 Game 视图里就已经是黑的。</summary>
    private static int ApplyBackdropToCameras()
    {
        int touched = 0;
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera cam in cameras)
        {
            if (cam == null)
            {
                continue;
            }

            Undo.RecordObject(cam, "设置黑色底");
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.04f, 0.03f, 0.055f, 0f);
            EditorUtility.SetDirty(cam);
            touched++;
        }

        return touched;
    }

    /// <summary>把场景里已经【存过】的入场/打击组件的光圈大小刷成当前默认值。
    ///
    /// Unity 不会用脚本里的新默认值覆盖已序列化的实例 —— Scanplay 里存着的那个
    /// LijiangEchoPatternIntro 光圈还是老的 0.62,而打击是运行时新建的、拿的是新默认值,
    /// 于是两段光圈一大一小。运行时扫码脚本会统一下发一遍,这里再把存盘的值也一起刷掉,
    /// 免得编辑器预览里看着还是旧的。</summary>
    private static int NormalizeRingSizes()
    {
        int count = 0;

        foreach (LijiangEchoPatternIntro intro in
                 Object.FindObjectsByType<LijiangEchoPatternIntro>(FindObjectsSortMode.None))
        {
            if (intro == null || Mathf.Approximately(intro.RingSize, LijiangEchoPatternIntro.DefaultRingSize))
            {
                continue;
            }

            Undo.RecordObject(intro, "统一光圈大小");
            intro.RingSize = LijiangEchoPatternIntro.DefaultRingSize;
            EditorUtility.SetDirty(intro);
            count++;
        }

        foreach (LijiangEchoPatternStrike strike in
                 Object.FindObjectsByType<LijiangEchoPatternStrike>(FindObjectsSortMode.None))
        {
            if (strike == null || Mathf.Approximately(strike.RingSize, LijiangEchoPatternIntro.DefaultRingSize))
            {
                continue;
            }

            Undo.RecordObject(strike, "统一光圈大小");
            strike.RingSize = LijiangEchoPatternIntro.DefaultRingSize;
            EditorUtility.SetDirty(strike);
            count++;
        }

        return count;
    }

    /// <summary>MRUK 的 Awake 里会硬性检查 OVRCameraRig,没有就直接报错。</summary>
    private static bool EnsureCameraRig()
    {
        if (Object.FindFirstObjectByType<OVRCameraRig>() != null)
        {
            return false;
        }

        GameObject prefab = FindPrefab("OVRCameraRig");
        if (prefab != null)
        {
            GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.name = "OVRCameraRig";
            Undo.RegisterCreatedObjectUndo(rig, "添加 OVRCameraRig");
            RemovePlainMainCamera();
            return true;
        }

        Debug.LogWarning("[漓江回声] 没找到 OVRCameraRig 预制件。\n"
            + "请用 Meta / Tools / Building Blocks / Camera Rig 手动加一个 —— MRUK 依赖它。\n"
            + "(只在电脑上按 1/2/3/4 跑测的话,不加也能看动画,只是真扫码用不了。)");
        return false;
    }

    private static bool EnsureMruk()
    {
        if (Object.FindFirstObjectByType<MRUK>() != null)
        {
            return false;
        }

        GameObject prefab = FindPrefab("MRUK");
        GameObject mruk;
        if (prefab != null)
        {
            mruk = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            mruk.name = "MRUK";
        }
        else
        {
            mruk = new GameObject("MRUK");
            mruk.AddComponent<MRUK>();
        }

        Undo.RegisterCreatedObjectUndo(mruk, "添加 MRUK");

        // 我们只要二维码追踪,不需要它去加载房间网格 —— 那个还要用户先做过房间扫描
        MRUK component = mruk.GetComponent<MRUK>();
        if (component != null && component.SceneSettings != null)
        {
            component.SceneSettings.LoadSceneOnStartup = false;

            // TrackerConfiguration 是 struct,取出来改完塞回去
            OVRAnchor.TrackerConfiguration config = component.SceneSettings.TrackerConfiguration;
            config.QRCodeTrackingEnabled = true;
            component.SceneSettings.TrackerConfiguration = config;
        }

        return true;
    }

    private static GameObject EnsureScanner()
    {
        LijiangEchoQrScan existing = Object.FindFirstObjectByType<LijiangEchoQrScan>();
        if (existing != null)
        {
            EnsureTunableModules(existing.gameObject);   // 已经搭过的场景也补上,好在 Inspector 里调
            return existing.gameObject;
        }

        GameObject host = new GameObject("漓江回声_扫码");
        host.AddComponent<LijiangEchoQrScan>();
        Undo.RegisterCreatedObjectUndo(host, "添加漓江回声扫码");
        EnsureTunableModules(host);
        return host;
    }

    /// <summary>把入场动画和打击这两个组件也【提前挂到场景里】。
    ///
    /// 它们本来是运行时 AddComponent 出来的,能跑,但有个坏处:Play 之前 Inspector 里
    /// 根本看不到它们的参数,音符大小、飞入距离、扭动幅度这些就没法调 —— 只能改代码。
    /// 现在搭场景时就挂上,所有参数都能在 Inspector 里直接拖,运行时那两句
    /// AddComponent 会因为 GetComponent 拿得到而自动跳过。</summary>
    private static void EnsureTunableModules(GameObject host)
    {
        if (host.GetComponent<LijiangEchoPatternIntro>() == null)
        {
            Undo.AddComponent<LijiangEchoPatternIntro>(host);
        }

        if (host.GetComponent<LijiangEchoPatternStrike>() == null)
        {
            Undo.AddComponent<LijiangEchoPatternStrike>(host);
        }
    }

    /// <summary>新建场景自带的那台 Main Camera 会和 OVRCameraRig 打架(两个 AudioListener、两台相机)。</summary>
    private static void RemovePlainMainCamera()
    {
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
        foreach (Camera cam in cameras)
        {
            if (cam == null || cam.GetComponentInParent<OVRCameraRig>() != null)
            {
                continue;
            }

            if (cam.gameObject.name == "Main Camera")
            {
                Undo.DestroyObjectImmediate(cam.gameObject);
            }
        }
    }

    /// <summary>按名字在工程和包里找预制件。Meta 的东西在 PackageCache 里,路径带版本哈希,不能写死。</summary>
    private static GameObject FindPrefab(string exactName)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{exactName} t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) != exactName)
            {
                continue;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                return prefab;
            }
        }

        return null;
    }

    // ————————————————————————————— Play 时手动触发 —————————————————————————————

    [MenuItem(Root + "模拟扫到:鱼纹", false, 20)]
    private static void SimFish() => Simulate(LijiangEchoPatternIntro.Pattern.Fish);

    [MenuItem(Root + "模拟扫到:蛇纹", false, 21)]
    private static void SimSnake() => Simulate(LijiangEchoPatternIntro.Pattern.Snake);

    [MenuItem(Root + "模拟扫到:蛙纹", false, 22)]
    private static void SimFrog() => Simulate(LijiangEchoPatternIntro.Pattern.Frog);

    [MenuItem(Root + "模拟扫到:鸟纹", false, 23)]
    private static void SimBird() => Simulate(LijiangEchoPatternIntro.Pattern.Bird);

    private static void Simulate(LijiangEchoPatternIntro.Pattern pattern)
    {
        if (!Application.isPlaying)
        {
            EditorUtility.DisplayDialog("漓江回声",
                "模拟扫码要在 Play 模式下。\n\nPlay 之后也可以直接按键盘 1/2/3/4。", "知道了");
            return;
        }

        LijiangEchoQrScan scanner = Object.FindFirstObjectByType<LijiangEchoQrScan>();
        if (scanner == null)
        {
            EditorUtility.DisplayDialog("漓江回声",
                "场景里没有扫码脚本。\n先跑一次「一键搭好当前场景」。", "知道了");
            return;
        }

        scanner.SimulateScan(pattern);
    }
}
