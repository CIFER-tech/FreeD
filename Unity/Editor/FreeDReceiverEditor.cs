using UnityEditor;
using UnityEngine;

/// <summary>FreeDReceiver の Inspector。状態の欄を、枠で囲んでまとめて下に出す。</summary>
[CustomEditor(typeof(FreeDReceiver))]
public class FreeDReceiverEditor : Editor
{
    static readonly string[] StatusPropertyNames =
        { "receiving", "panTiltRollDeg", "positionMm", "hasZoom", "zoomRaw", "hasFocus", "focusRaw" };

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        var excluded = new string[StatusPropertyNames.Length + 1];
        excluded[0] = "m_Script";
        StatusPropertyNames.CopyTo(excluded, 1);
        DrawPropertiesExcluding(serializedObject, excluded);

        EditorGUILayout.Space();
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("状態", EditorStyles.boldLabel);
            foreach (string name in StatusPropertyNames)
                EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
        }

        serializedObject.ApplyModifiedProperties();
    }
}
