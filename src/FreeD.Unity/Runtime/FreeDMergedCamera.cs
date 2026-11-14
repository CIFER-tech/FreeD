using CIFER.tech.FreeD.Core;

namespace CIFER.tech.FreeD.Unity
{
    /// <summary>カメラ ID ごとの、合成したパケットと、Receiver から届いたことのある項目。</summary>
    internal sealed class FreeDMergedCamera
    {
        public readonly FreeDPacket packet = new FreeDPacket();
        public FreeDField fields;
    }
}
