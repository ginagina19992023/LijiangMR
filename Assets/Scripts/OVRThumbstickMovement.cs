using UnityEngine;

/// <summary>
/// 使用 OVR 手柄摇杆控制相机 Rig 移动，挂载到 [BuildingBlock] Camera Rig 上。
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class OVRThumbstickMovement : MonoBehaviour
{
    [Header("移动设置")]
    [SerializeField] private float moveSpeed = 3f;
    [SerializeField] private bool useRightHand = false;

    private CharacterController controller;
    private Transform headTransform;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
    }

    private void Start()
    {
        headTransform = FindActiveVRCamera();
        ForceEnablePassthrough();
    }

    private void ForceEnablePassthrough()
    {
        if (OVRManager.instance != null)
        {
            OVRManager.instance.isInsightPassthroughEnabled = true;
        }

        OVRPassthroughLayer passthroughLayer = Object.FindFirstObjectByType<OVRPassthroughLayer>();
        if (passthroughLayer != null)
        {
            passthroughLayer.enabled = true;
            passthroughLayer.hidden = false;
        }

        if (headTransform != null && headTransform.TryGetComponent(out Camera cam))
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.depth = 0;
        }
    }

    private Transform FindActiveVRCamera()
    {
        Transform centerEye = transform.Find("TrackingSpace/CenterEyeAnchor");
        if (centerEye != null && centerEye.TryGetComponent(out Camera centerEyeCamera) && centerEyeCamera.enabled)
        {
            return centerEye;
        }

        foreach (Camera cameraItem in Camera.allCameras)
        {
            if (cameraItem.enabled && cameraItem.gameObject.activeInHierarchy)
            {
                return cameraItem.transform;
            }
        }

        return null;
    }

    private void Update()
    {
        OVRInput.Controller hand = useRightHand ? OVRInput.Controller.RTouch : OVRInput.Controller.LTouch;
        Vector2 thumbstick = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick, hand);

        if (thumbstick.magnitude < 0.3f)
        {
            thumbstick = Vector2.zero;
        }

        Vector3 moveDirection = Vector3.zero;
        if (thumbstick.magnitude > 0.3f)
        {
            if (headTransform == null)
            {
                headTransform = FindActiveVRCamera();
            }

            if (headTransform != null)
            {
                Vector3 forward = headTransform.forward;
                Vector3 right = headTransform.right;
                forward.y = 0;
                right.y = 0;
                forward.Normalize();
                right.Normalize();

                moveDirection = forward * thumbstick.y + right * thumbstick.x;
            }
        }

        controller.Move(moveDirection * (moveSpeed * Time.deltaTime));
    }
}
