using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// 漓江回声各阶段场景共用的拼装/输入/动效基础设施。
/// 从 LijiangEchoGameController 中提炼出来，供拆分出的各阶段场景脚本复用，
/// 避免每个阶段场景重复实现同一套精灵拼装与 VR 输入逻辑。
/// </summary>
public static class LijiangEchoStageKit
{
    public const string ArtRoot = "LijiangEchoArt/";
    public const float PixelsPerUnit = 520f;
    public const float MainCanvasWidth = 5.65f;
    public const float WideStripWidth = 6.05f;
    public const float StageDistance = 2.35f;
    public const float StageWorldScale = 0.78f;
    public const float TracePlaneZ = -0.72f;

    public enum MotionKind
    {
        FloatY,
        FloatX,
        Pulse,
        Flame,
        Monster,
        Wing,
        Hand
    }

    public sealed class MotionItem
    {
        public Transform Transform;
        public SpriteRenderer Renderer;
        public Vector3 BasePosition;
        public Vector3 BaseScale;
        public Quaternion BaseRotation;
        public Color BaseColor;
        public MotionKind Kind;
        public float Speed;
        public float Amplitude;
        public float Phase;
    }

    private static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();
    private static readonly Dictionary<string, Texture2D> solidTextureCache = new Dictionary<string, Texture2D>();
    private static readonly Dictionary<string, AudioClip> audioCache = new Dictionary<string, AudioClip>();
    private static Font uiFont;

    private static Transform persistentParent;
    private static AudioSource ambienceSource;
    private static AudioSource sfxSource;

    private static Camera previewCamera;
    private static Transform leftControllerAnchor;
    private static Transform rightControllerAnchor;
    private static LineRenderer leftControllerRay;
    private static LineRenderer rightControllerRay;
    private static Transform leftControllerReticle;
    private static Transform rightControllerReticle;

    private static bool leftControllerTracked;
    private static bool rightControllerTracked;
    private static float leftTriggerValue;
    private static float rightTriggerValue;
    private static float previousLeftTriggerValue;
    private static float previousRightTriggerValue;
    private static bool leftTriggerDown;
    private static bool rightTriggerDown;

    /// <summary>由 LijiangEchoGameFlow 在 Bootstrap 场景启动时调用一次。</summary>
    public static void Bind(Transform persistentRoot, AudioSource ambience, AudioSource sfx)
    {
        persistentParent = persistentRoot;
        ambienceSource = ambience;
        sfxSource = sfx;
    }

    // ---------------------------------------------------------------
    // 相机 / 舞台锚定
    // ---------------------------------------------------------------

    public static Camera FindGameplayCamera()
    {
        Camera[] cameras = Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Camera taggedMainCamera = null;
        Camera firstEnabledCamera = null;

        foreach (Camera camera in cameras)
        {
            if (camera == null || camera.targetTexture != null)
            {
                continue;
            }

            if (camera.name == "CenterEyeAnchor" || camera.name.Contains("CenterEye"))
            {
                return camera;
            }

            if (!camera.isActiveAndEnabled)
            {
                continue;
            }

            if (taggedMainCamera == null && camera.CompareTag("MainCamera"))
            {
                taggedMainCamera = camera;
            }

            if (firstEnabledCamera == null)
            {
                firstEnabledCamera = camera;
            }
        }

        return taggedMainCamera != null ? taggedMainCamera : firstEnabledCamera;
    }

    /// <summary>找不到真机相机时（Editor 无头显预览），创建一个常驻的兜底相机。</summary>
    public static Camera EnsureCamera()
    {
        Camera camera = FindGameplayCamera();
        if (camera != null)
        {
            return camera;
        }

        if (previewCamera != null)
        {
            return previewCamera;
        }

        GameObject cameraObject = new GameObject("漓江回声_预览相机");
        if (persistentParent != null)
        {
            cameraObject.transform.SetParent(persistentParent, false);
        }

        previewCamera = cameraObject.AddComponent<Camera>();
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = Vector3.zero;
        cameraObject.transform.rotation = Quaternion.identity;
        previewCamera.clearFlags = CameraClearFlags.SolidColor;
        previewCamera.backgroundColor = new Color(0.04f, 0.03f, 0.055f);
        return previewCamera;
    }

    public static bool IsHeadPoseTracked()
    {
        UnityEngine.XR.InputDevice headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        if (!headDevice.isValid)
        {
            return false;
        }

        if (headDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked))
        {
            return tracked;
        }

        return headDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out _);
    }

    /// <summary>
    /// 在当前激活场景里创建一个新的舞台根节点，锚定在相机前方（不随转头移动）。
    /// 供尚未场景化的阶段使用；已场景化的阶段请改用 AnchorStageRoot。
    /// </summary>
    public static Transform PrepareStageRoot(string rootName, float verticalOffset = 0f)
    {
        GameObject rootObject = new GameObject(rootName);
        AnchorStageRoot(rootObject.transform, verticalOffset);
        return rootObject.transform;
    }

    /// <summary>
    /// 把一个已存在的舞台根节点摆到相机前方。场景化后的阶段，其根节点预先放在场景里
    /// （这样美术内容作为子物体在 Scene 视图中可见可拖），运行时只需要重新定位。
    /// </summary>
    /// <param name="verticalOffset">额外的抬高量(米)。默认 0 = 原位置。
    /// 9.1 需求第 1 条要把开始界面抬到平视中心,由 StartStageController 传 0.20。</param>
    public static void AnchorStageRoot(Transform stageRoot, float verticalOffset = 0f)
    {
        if (stageRoot == null)
        {
            return;
        }

        Camera camera = EnsureCamera();

        Vector3 forward = Vector3.ProjectOnPlane(camera.transform.forward, Vector3.up);
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.forward;
        }

        forward.Normalize();
        stageRoot.SetParent(null, true);
        stageRoot.position = camera.transform.position + forward * StageDistance
                             + Vector3.down * 0.02f
                             + Vector3.up * verticalOffset;
        stageRoot.rotation = Quaternion.LookRotation(forward, Vector3.up);
        stageRoot.localScale = Vector3.one * StageWorldScale;

        CacheControllerAnchors();
    }

    private static void CacheControllerAnchors()
    {
        if (leftControllerAnchor == null)
        {
            GameObject leftObject = GameObject.Find("LeftControllerAnchor");
            if (leftObject != null)
            {
                leftControllerAnchor = leftObject.transform;
            }
        }

        if (rightControllerAnchor == null)
        {
            GameObject rightObject = GameObject.Find("RightControllerAnchor");
            if (rightObject != null)
            {
                rightControllerAnchor = rightObject.transform;
            }
        }
    }

    // ---------------------------------------------------------------
    // VR 输入 / 悬停判定
    // ---------------------------------------------------------------

    public static void UpdateControllerInput(Transform stageRoot)
    {
        CacheControllerAnchors();

        UnityEngine.XR.InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        UnityEngine.XR.InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        OVRInput.Controller connected = OVRInput.GetConnectedControllers();

        leftControllerTracked = IsTracked(leftDevice) || (connected & OVRInput.Controller.LTouch) != 0;
        rightControllerTracked = IsTracked(rightDevice) || (connected & OVRInput.Controller.RTouch) != 0;

        leftTriggerValue = ReadTrigger(leftDevice, OVRInput.Controller.LTouch);
        rightTriggerValue = ReadTrigger(rightDevice, OVRInput.Controller.RTouch);
        leftTriggerDown = leftTriggerValue >= 0.55f && previousLeftTriggerValue < 0.55f;
        rightTriggerDown = rightTriggerValue >= 0.55f && previousRightTriggerValue < 0.55f;

        EnsureControllerPointerVisuals();
        UpdateControllerPointerVisual(stageRoot, leftControllerAnchor, leftControllerRay, leftControllerReticle, leftControllerTracked, leftTriggerValue);
        UpdateControllerPointerVisual(stageRoot, rightControllerAnchor, rightControllerRay, rightControllerReticle, rightControllerTracked, rightTriggerValue);

        previousLeftTriggerValue = leftTriggerValue;
        previousRightTriggerValue = rightTriggerValue;
    }

    private static bool IsTracked(UnityEngine.XR.InputDevice device)
    {
        if (!device.isValid)
        {
            return false;
        }

        if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.isTracked, out bool tracked))
        {
            return tracked;
        }

        return device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out _);
    }

    private static float ReadTrigger(UnityEngine.XR.InputDevice device, OVRInput.Controller controller)
    {
        float value = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, controller);
        if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float xrValue))
        {
            value = Mathf.Max(value, xrValue);
        }

        if (device.isValid && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.triggerButton, out bool pressed) && pressed)
        {
            value = Mathf.Max(value, 1f);
        }

        return value;
    }

    /// <summary>隐藏 StageKit 的手柄射线/落点(它们挂在持久根上,进入不调用 UpdateControllerInput 的场景(如旧战斗场景)
    /// 后会停留在最后位置变成"残留射线")。进这类场景时调一次即可;之后再次调用 UpdateControllerInput 会自动恢复。</summary>
    public static void HideControllerPointers()
    {
        if (leftControllerRay != null) { leftControllerRay.enabled = false; }
        if (rightControllerRay != null) { rightControllerRay.enabled = false; }
        if (leftControllerReticle != null) { leftControllerReticle.gameObject.SetActive(false); }
        if (rightControllerReticle != null) { rightControllerReticle.gameObject.SetActive(false); }
    }

    private static void EnsureControllerPointerVisuals()
    {
        if (leftControllerRay == null)
        {
            CreateControllerPointer("左手描画射线", new Color(0.27f, 1f, 0.82f, 0.95f), out leftControllerRay, out leftControllerReticle);
        }

        if (rightControllerRay == null)
        {
            CreateControllerPointer("右手描画射线", new Color(1f, 0.72f, 0.24f, 0.95f), out rightControllerRay, out rightControllerReticle);
        }
    }

    private static void CreateControllerPointer(string pointerName, Color color, out LineRenderer line, out Transform reticle)
    {
        GameObject lineObject = new GameObject(pointerName);
        if (persistentParent != null)
        {
            lineObject.transform.SetParent(persistentParent, false);
        }

        line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = 0.009f;
        line.endWidth = 0.004f;
        line.startColor = color;
        line.endColor = new Color(color.r, color.g, color.b, 0.35f);
        line.numCapVertices = 5;

        Shader lineShader = Shader.Find("Sprites/Default");
        if (lineShader != null)
        {
            line.sharedMaterial = new Material(lineShader);
        }

        GameObject reticleObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        reticleObject.name = pointerName + "落点";
        if (persistentParent != null)
        {
            reticleObject.transform.SetParent(persistentParent, false);
        }

        reticleObject.transform.localScale = Vector3.one * 0.035f;
        Collider reticleCollider = reticleObject.GetComponent<Collider>();
        if (reticleCollider != null)
        {
            Object.Destroy(reticleCollider);
        }

        Renderer reticleRenderer = reticleObject.GetComponent<Renderer>();
        Shader reticleShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (reticleShader == null)
        {
            reticleShader = Shader.Find("Sprites/Default");
        }

        if (reticleRenderer != null && reticleShader != null)
        {
            Material reticleMaterial = new Material(reticleShader);
            reticleMaterial.color = color;
            reticleRenderer.sharedMaterial = reticleMaterial;
        }

        reticle = reticleObject.transform;
        line.enabled = false;
        reticleObject.SetActive(false);
    }

    private static void UpdateControllerPointerVisual(
        Transform stageRoot,
        Transform controller,
        LineRenderer line,
        Transform reticle,
        bool tracked,
        float triggerValue)
    {
        bool visible = tracked && controller != null && line != null && reticle != null;
        if (line != null)
        {
            line.enabled = visible;
        }

        if (reticle != null)
        {
            reticle.gameObject.SetActive(visible);
        }

        if (!visible)
        {
            return;
        }

        Vector3 origin = controller.position;
        Vector3 direction = GetControllerRayDirection(stageRoot, controller);
        Vector3 target = origin + direction * 2f;
        if (stageRoot != null && TryProjectRay(stageRoot, new Ray(origin, direction), out Vector3 localPoint))
        {
            target = stageRoot.TransformPoint(localPoint);
        }

        line.SetPosition(0, origin);
        line.SetPosition(1, target);
        reticle.position = target;
        reticle.localScale = Vector3.one * Mathf.Lerp(0.035f, 0.052f, triggerValue);
    }

    private static Vector3 GetControllerRayDirection(Transform stageRoot, Transform controller)
    {
        Vector3 forward = controller.forward;
        if (stageRoot == null)
        {
            return forward;
        }

        Vector3 toStage = stageRoot.position - controller.position;
        return Vector3.Dot(forward, toStage) >= Vector3.Dot(-forward, toStage) ? forward : -forward;
    }

    private static bool TryProjectControllerRay(Transform stageRoot, Transform controller, out Vector3 localPoint)
    {
        return TryProjectRay(stageRoot, new Ray(controller.position, GetControllerRayDirection(stageRoot, controller)), out localPoint);
    }

    private static bool TryProjectRay(Transform stageRoot, Ray ray, out Vector3 localPoint)
    {
        Vector3 planePoint = stageRoot.TransformPoint(new Vector3(0f, 0f, TracePlaneZ));
        Plane plane = new Plane(stageRoot.forward, planePoint);
        if (plane.Raycast(ray, out float distance) && distance > 0f && distance < 8f)
        {
            localPoint = stageRoot.InverseTransformPoint(ray.GetPoint(distance));
            return Mathf.Abs(localPoint.x) <= 2.25f && Mathf.Abs(localPoint.y) <= 1.2f;
        }

        localPoint = Vector3.zero;
        return false;
    }

    /// <summary>某个手柄的射线是否落在 stageRoot 局部坐标下的 localBounds 区域内，并返回本帧是否有确认按下。</summary>
    public static bool TryGetControllerHover(Transform stageRoot, Rect localBounds, out bool pressed)
    {
        bool leftHover = leftControllerTracked &&
                         leftControllerAnchor != null &&
                         TryProjectControllerRay(stageRoot, leftControllerAnchor, out Vector3 leftPoint) &&
                         localBounds.Contains(new Vector2(leftPoint.x, leftPoint.y));
        bool rightHover = rightControllerTracked &&
                          rightControllerAnchor != null &&
                          TryProjectControllerRay(stageRoot, rightControllerAnchor, out Vector3 rightPoint) &&
                          localBounds.Contains(new Vector2(rightPoint.x, rightPoint.y));

        pressed = (leftHover && leftTriggerDown) || (rightHover && rightTriggerDown);
        return leftHover || rightHover;
    }

    /// <summary>取当前「活动指针」在 stageRoot 局部平面上的落点 + 是否处于按住状态(扳机/鼠标左键)。
    /// 哪只手扳机压得深就用哪只;编辑器下退回鼠标。供选关滚轮做「摁住左右拖滑」。</summary>
    public static bool TryGetActivePointer(Transform stageRoot, out Vector3 localPoint, out bool held)
    {
        bool useRight = rightTriggerValue > leftTriggerValue + 0.04f ||
                        (!leftControllerTracked && rightControllerTracked);
        Transform controller = useRight ? rightControllerAnchor : leftControllerAnchor;
        bool tracked = useRight ? rightControllerTracked : leftControllerTracked;
        held = Mathf.Max(leftTriggerValue, rightTriggerValue) > 0.35f;

        if (tracked && controller != null && TryProjectControllerRay(stageRoot, controller, out localPoint))
        {
            return true;
        }

        if (Mouse.current != null && stageRoot != null)
        {
            Camera cam = FindGameplayCamera();
            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (TryProjectRay(stageRoot, ray, out localPoint))
                {
                    held = Mouse.current.leftButton.isPressed;
                    return true;
                }
            }
        }

        localPoint = Vector3.zero;
        held = false;
        return false;
    }

    /// <summary>
    /// 取【指定某只手】的射线在 stageRoot 平面上的落点 + 该手扳机是否按住(仅该手,左右互不干扰)。
    /// 供「双手各描各的半只」的描绘阶段:右手判右半、左手判左半。先调 UpdateControllerInput。
    /// </summary>
    public static bool TryGetHandPointer(Transform stageRoot, bool right, out Vector3 localPoint, out bool drawing)
    {
        Transform controller = right ? rightControllerAnchor : leftControllerAnchor;
        bool tracked = right ? rightControllerTracked : leftControllerTracked;
        float trigger = right ? rightTriggerValue : leftTriggerValue;
        drawing = trigger > 0.35f;

        if (tracked && controller != null && TryProjectControllerRay(stageRoot, controller, out localPoint))
        {
            return true;
        }

        localPoint = Vector3.zero;
        drawing = false;
        return false;
    }

    /// <summary>编辑器鼠标落点 + 左键是否按下(无手柄时给描绘兜底)。</summary>
    public static bool TryGetMousePointer(Transform stageRoot, out Vector3 localPoint, out bool drawing)
    {
        if (Mouse.current != null && stageRoot != null)
        {
            Camera cam = FindGameplayCamera();
            if (cam != null)
            {
                Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                if (TryProjectRay(stageRoot, ray, out localPoint))
                {
                    drawing = Mouse.current.leftButton.isPressed;
                    return true;
                }
            }
        }

        localPoint = Vector3.zero;
        drawing = false;
        return false;
    }

    public static bool NonPointerConfirmPressed()
    {
        bool keyboardPressed = Keyboard.current != null &&
                               (Keyboard.current.spaceKey.wasPressedThisFrame ||
                                Keyboard.current.enterKey.wasPressedThisFrame ||
                                Keyboard.current.numpadEnterKey.wasPressedThisFrame);
        bool mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool faceButtonPressed = OVRInput.GetDown(OVRInput.Button.One);
        return keyboardPressed || mousePressed || faceButtonPressed;
    }

    public static int ReadHorizontalStep()
    {
        float value = 0f;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.leftArrowKey.isPressed || Keyboard.current.aKey.isPressed)
            {
                value -= 1f;
            }

            if (Keyboard.current.rightArrowKey.isPressed || Keyboard.current.dKey.isPressed)
            {
                value += 1f;
            }
        }

        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        UnityEngine.XR.InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        UnityEngine.XR.InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (leftDevice.isValid && leftDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 leftStick) &&
            Mathf.Abs(leftStick.x) > Mathf.Abs(stick.x))
        {
            stick = leftStick;
        }

        if (rightDevice.isValid && rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 rightStick) &&
            Mathf.Abs(rightStick.x) > Mathf.Abs(stick.x))
        {
            stick = rightStick;
        }

        if (Mathf.Abs(stick.x) > Mathf.Abs(value))
        {
            value = stick.x;
        }

        if (value > 0.45f)
        {
            return 1;
        }

        if (value < -0.45f)
        {
            return -1;
        }

        return 0;
    }

    /// <summary>
    /// 读「向前推」的连续量:摇杆前推(Y 正)= 正值,后拉 = 负值,范围约 [-1,1](带死区)。
    /// 用于「手动往前走」的过场:玩家推摇杆才前进,松开就停。PC 测试可用 W/↑(前)、S/↓(后)。
    /// 左右手任一摇杆的 |y| 较大者为准。
    /// </summary>
    public static float ReadForwardAxis()
    {
        float value = 0f;
        if (Keyboard.current != null)
        {
            if (Keyboard.current.upArrowKey.isPressed || Keyboard.current.wKey.isPressed)
            {
                value += 1f;
            }

            if (Keyboard.current.downArrowKey.isPressed || Keyboard.current.sKey.isPressed)
            {
                value -= 1f;
            }
        }

        Vector2 stick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        UnityEngine.XR.InputDevice leftDevice = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        UnityEngine.XR.InputDevice rightDevice = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (leftDevice.isValid && leftDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 leftStick) &&
            Mathf.Abs(leftStick.y) > Mathf.Abs(stick.y))
        {
            stick = leftStick;
        }

        if (rightDevice.isValid && rightDevice.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primary2DAxis, out Vector2 rightStick) &&
            Mathf.Abs(rightStick.y) > Mathf.Abs(stick.y))
        {
            stick = rightStick;
        }

        if (Mathf.Abs(stick.y) > Mathf.Abs(value))
        {
            value = stick.y;
        }

        if (Mathf.Abs(value) < 0.12f)   // 死区,防漂移
        {
            return 0f;
        }

        return Mathf.Clamp(value, -1f, 1f);
    }

    // ---------------------------------------------------------------
    // 精灵拼装
    // ---------------------------------------------------------------

    public static GameObject AddLayer(Transform stageRoot, List<GameObject> spawned, string resourcePath, string objectName, Vector3 localPosition, float targetWidth, int order, float alpha = 1f, Transform parent = null)
    {
        GameObject spriteObject = AddSprite(stageRoot, spawned, resourcePath, objectName, localPosition, Vector3.one, order, alpha, false, parent);
        SpriteRenderer renderer = spriteObject.GetComponent<SpriteRenderer>();
        FitRendererWidth(renderer, targetWidth);
        AttachSpriteLayer(spriteObject, renderer, LijiangEchoSpriteLayer.FitMode.Width, targetWidth, order, alpha);
        return spriteObject;
    }

    public static GameObject AddIcon(Transform stageRoot, List<GameObject> spawned, string resourcePath, string objectName, Vector3 visibleCenter, float targetHeight, int order, float alpha = 1f)
    {
        GameObject spriteObject = AddSprite(stageRoot, spawned, resourcePath, objectName, visibleCenter, Vector3.one, order, alpha, true);
        SpriteRenderer renderer = spriteObject.GetComponent<SpriteRenderer>();
        FitRendererHeight(renderer, targetHeight);
        PlaceVisibleCenter(spriteObject.transform, renderer, visibleCenter);
        AttachSpriteLayer(spriteObject, renderer, LijiangEchoSpriteLayer.FitMode.Height, targetHeight, order, alpha);
        return spriteObject;
    }

    /// <summary>
    /// 给运行时生成的图层补上 LijiangEchoSpriteLayer，记录它「按什么拟合、拟合到多大」。
    /// 场景化烘焙工具据此判断每个物体该用哪种拟合模式，不必再去猜。
    /// 注意：先赋值字段再挂组件会触发 Awake 里的 Apply，故这里直接用已算好的值填充，
    /// Apply 的结果与上面几行的计算等价，不会改变外观。
    /// </summary>
    private static void AttachSpriteLayer(
        GameObject spriteObject,
        SpriteRenderer renderer,
        LijiangEchoSpriteLayer.FitMode fitMode,
        float fitSize,
        int order,
        float alpha)
    {
        LijiangEchoSpriteLayer layer = spriteObject.AddComponent<LijiangEchoSpriteLayer>();
        layer.sprite = renderer.sprite;
        layer.fitMode = fitMode;
        layer.fitSize = fitSize;
        layer.sortingOrder = order;
        layer.alpha = alpha;
    }

    public static GameObject AddCroppedSprite(
        Transform stageRoot,
        List<GameObject> spawned,
        string resourcePath,
        string objectName,
        RectInt topLeftCrop,
        Vector3 visibleCenter,
        float targetHeight,
        int order,
        float alpha,
        bool mirrorX,
        Transform parent = null)
    {
        GameObject spriteObject = new GameObject(objectName);
        spriteObject.transform.SetParent(parent != null ? parent : stageRoot, false);

        SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetCroppedSprite(resourcePath, topLeftCrop);
        renderer.sortingOrder = order;
        SetCroppedSpritePose(renderer, visibleCenter, targetHeight, alpha, mirrorX);

        spawned.Add(spriteObject);
        return spriteObject;
    }

    public static void SetCroppedSpritePose(SpriteRenderer renderer, Vector3 visibleCenter, float targetHeight, float alpha, bool mirrorX)
    {
        if (renderer == null || renderer.sprite == null || renderer.sprite.bounds.size.y <= 0f)
        {
            return;
        }

        float scale = targetHeight / renderer.sprite.bounds.size.y;
        renderer.transform.localPosition = visibleCenter;
        renderer.transform.localRotation = Quaternion.identity;
        renderer.transform.localScale = new Vector3(mirrorX ? -scale : scale, scale, scale);
        renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
    }

    private static GameObject AddSprite(Transform stageRoot, List<GameObject> spawned, string resourcePath, string objectName, Vector3 localPosition, Vector3 localScale, int order, float alpha, bool tight, Transform parent = null)
    {
        GameObject spriteObject = new GameObject(objectName);
        spriteObject.transform.SetParent(parent != null ? parent : stageRoot, false);
        spriteObject.transform.localPosition = localPosition;
        spriteObject.transform.localRotation = Quaternion.identity;
        spriteObject.transform.localScale = localScale;

        SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetSprite(resourcePath, tight);
        renderer.sortingOrder = order;
        renderer.color = new Color(1f, 1f, 1f, alpha);

        spawned.Add(spriteObject);
        return spriteObject;
    }

    public static GameObject AddSolidRect(Transform stageRoot, List<GameObject> spawned, string objectName, Vector3 localPosition, float width, float height, Color color, int order)
    {
        GameObject spriteObject = new GameObject(objectName);
        spriteObject.transform.SetParent(stageRoot, false);
        spriteObject.transform.localPosition = localPosition;
        spriteObject.transform.localRotation = Quaternion.identity;
        spriteObject.transform.localScale = new Vector3(width, height, 1f);

        SpriteRenderer renderer = spriteObject.AddComponent<SpriteRenderer>();
        renderer.sprite = GetSolidSprite(color);
        renderer.sortingOrder = order;
        renderer.color = color;

        spawned.Add(spriteObject);
        return spriteObject;
    }

    public static LineRenderer AddLineRenderer(Transform stageRoot, List<GameObject> spawned, string objectName, float width, Color color, int order)
    {
        GameObject lineObject = new GameObject(objectName);
        lineObject.transform.SetParent(stageRoot, false);
        lineObject.transform.localPosition = Vector3.zero;
        lineObject.transform.localRotation = Quaternion.identity;
        lineObject.transform.localScale = Vector3.one;

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
        line.numCapVertices = 5;
        line.numCornerVertices = 4;
        line.sortingOrder = order;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }

        if (shader != null)
        {
            Material material = new Material(shader);
            material.color = Color.white;
            line.sharedMaterial = material;
        }

        spawned.Add(lineObject);
        return line;
    }

    public static TextMesh AddText(Transform stageRoot, List<GameObject> spawned, string text, Vector3 localPosition, float size, Color color, int order)
    {
        GameObject textObject = new GameObject("文字_" + text);
        textObject.transform.SetParent(stageRoot, false);
        textObject.transform.localPosition = localPosition;
        textObject.transform.localRotation = Quaternion.identity;
        textObject.transform.localScale = Vector3.one;

        TextMesh textMesh = textObject.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.font = GetUiFont();
        textMesh.GetComponent<MeshRenderer>().sharedMaterial = GetUiFont().material;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.fontSize = 72;
        textMesh.characterSize = size;
        textMesh.color = color;
        textMesh.richText = false;

        MeshRenderer renderer = textObject.GetComponent<MeshRenderer>();
        renderer.sortingOrder = order;

        spawned.Add(textObject);
        return textMesh;
    }

    // ---------------------------------------------------------------
    // 浮动 / 呼吸动效
    // ---------------------------------------------------------------

    public static void RegisterMotion(List<MotionItem> items, GameObject item, MotionKind kind, float amplitude, float speed, float phase)
    {
        if (item == null)
        {
            return;
        }

        SpriteRenderer renderer = item.GetComponent<SpriteRenderer>();
        items.Add(new MotionItem
        {
            Transform = item.transform,
            Renderer = renderer,
            BasePosition = item.transform.localPosition,
            BaseScale = item.transform.localScale,
            BaseRotation = item.transform.localRotation,
            BaseColor = renderer != null ? renderer.color : Color.white,
            Kind = kind,
            Speed = speed,
            Amplitude = amplitude,
            Phase = phase
        });
    }

    public static void UpdateMotions(List<MotionItem> items)
    {
        foreach (MotionItem item in items)
        {
            if (item.Transform == null)
            {
                continue;
            }

            float wave = Mathf.Sin(Time.time * item.Speed + item.Phase);
            switch (item.Kind)
            {
                case MotionKind.FloatY:
                    item.Transform.localPosition = item.BasePosition + new Vector3(0f, wave * item.Amplitude, 0f);
                    break;
                case MotionKind.FloatX:
                    item.Transform.localPosition = item.BasePosition + new Vector3(wave * item.Amplitude, 0f, 0f);
                    break;
                case MotionKind.Pulse:
                    item.Transform.localScale = item.BaseScale * (1f + wave * item.Amplitude);
                    break;
                case MotionKind.Flame:
                    item.Transform.localScale = new Vector3(
                        item.BaseScale.x * (1f + wave * item.Amplitude * 0.45f),
                        item.BaseScale.y * (1f + Mathf.Abs(wave) * item.Amplitude),
                        item.BaseScale.z);
                    if (item.Renderer != null)
                    {
                        Color color = item.BaseColor;
                        color.a = Mathf.Clamp01(item.BaseColor.a * (0.76f + Mathf.Abs(wave) * 0.26f));
                        item.Renderer.color = color;
                    }
                    break;
                case MotionKind.Monster:
                    item.Transform.localPosition = item.BasePosition + new Vector3(0f, wave * item.Amplitude, 0f);
                    item.Transform.localScale = item.BaseScale * (1f + Mathf.Abs(wave) * 0.012f);
                    break;
                case MotionKind.Wing:
                    item.Transform.localPosition = item.BasePosition + new Vector3(wave * item.Amplitude * 0.45f, wave * item.Amplitude, 0f);
                    item.Transform.localRotation = item.BaseRotation * Quaternion.Euler(0f, 0f, wave * 3.8f);
                    break;
                case MotionKind.Hand:
                    item.Transform.localPosition = item.BasePosition + new Vector3(wave * item.Amplitude * 0.65f, wave * item.Amplitude, 0f);
                    item.Transform.localRotation = item.BaseRotation * Quaternion.Euler(0f, 0f, wave * 5.4f);
                    item.Transform.localScale = item.BaseScale * (1f + Mathf.Abs(wave) * 0.008f);
                    break;
            }
        }
    }

    // ---------------------------------------------------------------
    // 缩放 / 定位辅助
    // ---------------------------------------------------------------

    private static void FitRendererWidth(SpriteRenderer renderer, float targetWidth)
    {
        if (renderer == null || renderer.sprite == null || renderer.sprite.bounds.size.x <= 0f)
        {
            return;
        }

        float scale = targetWidth / renderer.sprite.bounds.size.x;
        renderer.transform.localScale = Vector3.one * scale;
    }

    private static void FitRendererHeight(SpriteRenderer renderer, float targetHeight)
    {
        if (renderer == null || renderer.sprite == null || renderer.sprite.bounds.size.y <= 0f)
        {
            return;
        }

        float scale = targetHeight / renderer.sprite.bounds.size.y;
        renderer.transform.localScale = Vector3.one * scale;
    }

    private static readonly Dictionary<Sprite, Vector3> visibleCenterCache = new Dictionary<Sprite, Vector3>();

    /// <summary>取 Sprite【不透明像素真实中心】相对自身原点(pivot)的局部偏移。
    ///
    /// 为什么不能用 sprite.bounds.center 或 transform.position:
    /// FullRect 精灵的 bounds.center 恒为 0(= pivot 几何中心),完全反映不出内容偏心;
    /// 而这些图标贴图内容不居中、四周留白很多,物理中心和眼睛看到的中心差很远。
    /// 拿物理中心做点击判定或缩放轴心,就会出现「看到的排布」和「能点的排布」对不上。
    /// (LijiangEchoGameController.GetSpriteVisibleCenter 是同一套逻辑,那边为音符踩过同一个坑。)
    ///
    /// 贴图未开 Read/Write 时 GetPixels32 会抛异常,回退到 bounds.center —— 不会比原来更差。
    /// 结果按精灵缓存,每张只算一次。</summary>
    public static Vector3 GetSpriteVisibleCenter(Sprite sprite)
    {
        if (sprite == null)
        {
            return Vector3.zero;
        }

        if (visibleCenterCache.TryGetValue(sprite, out Vector3 cached))
        {
            return cached;
        }

        Vector3 result = sprite.bounds.center;   // 最后的回退值

        // ① 首选 Tight 网格顶点:它就是不透明区域的轮廓,而且【不要求贴图可读】。
        // 本项目的 UI 图标全是 spriteMeshType: 1(Tight) 且 isReadable: 0,
        // 所以只有这条路走得通 —— 下面的像素质心对它们会直接抛异常。
        Vector2[] vertices = sprite.vertices;
        if (vertices != null && vertices.Length > 0)
        {
            Vector2 min = vertices[0];
            Vector2 max = vertices[0];
            for (int i = 1; i < vertices.Length; i++)
            {
                min = Vector2.Min(min, vertices[i]);
                max = Vector2.Max(max, vertices[i]);
            }

            result = (Vector3)((min + max) * 0.5f);
            visibleCenterCache[sprite] = result;
            return result;
        }

        // ② 退而求其次:按 alpha 加权采像素质心(需要贴图开 Read/Write)。
        try
        {
            Texture2D texture = sprite.texture;
            Rect tr = sprite.textureRect;
            int rx = Mathf.Clamp(Mathf.RoundToInt(tr.x), 0, texture.width - 1);
            int ry = Mathf.Clamp(Mathf.RoundToInt(tr.y), 0, texture.height - 1);
            int rw = Mathf.Clamp(Mathf.RoundToInt(tr.width), 1, texture.width - rx);
            int rh = Mathf.Clamp(Mathf.RoundToInt(tr.height), 1, texture.height - ry);

            Color32[] pixels = texture.GetPixels32();
            double sumX = 0d;
            double sumY = 0d;
            double sumA = 0d;
            for (int y = 0; y < rh; y++)
            {
                int row = (ry + y) * texture.width;
                for (int x = 0; x < rw; x++)
                {
                    byte a = pixels[row + rx + x].a;
                    if (a == 0)
                    {
                        continue;
                    }

                    sumX += (x + 0.5d) * a;
                    sumY += (y + 0.5d) * a;
                    sumA += a;
                }
            }

            if (sumA > 0d)
            {
                // 质心(像素) → 相对 pivot 的局部单位偏移。
                float cx = (float)(sumX / sumA);
                float cy = (float)(sumY / sumA);
                float pivotX = sprite.pivot.x;
                float pivotY = sprite.pivot.y;
                result = new Vector3(
                    (cx - pivotX) / sprite.pixelsPerUnit,
                    (cy - pivotY) / sprite.pixelsPerUnit,
                    0f);
            }
        }
        catch (UnityException)
        {
            // 贴图没开 Read/Write:保留 bounds.center 回退值。
        }

        visibleCenterCache[sprite] = result;
        return result;
    }

    private static void PlaceVisibleCenter(Transform itemTransform, SpriteRenderer renderer, Vector3 targetCenter)
    {
        if (renderer == null || renderer.sprite == null)
        {
            return;
        }

        Vector3 localCenter = renderer.sprite.bounds.center;
        Vector3 scaledCenter = Vector3.Scale(localCenter, itemTransform.localScale);
        itemTransform.localPosition = targetCenter - scaledCenter;
    }

    private static Font GetUiFont()
    {
        if (uiFont != null)
        {
            return uiFont;
        }

        uiFont = Resources.Load<Font>("Fonts/LijiangUiFont");
        if (uiFont != null)
        {
            return uiFont;
        }

        uiFont = Font.CreateDynamicFontFromOSFont(
            new[]
            {
                "STXinwei",
                "华文新魏",
                "STXingkai",
                "华文行楷",
                "STKaiti",
                "华文楷体",
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "SimHei"
            },
            96);
        return uiFont;
    }

    // ---------------------------------------------------------------
    // 音频
    // ---------------------------------------------------------------

    private static AudioClip GetAudioClip(string clipName)
    {
        if (audioCache.TryGetValue(clipName, out AudioClip cachedClip))
        {
            return cachedClip;
        }

        AudioClip clip = Resources.Load<AudioClip>("LijiangEchoAudio/" + clipName);
        if (clip == null)
        {
            Debug.LogWarning("[漓江回声] 未找到音频资源：" + clipName);
            return null;
        }

        audioCache[clipName] = clip;
        return clip;
    }

    public static void PlayStageLoop(string clipName, float volume)
    {
        if (ambienceSource == null)
        {
            return;
        }

        AudioClip clip = GetAudioClip(clipName);
        if (clip == null)
        {
            return;
        }

        ambienceSource.volume = volume;
        if (ambienceSource.clip == clip && ambienceSource.isPlaying)
        {
            return;
        }

        ambienceSource.Stop();
        ambienceSource.clip = clip;
        ambienceSource.loop = true;
        ambienceSource.Play();
    }

    public static void StopStageLoop()
    {
        if (ambienceSource != null)
        {
            ambienceSource.Stop();
            ambienceSource.clip = null;
        }
    }

    public static void PlaySfx(string clipName, float volume)
    {
        if (sfxSource == null)
        {
            return;
        }

        AudioClip clip = GetAudioClip(clipName);
        if (clip != null)
        {
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }

    // ---------------------------------------------------------------
    // 素材加载
    // ---------------------------------------------------------------

    public static Sprite GetSprite(string resourcePath, bool tight)
    {
        string cacheKey = resourcePath + (tight ? "#tight" : "#full");
        if (spriteCache.TryGetValue(cacheKey, out Sprite cachedSprite))
        {
            return cachedSprite;
        }

        Texture2D texture = Resources.Load<Texture2D>(ArtRoot + resourcePath);
        if (texture == null)
        {
            Debug.LogWarning("[漓江回声] 未找到美术资源：" + ArtRoot + resourcePath);
            texture = CreateFallbackTexture();
        }

        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            PixelsPerUnit,
            0,
            tight ? SpriteMeshType.Tight : SpriteMeshType.FullRect);
        spriteCache[cacheKey] = sprite;
        return sprite;
    }

    public static Sprite GetCroppedSprite(string resourcePath, RectInt topLeftCrop)
    {
        string cacheKey = resourcePath + "#crop:" + topLeftCrop.x + ":" + topLeftCrop.y + ":" + topLeftCrop.width + ":" + topLeftCrop.height;
        if (spriteCache.TryGetValue(cacheKey, out Sprite cachedSprite))
        {
            return cachedSprite;
        }

        Texture2D texture = Resources.Load<Texture2D>(ArtRoot + resourcePath);
        if (texture == null)
        {
            Debug.LogWarning("[漓江回声] 未找到美术资源：" + ArtRoot + resourcePath);
            texture = CreateFallbackTexture();
            topLeftCrop = new RectInt(0, 0, texture.width, texture.height);
        }

        float sourceWidth = texture.width;
        float sourceHeight = texture.height;
        if (resourcePath.StartsWith("transition/"))
        {
            sourceWidth = 3207f;
            sourceHeight = 630f;
        }
        else if (resourcePath.StartsWith("pattern/") && resourcePath != "pattern/drawing_card")
        {
            sourceWidth = 5000f;
            sourceHeight = 5000f;
        }
        else if (resourcePath == "battle/frog_swipe")
        {
            sourceWidth = 1672f;
            sourceHeight = 941f;
        }
        else if (resourcePath == "battle/hit_ring" || resourcePath == "battle/hit_block")
        {
            sourceWidth = 3840f;
            sourceHeight = 2160f;
        }

        float scaleX = texture.width / sourceWidth;
        float scaleY = texture.height / sourceHeight;
        int x = Mathf.Clamp(Mathf.RoundToInt(topLeftCrop.x * scaleX), 0, texture.width - 1);
        int width = Mathf.Clamp(Mathf.RoundToInt(topLeftCrop.width * scaleX), 1, texture.width - x);
        int top = Mathf.Clamp(Mathf.RoundToInt(topLeftCrop.y * scaleY), 0, texture.height - 1);
        int height = Mathf.Clamp(Mathf.RoundToInt(topLeftCrop.height * scaleY), 1, texture.height - top);
        int y = texture.height - top - height;
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(x, y, width, height),
            new Vector2(0.5f, 0.5f),
            PixelsPerUnit,
            0,
            SpriteMeshType.FullRect);
        spriteCache[cacheKey] = sprite;
        return sprite;
    }

    public static Sprite GetSolidSprite(Color color)
    {
        string cacheKey = ColorUtility.ToHtmlStringRGBA(color);
        if (!solidTextureCache.TryGetValue(cacheKey, out Texture2D texture))
        {
            texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
            Color[] pixels = new Color[8 * 8];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            solidTextureCache[cacheKey] = texture;
        }

        return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 8f);
    }

    private static Texture2D CreateFallbackTexture()
    {
        Texture2D texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        Color[] pixels = new Color[64 * 64];
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color(0.7f, 0.2f, 0.9f, 0.8f);
        }

        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }
}
