using FreeD;

/// <summary>FreeDManager が、カメラ ID ごとに合成した結果。FreeDManager が FreeDTracker に渡す値。</summary>
internal struct FreeDMergedCamera
{
    public FreeDFields filled;
    public FreeDPacket packet;
    public bool isReceiving;
}
