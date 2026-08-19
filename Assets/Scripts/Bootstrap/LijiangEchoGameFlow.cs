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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

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
        StartCoroutine(GoToStageRoutine(sceneName));
    }

    /// <summary>选关确认后，桥接进入尚未拆分的旧版流程（从过场动画开始）。</summary>
    public void EnterLegacyFlow(int selectedLevel)
    {
        SelectedLevel = selectedLevel;
        LijiangEchoGameController.ExternalSelectedLevel = selectedLevel;
        StartCoroutine(GoToStageRoutine(LegacyMainScene));
    }

    private IEnumerator GoToStageRoutine(string sceneName)
    {
        if (currentStageScene.IsValid() && currentStageScene.isLoaded)
        {
            yield return SceneManager.UnloadSceneAsync(currentStageScene);
        }

        AsyncOperation load = SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive);
        yield return load;

        Scene loadedScene = SceneManager.GetSceneByName(sceneName);
        SceneManager.SetActiveScene(loadedScene);
        currentStageScene = loadedScene;
    }
}
