using System;
using FreeD;

/// <summary>届いたカメラ ID ごとの状態。FreeDManager が集計して、Inspector に出す。</summary>
[Serializable]
public class FreeDCameraStatus
{
    public int cameraId;
    public bool isReceiving;
    public string fields;
    [NonSerialized] internal FreeDFields lastFilled = (FreeDFields)(-1);
}
