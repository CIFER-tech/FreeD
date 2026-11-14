using UnityEditor;
using UnityEngine;

/// <summary>状態の欄 (StatusPropertyNames) を、枠で囲んで Inspector の下にまとめて出す。</summary>
public abstract class FreeDStatusBoxEditor : Editor
{
    protected abstract string[] StatusPropertyNames { get; }

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
            foreach (string name in StatusPropertyNames)
            {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty(name));
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    // 状態は再生中に変わり続けるので、選んでいる間は描き直す。
    public override bool RequiresConstantRepaint()
    {
        return Application.isPlaying;
    }
}
