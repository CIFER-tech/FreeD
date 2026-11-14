using FreeD;

/// <summary>FreeDManager が、カメラ ID ごとに合成した結果。FreeDTracker が TryGet で受け取る値の元。</summary>
internal struct FreeDMergedCamera
{
    public FreeDFields filled;
    public FreeDPacket packet;
    public bool isReceiving;
}
