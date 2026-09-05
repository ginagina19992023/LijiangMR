using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 常驻于 Bootstrap 场景的流程管理器：持有跨阶段场景共用的音频源/输入基础设施，
/// 负责阶段场景之间的切换（卸载当前阶段场景、加载下一个），以及桥接到尚未拆分的
/// 旧版 LijiangEchoGameController（Intro 起的后续阶段）。
/// </summary>
public class LijiangEchoGameFlow : MonoBehaviour
{
    private const string LegacyMainScene = "LijiangEchoMR_Main";

    public static LijiangEchoGameFlow Instance { get; private set; }

    /// <summary>
    /// 由 LijiangEchoMrValidation 等自动化工具在进入 Play 模式前设置：只需要 Bootstrap 场景
    /// 提供 XR Rig（供旧版 LijiangEchoMR_Main.unity 独立测试用），不要自动加载 Stage_Start。
    /// </summary>
    public static bool SkipAutoStageLoad;

    public int SelectedLevel { get; set; }

    private Scene currentStageScene;
    private AudioSource ambienceSource;
    private AudioSource sfxSource;

    private bool transitioning;    // 正在切场景:期间来的请求排队,不并发(见 RequestStage)
    private string pendingStage;   // 切换中收到的最后一次请求

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 全局暂停菜单:它是盖在所有阶段之上的覆盖层,不属于任何一个阶段,所以挂在常驻的 Bootstrap 上。
        // 运行时自动补挂 —— Unity 那边不用拖任何东西。旧主场景在跑时它会自动让位(见 LegacyOwnsPauseMenu)。
        if (gameObject.GetComponent<LijiangEchoPauseMenu>() == null)
        {
            gameObject.AddComponent<LijiangEchoPauseMenu>();
        }

        ambienceSource = gameObject.AddComponent<AudioSource>();
        ambienceSource.playOnAwake = false;
        ambienceSource.loop = true;
        ambienceSource.spatialBlend = 0f;
        ambienceSource.priority = 128;

        sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.loop = false;
        sfxSource.spatialBlend = 0f;
        sfxSource.priority = 64;

        LijiangEchoStageKit.Bind(transform, ambienceSource, sfxSource);
    }

    private IEnumerator Start()
    {
        HidePrototypeObjects();

        float trackingWaitDeadline = Time.realtimeSinceStartup + 2f;
        while (Time.realtimeSinceStartup < trackingWaitDeadline)
        {
            Camera candidate = LijiangEchoStageKit.FindGameplayCamera();
            bool hasXrCamera = candidate != null && candidate.name.Contains("CenterEye");
            if (candidate != null && (!hasXrCamera || LijiangEchoStageKit.IsHeadPoseTracked()))
            {
                break;
            }

            yield return null;
        }

        LijiangEchoStageKit.EnsureCamera();
        if (!SkipAutoStageLoad)
        {
            yield return GoToStageRoutine("Stage_Start");
        }
    }

    private void HidePrototypeObjects()
    {
        foreach (GameObject rootObject in gameObject.scene.GetRootGameObjects())
        {
            HidePrototypeRecursive(rootObject.transform);
        }
    }

    private static void HidePrototypeRecursive(Transform item)
    {
        string objectName = item.name;
        if (objectName == "shanjingshen" ||
            objectName == "Teleport Hotspot" ||
            objectName.Contains("[BuildingBlock] Cube"))
        {
            item.gameObject.SetActive(false);
            return;
        }

        for (int i = 0; i < item.childCount; i++)
        {
            HidePrototypeRecursive(item.GetChild(i));
        }
    }

    /// <summary>卸载当前阶段场景并加载下一个阶段场景（新拆分出的场景之间跳转）。</summary>
    public void GoToStage(string sceneName)
    {
        RequestStage(sceneName);
    }

    /// <summary>选关确认后，桥接进入尚未拆分的旧版流程（从过场动画开始）。</summary>
    public void EnterLegacyFlow(int selectedLevel)
    {
        SelectedLevel = selectedLevel;
        LijiangEchoGameController.ExternalSelectedLevel = selectedLevel;
        RequestStage(LegacyMainScene);
    }

    /// <summary>
    /// 场景切换必须【串行】。以前 GoToStage/EnterLegacyFlow 直接各起一个协程,而
    /// currentStageScene 只在协程最后一行才更新 —— 两次切换一重叠就会:
    /// A 卸掉当前场景、正在加载目标场景时,B 读到的 currentStageScene 已经是"已卸载"状态,
    /// 于是 B 跳过卸载直接加载,最后两个场景同时留在内存里(上一个场景叠在新场景下面)。
    /// 暂停菜单能在任意时刻发起切换,让这个隐患变得很容易撞上。
    ///
    /// 现在:切换中再来的请求只记下【最后一次】,等当前这次切完再执行。
    /// </summary>
    private void RequestStage(string sceneName)
    {
        if (transitioning)
        {
            pendingStage = sceneName;
            return;
        }

        StartCoroutine(RunStageTransitions(sceneName));
    }

    private IEnumerator RunStageTransitions(string firstScene)
    {
        transitioning = true;

        string target = firstScene;
        while (!string.IsNullOrEmpty(target))
        {
            yield return GoToStageRoutine(target);
            target = pendingStage;
            pendingStage = null;
        }

        transitioning = false;
    }

    private IEnumerator GoToStageRoutine(string sceneName)
    {
        if (currentStageScene.IsValid() && currentStageScene.isLoaded)
        {
            Scene unloading = currentStageScene;
            currentStageScene = default;   // 先清空句柄:卸载期间别让任何人以为"当前还有场景"
            yield return SceneManager.UnloadSceneAsync(unloading);
        }

        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        yield return load;

        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        SceneManager.SetActiveScene(loadedScene);
        currentStageScene = loadedScene;
    }
}
