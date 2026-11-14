using UnityEditor;
using UnityEngine;

/// <summary>FreeDReceiver.Stream を、設定の項目と、枠で囲んだ状態の欄に分けて描く。</summary>
[CustomPropertyDrawer(typeof(FreeDReceiver.Stream))]
public class FreeDStreamDrawer : PropertyDrawer
{
    static readonly string[] SettingNames = { "port", "cameraId", "fields", "delayMs" };
    static readonly string[] StatusNames = { "receiving", "packetsPerSecond", "meanIntervalMs", "maxIntervalMs" };

    // 枠の余白は、FreeDReceiver 本体の状態の枠 (EditorStyles.helpBox) と同じ既定値を使う。
    static RectOffset BoxPadding { get { return EditorStyles.helpBox.padding; } }

    static float LineHeight { get { return EditorGUIUtility.singleLineHeight; } }
    static float Spacing { get { return EditorGUIUtility.standardVerticalSpacing; } }

    static float FieldsHeight(SerializedProperty stream, string[] names)
    {
        float height = 0f;
        foreach (string name in names)
            height += EditorGUI.GetPropertyHeight(stream.FindPropertyRelative(name), true) + Spacing;
        return height;
    }

    static float StatusBoxHeight(SerializedProperty stream)
    {
        return BoxPadding.vertical + FieldsHeight(stream, StatusNames);
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        if (!property.isExpanded) return LineHeight;
        return LineHeight + Spacing + FieldsHeight(property, SettingNames) + Spacing + StatusBoxHeight(property);
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var line = new Rect(position.x, position.y, position.width, LineHeight);
        property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
        if (!property.isExpanded) return;

        float y = line.yMax + Spacing;
        EditorGUI.indentLevel++;
        foreach (string name in SettingNames)
            y = DrawField(position, y, property.FindPropertyRelative(name));

        y += Spacing;
        Rect box = EditorGUI.IndentedRect(new Rect(position.x, y, position.width, StatusBoxHeight(property)));
        GUI.Box(box, GUIContent.none, EditorStyles.helpBox);

        // 枠の内側に余白をとって描く。位置は枠に合わせて計算済みなので、字下げは 0 に戻す。
        Rect inner = BoxPadding.Remove(box);
        int savedIndentLevel = EditorGUI.indentLevel;
        EditorGUI.indentLevel = 0;
        float innerY = inner.y;
        foreach (string name in StatusNames)
            innerY = DrawField(inner, innerY, property.FindPropertyRelative(name));
        EditorGUI.indentLevel = savedIndentLevel;
        EditorGUI.indentLevel--;
    }

    static float DrawField(Rect position, float y, SerializedProperty field)
    {
        float height = EditorGUI.GetPropertyHeight(field, true);
        EditorGUI.PropertyField(new Rect(position.x, y, position.width, height), field, true);
        return y + height + Spacing;
    }
}
