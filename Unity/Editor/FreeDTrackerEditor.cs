using UnityEditor;

[CustomEditor(typeof(FreeDTracker))]
public class FreeDTrackerEditor : FreeDStatusBoxEditor
{
    protected override string[] StatusPropertyNames
    {
        get
        {
            return new[] { "receiving", "panTiltRollDeg", "positionMm", "hasZoom", "zoomRaw", "hasFocus", "focusRaw" };
        }
    }
}
