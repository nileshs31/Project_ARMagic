#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(GestureQuickRecorder))]
public class GestureQuickRecorderEditor : Editor
{
    GestureQuickRecorder t;
    void OnEnable() { t = (GestureQuickRecorder)target; }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        GUILayout.Space(8);
        EditorGUILayout.LabelField("Quick Label Buttons (click then perform gesture):", EditorStyles.boldLabel);

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Clockwise Circle")) SetLabelAndNotify("circle_cw");
        if (GUILayout.Button("Anticlockwise Circle")) SetLabelAndNotify("circle_ccw");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Square")) SetLabelAndNotify("square");
        if (GUILayout.Button("Triangle")) SetLabelAndNotify("triangle");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Spiral")) SetLabelAndNotify("spiral");
        if (GUILayout.Button("Squiggle")) SetLabelAndNotify("squiggle");
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Swipe Down")) SetLabelAndNotify("swipe_down");
        if (GUILayout.Button("Swipe Up")) SetLabelAndNotify("swipe_up");
        GUILayout.EndHorizontal();

        GUILayout.Space(6);
        if (GUILayout.Button("Save templates now"))
        {
            t.SaveTemplatesToFile();
        }
        if (GUILayout.Button("Load templates now"))
        {
            t.LoadTemplatesFromFile();
        }

        GUILayout.Space(8);
        EditorGUILayout.LabelField("Recorder status:", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(t.activeLabel != null ? $"Pending label: {t.activeLabel}\n{t.status}" : t.status, MessageType.Info);

        // small hint
        EditorGUILayout.HelpBox("Click a label button, then press & hold the stylus button and draw the gesture, then release to save.", MessageType.None);
    }

    void SetLabelAndNotify(string label)
    {
        Undo.RecordObject(t, "Set pending label");
        t.activeLabel = label;
        t.status = $"Pending label set to '{label}'. Awaiting next gesture.";
        EditorUtility.SetDirty(t);
        Debug.Log($"GestureQuickRecorder: pending label = {label}. Now draw gesture and release.");
    }
}
#endif
