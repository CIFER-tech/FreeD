using UnityEditor;

[CustomEditor(typeof(FreeDTracker))]
public class FreeDTrackerEditor : FreeDStatusBoxEditor
{
    protected override string[] StatusPropertyNames
    {
        get
        {
            return new[] { "receiving", "rotation", "position", "zoomRaw", "focusRaw", "focalLengthMm", "focusDistanceM" };
        }
    }
}
