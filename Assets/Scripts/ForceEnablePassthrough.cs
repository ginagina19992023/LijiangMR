using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Keeps Quest passthrough active and configures the stereo camera for underlay composition.
/// </summary>
[DefaultExecutionOrder(-10000)]
public sealed class ForceEnablePassthrough : MonoBehaviour
{
    private float refreshTimer;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureInstance()
    {
        if (FindFirstObjectByType<ForceEnablePassthrough>() != null)
        {
            return;
        }

        GameObject runtimeObject = new GameObject("漓江回声_MR透视管理器");
        runtimeObject.AddComponent<ForceEnablePassthrough>();
        DontDestroyOnLoad(runtimeObject);
    }

    private void Awake()
    {
        ApplyPassthroughSettings();
    }

    private void OnEnable()
    {
        ApplyPassthroughSettings();
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            ApplyPassthroughSettings();
        }
    }

    private void Update()
    {
        refreshTimer -= Time.unscaledDeltaTime;
        if (refreshTimer <= 0f)
        {
            refreshTimer = 1f;
            ApplyPassthroughSettings();
        }
    }

    private static void ApplyPassthroughSettings()
    {
        OVRManager manager = OVRManager.instance != null
            ? OVRManager.instance
            : FindFirstObjectByType<OVRManager>(FindObjectsInactive.Include);
        if (manager != null)
        {
            manager.isInsightPassthroughEnabled = true;
            manager.launchSimultaneousHandsControllersOnStartup = true;
        }

        OVRPassthroughLayer passthroughLayer = FindFirstObjectByType<OVRPassthroughLayer>(FindObjectsInactive.Include);
        if (passthroughLayer != null)
        {
            bool restartLayer = false;
#pragma warning disable CS0618
            if (passthroughLayer.overlayType != OVROverlay.OverlayType.Underlay)
            {
                passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
                restartLayer = passthroughLayer.enabled;
            }
#pragma warning restore CS0618

            passthroughLayer.hidden = false;
            if (!passthroughLayer.gameObject.activeSelf)
            {
                passthroughLayer.gameObject.SetActive(true);
            }

            if (restartLayer)
            {
                passthroughLayer.enabled = false;
            }

            passthroughLayer.enabled = true;
        }

        Camera[] cameras = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Camera cameraComponent in cameras)
        {
            if (!cameraComponent.isActiveAndEnabled || cameraComponent.targetTexture != null)
            {
                continue;
            }

            cameraComponent.clearFlags = CameraClearFlags.SolidColor;
            cameraComponent.backgroundColor = new Color(0f, 0f, 0f, 0f);
            if (GraphicsSettings.currentRenderPipeline == null)
            {
                cameraComponent.stereoTargetEye = StereoTargetEyeMask.Both;
            }
        }
    }
}
