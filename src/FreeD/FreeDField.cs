using System;

namespace CIFER.tech.FreeD.Core
{
    [Flags]
    public enum FreeDField
    {
        None = 0,
        Rotation = 1 << 0, // パン・チルト・ロール
        Position = 1 << 1, // X・Y・Z
        Zoom = 1 << 2,
        Focus = 1 << 3,
    }
}
