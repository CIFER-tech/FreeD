using CIFER.tech.FreeD.Core;

namespace CIFER.tech.FreeD.Unity
{
    /// <summary>FreeDReceiver が受信して復号したパケットと、受けた時刻 (<see cref="FreeDClock"/> の ms)。</summary>
    public struct FreeDReceivedPacket
    {
        public double receivedMs;
        public FreeDPacket packet;
    }
}
