using UnityEditor;
using UnityEngine;

public class SetupTrackingSpaceMovement
{
    [MenuItem("Tools/Setup TrackingSpace Movement")]
    static void Setup()
    {
        GameObject ts = GameObject.Find("TrackingSpace");
        if (ts == null)
        {
            Debug.LogError("找不到 TrackingSpace，请确认场景已打开。");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(ts, "Setup TrackingSpace Movement");

        // 添加 CharacterController
        CharacterController cc = ts.GetComponent<CharacterController>();
        if (cc == null)
        {
            cc = ts.AddComponent<CharacterController>();
            cc.height = 1.7f;
            cc.radius = 0.2f;
            cc.center = new Vector3(0, 0.85f, 0);
            Debug.Log("已添加 CharacterController");
        }

        // 添加摇杆移动脚本
        OVRThumbstickMovement mov = ts.GetComponent<OVRThumbstickMovement>();
        if (mov == null)
        {
            ts.AddComponent<OVRThumbstickMovement>();
            Debug.Log("已添加 OVRThumbstickMovement");
        }

        EditorUtility.SetDirty(ts);
        Debug.Log("TrackingSpace 移动组件配置完成。");
    }
}
