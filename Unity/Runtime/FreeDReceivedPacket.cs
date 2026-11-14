using FreeD;

/// <summary>FreeDReceiver が受信して復号したパケットと、受けた時刻 (<see cref="FreeDClock"/> の ms)。</summary>
internal struct FreeDReceivedPacket
{
    public FreeDPacket packet;
    public double receivedMs;
}
