namespace CIFER.tech.FreeD.Core
{
    /// <summary>FreeDReceiver が受信して復号したパケットと、受けた時刻 (<see cref="FreeDClock"/> の ms)。使い回すので、最初にまとめて作っておく。</summary>
    internal sealed class FreeDReceivedPacket
    {
        public double receivedMs;
        public readonly FreeDPacket packet = new FreeDPacket();
    }
}
