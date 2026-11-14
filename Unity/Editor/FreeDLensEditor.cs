using UnityEditor;

[CustomEditor(typeof(FreeDLens))]
public class FreeDLensEditor : FreeDStatusBoxEditor
{
    protected override string[] StatusPropertyNames
    {
        get { return new[] { "focalLengthMm", "focusDistanceM" }; }
    }
}
