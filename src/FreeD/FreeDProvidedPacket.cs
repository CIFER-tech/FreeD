namespace CIFER.tech.FreeD.Core
{
    public struct FreeDProvidedPacket
    {
        /// <summary>FreeDClock の時間軸 (ms)。プロバイダの TryTakeNext が、DelaySec の判定に使う。</summary>
        public double timestampMs;
        public FreeDPacket packet;
    }
}
