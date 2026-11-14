using UnityEditor;
using UnityEngine;

/// <summary>FreeDManager の Inspector。状態の欄を、枠で囲んで出す。Receiver・カメラ ID ごとの状態は 1 行ずつのラベルで出す。</summary>
[CustomEditor(typeof(FreeDManager))]
public class FreeDManagerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        EditorGUILayout.Space();
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("receiving"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("totalPacketsPerSecond"));
            DrawReceivers(serializedObject.FindProperty("receivers"));
            DrawCameras(serializedObject.FindProperty("cameras"));
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawReceivers(SerializedProperty receivers)
    {
        for (int i = 0; i < receivers.arraySize; i++)
        {
            SerializedProperty receiver = receivers.GetArrayElementAtIndex(i);
            string label = "Port " + receiver.FindPropertyRelative("port").intValue;
            string state = receiver.FindPropertyRelative("isReceiving").boolValue ? "receiving" : "lost";
            int packetsPerSecond = receiver.FindPropertyRelative("packetsPerSecond").intValue;
            float meanMs = receiver.FindPropertyRelative("meanIntervalMs").floatValue;
            float maxMs = receiver.FindPropertyRelative("maxIntervalMs").floatValue;
            EditorGUILayout.LabelField(label, string.Format("{0}  {1} pkt/s  mean {2:F1} / max {3:F1} ms", state, packetsPerSecond, meanMs, maxMs));
        }
    }

    private static void DrawCameras(SerializedProperty cameras)
    {
        for (int i = 0; i < cameras.arraySize; i++)
        {
            SerializedProperty camera = cameras.GetArrayElementAtIndex(i);
            string label = "Camera Id " + camera.FindPropertyRelative("cameraId").intValue;
            string state = camera.FindPropertyRelative("isReceiving").boolValue ? "receiving" : "lost";
            string fields = camera.FindPropertyRelative("fields").stringValue;
            EditorGUILayout.LabelField(label, state + "  " + fields);
        }
    }

    // 状態は再生中に変わり続けるので、選んでいる間は描き直す。
    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }
}
