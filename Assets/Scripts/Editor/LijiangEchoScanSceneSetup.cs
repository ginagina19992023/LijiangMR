using Meta.XR.MRUtilityKit;
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
        EnsureCameraRig();
        EnsureMruk();
        GameObject scanner = EnsureScanner();

        Selection.activeGameObject = scanner;
        EditorUtility.SetDirty(scanner);

        Debug.Log("[漓江回声] 扫码场景已就绪。\n"
            + "· 直接点 Play:电脑上按 1/2/3/4 = 鱼/蛇/蛙/鸟,演出出现在相机正前方\n"
            + "· 打包上头显:把打印的二维码放进视野即可,内容是 lijiang:fish / snake / frog / bird\n"
            + "· 入场动画演完后按 空格 / 鼠标左键 / 手柄 A 完成打击占位");
    }

    /// <summary>MRUK 的 Awake 里会硬性检查 OVRCameraRig,没有就直接报错。</summary>
    private static void EnsureCameraRig()
    {
        if (Object.FindFirstObjectByType<OVRCameraRig>() != null)
        {
            return;
        }

        GameObject prefab = FindPrefab("OVRCameraRig");
        if (prefab != null)
        {
            GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            rig.name = "OVRCameraRig";
            Undo.RegisterCreatedObjectUndo(rig, "添加 OVRCameraRig");
            RemovePlainMainCamera();
            return;
        }

        Debug.LogWarning("[漓江回声] 没找到 OVRCameraRig 预制件。\n"
            + "请用 Meta / Tools / Building Blocks / Camera Rig 手动加一个 —— MRUK 依赖它。\n"
            + "(只在电脑上按 1/2/3/4 跑测的话,不加也能看动画,只是真扫码用不了。)");
    }

    private static void EnsureMruk()
    {
        if (Object.FindFirstObjectByType<MRUK>() != null)
        {
            return;
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
    }

    private static GameObject EnsureScanner()
    {
        LijiangEchoQrScan existing = Object.FindFirstObjectByType<LijiangEchoQrScan>();
        if (existing != null)
        {
            return existing.gameObject;
        }

        GameObject host = new GameObject("漓江回声_扫码");
        host.AddComponent<LijiangEchoQrScan>();       // 它自己会补上 LijiangEchoPatternIntro
        Undo.RegisterCreatedObjectUndo(host, "添加漓江回声扫码");
        return host;
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
