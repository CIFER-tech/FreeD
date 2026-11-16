using CIFER.tech.FreeD.Core;

namespace CIFER.tech.FreeD.Unity
{
    internal struct FreeDMergedCamera
    {
        public FreeDPacket packet;

        // 届いていない項目は packet の値が 0 のままなので、届いたことのある項目を覚える。
        public FreeDField fields;
    }
}
