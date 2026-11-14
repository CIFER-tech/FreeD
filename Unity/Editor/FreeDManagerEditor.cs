using UnityEditor;
using UnityEngine;

/// <summary>FreeDManager の Inspector。状態の欄を、枠で囲んで出す。カメラ ID ごとの状態は 1 行ずつのラベルで出す。</summary>
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
            EditorGUILayout.PropertyField(serializedObject.FindProperty("openedReceiverCount"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("totalPacketsPerSecond"));

            SerializedProperty cameras = serializedObject.FindProperty("cameras");
            for (int i = 0; i < cameras.arraySize; i++)
            {
                SerializedProperty camera = cameras.GetArrayElementAtIndex(i);
                string label = "Camera Id " + camera.FindPropertyRelative("cameraId").intValue;
                string receiving = camera.FindPropertyRelative("isReceiving").boolValue ? "receiving" : "lost";
                string fields = camera.FindPropertyRelative("fields").stringValue;
                EditorGUILayout.LabelField(label, receiving + "  " + fields);
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    // 状態は再生中に変わり続けるので、選んでいる間は描き直す。
    public override bool RequiresConstantRepaint() { return Application.isPlaying; }
}
