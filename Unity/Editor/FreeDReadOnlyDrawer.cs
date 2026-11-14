using UnityEditor;
using UnityEngine;

/// <summary>[FreeDReadOnly] の欄を、値だけのラベルとして描く。</summary>
[CustomPropertyDrawer(typeof(FreeDReadOnlyAttribute))]
public class FreeDReadOnlyDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.LabelField(position, label, new GUIContent(ValueText(property)));
    }

    static string ValueText(SerializedProperty property)
    {
        switch (property.propertyType)
        {
            case SerializedPropertyType.Boolean: return property.boolValue ? "true" : "false";
            case SerializedPropertyType.Integer: return property.longValue.ToString();
            case SerializedPropertyType.Float: return property.doubleValue.ToString("0.###");
            case SerializedPropertyType.Vector3:
                Vector3 v = property.vector3Value;
                return string.Format("X {0:0.##}   Y {1:0.##}   Z {2:0.##}", v.x, v.y, v.z);
            default: return property.displayName;
        }
    }
}
