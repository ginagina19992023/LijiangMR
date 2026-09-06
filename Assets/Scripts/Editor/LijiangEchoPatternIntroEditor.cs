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
                + "正对着看是看不出来的。青→品红的线是轨迹,黄球起点、红球终点。",
                MessageType.None);
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
