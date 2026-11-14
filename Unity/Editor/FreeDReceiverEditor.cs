using UnityEditor;

[CustomEditor(typeof(FreeDReceiver))]
public class FreeDReceiverEditor : FreeDStatusBoxEditor
{
    protected override string[] StatusPropertyNames
    {
        get { return new[] { "receiving", "panTiltRollDeg", "positionMm", "hasZoom", "zoomRaw", "hasFocus", "focusRaw" }; }
    }
}
