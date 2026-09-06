using UnityEditor;
using UnityEngine;

/// <summary>
/// 入场动画的 Inspector:把「看效果」这件事做成四个按钮 + 一条时间轴,
/// 不用 Play、不用在一堆字段里找 previewPattern。
///
/// 反馈:「创建了预览物体怎么播放来着」「只看到鱼纹一个,其他的不知道怎么生成」——
/// 就是因为原来只能靠一个下拉框切换,不够直观。
/// </summary>
[CustomEditor(typeof(LijiangEchoPatternIntro))]
public class LijiangEchoPatternIntroEditor : Editor
{
    private static readonly (LijiangEchoPatternIntro.Pattern Pattern, string Label)[] Buttons =
    {
        (LijiangEchoPatternIntro.Pattern.Fish, "鱼纹\n跃入"),
        (LijiangEchoPatternIntro.Pattern.Snake, "蛇纹\n缠绕"),
        (LijiangEchoPatternIntro.Pattern.Frog, "蛙纹\n荷叶"),
        (LijiangEchoPatternIntro.Pattern.Bird, "鸟纹\n盘旋")
    };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        SerializedProperty previewOn = serializedObject.FindProperty("previewInEditor");
        SerializedProperty previewPattern = serializedObject.FindProperty("previewPattern");
        SerializedProperty previewTime = serializedObject.FindProperty("previewTime");
        SerializedProperty autoPlay = serializedObject.FindProperty("autoPlayInEditor");

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("看效果(不用 Play)", EditorStyles.boldLabel);

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            if (!previewOn.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "预览没开。点下面任一个纹样按钮就会打开,并在 Scene 视图里播起来。",
                    MessageType.Info);
            }

            // ——— 四个纹样按钮 ———
            using (new EditorGUILayout.HorizontalScope())
            {
                foreach ((LijiangEchoPatternIntro.Pattern pattern, string label) in Buttons)
                {
                    bool active = previewOn.boolValue && previewPattern.enumValueIndex == (int)pattern;
                    Color old = GUI.backgroundColor;
                    if (active)
                    {
                        GUI.backgroundColor = new Color(0.45f, 0.85f, 1f);
                    }

                    if (GUILayout.Button(label, GUILayout.Height(46)))
                    {
                        previewOn.boolValue = true;
                        previewPattern.enumValueIndex = (int)pattern;
                        previewTime.floatValue = 0f;
                        autoPlay.boolValue = true;
                        FocusSceneViewOnTarget();
                    }

                    GUI.backgroundColor = old;
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                autoPlay.boolValue = GUILayout.Toggle(
                    autoPlay.boolValue, autoPlay.boolValue ? "▶ 播放中(点这里暂停)" : "⏸ 已暂停(点这里播放)",
                    EditorStyles.miniButton, GUILayout.Height(22));

                if (GUILayout.Button("从头", EditorStyles.miniButton, GUILayout.Width(48), GUILayout.Height(22)))
                {
                    previewTime.floatValue = 0f;
                }

                if (GUILayout.Button("关闭预览", EditorStyles.miniButton, GUILayout.Width(70), GUILayout.Height(22)))
                {
                    previewOn.boolValue = false;
                }
            }

            EditorGUI.BeginDisabledGroup(autoPlay.boolValue);
            previewTime.floatValue = EditorGUILayout.Slider("时间轴", previewTime.floatValue, 0f, 1f);
            EditorGUI.EndDisabledGroup();

            if (autoPlay.boolValue)
            {
                Rect bar = EditorGUILayout.GetControlRect(false, 6);
                EditorGUI.DrawRect(bar, new Color(0f, 0f, 0f, 0.25f));
                Rect fill = new Rect(bar.x, bar.y, bar.width * previewTime.floatValue, bar.height);
                EditorGUI.DrawRect(fill, new Color(0.45f, 0.85f, 1f, 0.9f));
            }

            EditorGUILayout.HelpBox(
                "在 Scene 视图里【转视角】看 —— 这套动画有纵深(鸟忽远忽近、蛇一半绕到圈后面),"
                + "正对着看是看不出来的。青→品红的线是轨迹,黄球起点、红球终点。\n\n"
                + "轨迹上的控制点是【可以直接拖的箭头】:鱼的起跳点/探头点、弧线顶点(上下=跳多高、"
                + "前后=冲多近)、蛙的荷叶与进出场点、蛇游过来的起点与缠绕半径。拖完立刻生效。",
                MessageType.None);

            if (GUILayout.Button("轨迹控制点恢复默认"))
            {
                Undo.RecordObject(target, "恢复轨迹控制点");
                ((LijiangEchoPatternIntro)target).ResetTrajectoryPoints();
                EditorUtility.SetDirty(target);
            }
        }

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("全部参数", EditorStyles.boldLabel);
        DrawPropertiesExcluding(serializedObject, "m_Script");

        serializedObject.ApplyModifiedProperties();

        // 编辑模式默认不是每帧跑的,自动播放要靠这里推着走
        if (previewOn.boolValue && autoPlay.boolValue && !Application.isPlaying)
        {
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
            Repaint();
        }
    }

    // ————————————————————————————— Scene 视图里的拖动手柄 —————————————————————————————

    /// <summary>轨迹的控制点在 Scene 视图里画成可以直接抓的箭头。
    ///
    /// 反馈:「轨迹我又用不来 我都没法调整」—— 之前 Gizmo 只是把轨迹【画出来】给你看,
    /// 点本身还写死在代码里,当然没法调。现在点都搬到了序列化字段上,这里给它们配手柄。
    /// 拖完立刻生效(鱼是每帧回读控制点的,不用等重建)。</summary>
    private void OnSceneGUI()
    {
        LijiangEchoPatternIntro intro = (LijiangEchoPatternIntro)target;
        serializedObject.Update();

        SerializedProperty previewOn = serializedObject.FindProperty("previewInEditor");
        if (previewOn == null || !previewOn.boolValue)
        {
            return;
        }

        // 控制点是相对预览根(= 本物体)的局部坐标,手柄也要在那个空间里画
        Matrix4x4 old = Handles.matrix;
        Handles.matrix = intro.transform.localToWorldMatrix;

        switch ((LijiangEchoPatternIntro.Pattern)serializedObject.FindProperty("previewPattern").enumValueIndex)
        {
            case LijiangEchoPatternIntro.Pattern.Fish:
                DrawArrayHandles("fishLeapStarts", "起跳", new Color(0.4f, 0.9f, 1f));
                DrawArrayHandles("fishPeekSpots", "探头", new Color(0.5f, 1f, 0.6f));
                DrawFishArcHandle();
                break;

            case LijiangEchoPatternIntro.Pattern.Snake:
                DrawPointHandle("snakeApproachFrom", "蛇 · 从这里游来", new Color(0.9f, 0.6f, 1f));
                DrawRingRadiusHandle("snakeCoilRadius", "缠绕半径", new Color(0.9f, 0.6f, 1f));
                break;

            case LijiangEchoPatternIntro.Pattern.Frog:
                DrawPointHandle("frogEnterFrom", "蛙 · 从这里进场", new Color(0.6f, 0.8f, 1f));
                DrawPointHandle("frogPadLeftPos", "小荷叶", new Color(0.5f, 1f, 0.7f));
                DrawPointHandle("frogOnPadLeft", "蹲在小荷叶上", new Color(0.5f, 1f, 0.7f));
                DrawPointHandle("frogPadNextPos", "下一片荷叶", new Color(0.5f, 1f, 0.7f));
                DrawPointHandle("frogExitTo", "蛙 · 从这里跳走", new Color(1f, 0.8f, 0.5f));
                break;

            default:
                DrawRingRadiusHandle("birdOrbitRadius", "盘旋半径", new Color(1f, 0.85f, 0.4f));
                break;
        }

        Handles.matrix = old;
        serializedObject.ApplyModifiedProperties();
    }

    private void DrawPointHandle(string propertyName, string label, Color color)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            return;
        }

        Handles.color = color;
        Vector3 point = prop.vector3Value;
        Handles.Label(point + Vector3.up * 0.09f, label);

        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(point, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            prop.vector3Value = moved;
        }
    }

    private void DrawArrayHandles(string propertyName, string labelPrefix, Color color)
    {
        SerializedProperty array = serializedObject.FindProperty(propertyName);
        if (array == null || !array.isArray)
        {
            return;
        }

        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty element = array.GetArrayElementAtIndex(i);
            Handles.color = color;
            Vector3 point = element.vector3Value;
            Handles.Label(point + Vector3.up * 0.09f, $"{labelPrefix} {i + 1}");

            EditorGUI.BeginChangeCheck();
            Vector3 moved = Handles.PositionHandle(point, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                element.vector3Value = moved;
            }
        }
    }

    /// <summary>鱼弧线的顶点当成一个手柄:往上拖 = 跳得更高,往靠近相机的方向拖 = 冲得更近。
    /// 比在 Inspector 里盲改两个数字直观得多。</summary>
    private void DrawFishArcHandle()
    {
        SerializedProperty starts = serializedObject.FindProperty("fishLeapStarts");
        SerializedProperty height = serializedObject.FindProperty("fishLeapHeight");
        SerializedProperty depth = serializedObject.FindProperty("fishLeapDepth");
        if (starts == null || starts.arraySize == 0 || height == null || depth == null)
        {
            return;
        }

        // 用第一条鱼的弧线做代表 —— 高度和深度是所有鱼共用的
        Vector3 from = starts.GetArrayElementAtIndex(0).vector3Value;
        Vector3 mid = Vector3.Lerp(from, Vector3.zero, 0.5f);
        Vector3 apex = mid + new Vector3(0f, height.floatValue, -depth.floatValue);

        Handles.color = new Color(1f, 0.6f, 0.3f);
        Handles.DrawDottedLine(mid, apex, 3f);
        Handles.Label(apex + Vector3.up * 0.09f, "弧线顶点\n上下=跳多高 / 前后=冲多近");

        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.PositionHandle(apex, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            height.floatValue = Mathf.Max(0f, moved.y - mid.y);
            depth.floatValue = Mathf.Max(0f, mid.z - moved.z);
        }
    }

    /// <summary>半径这种一个数字的东西,用圆 + 边上一个滑块比拖箭头合适。</summary>
    private void DrawRingRadiusHandle(string propertyName, string label, Color color)
    {
        SerializedProperty prop = serializedObject.FindProperty(propertyName);
        if (prop == null)
        {
            return;
        }

        float radius = prop.floatValue;
        Handles.color = color;
        Handles.DrawWireDisc(Vector3.zero, Vector3.forward, radius);
        Handles.Label(new Vector3(radius, 0.09f, 0f), $"{label} {radius:F2}");

        EditorGUI.BeginChangeCheck();
        Vector3 knob = Handles.Slider(new Vector3(radius, 0f, 0f), Vector3.right,
            HandleUtility.GetHandleSize(Vector3.zero) * 0.12f, Handles.DotHandleCap, 0f);
        if (EditorGUI.EndChangeCheck())
        {
            prop.floatValue = Mathf.Max(0.02f, knob.x);
        }
    }

    private void FocusSceneViewOnTarget()
    {
        SceneView view = SceneView.lastActiveSceneView;
        if (view == null)
        {
            return;
        }

        Selection.activeGameObject = ((LijiangEchoPatternIntro)target).gameObject;
        view.FrameSelected();
    }
}
