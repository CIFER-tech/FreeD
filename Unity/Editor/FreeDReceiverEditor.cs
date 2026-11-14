using UnityEditor;

[CustomEditor(typeof(FreeDReceiver))]
public class FreeDReceiverEditor : FreeDStatusBoxEditor
{
    protected override string[] StatusPropertyNames
    {
        get
        {
            return new[] { "status.isReceiving", "status.packetsPerSecond", "status.meanIntervalMs", "status.maxIntervalMs" };
        }
    }

    protected override string[] ExcludedPropertyNames
    {
        get
        {
            return new[] { "status" };
        }
    }
}
