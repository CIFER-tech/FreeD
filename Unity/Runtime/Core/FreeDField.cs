using System;

namespace CIFER.tech.FreeD.Core
{
    /// <summary>合成の対象になる項目。ストリームごとに、どの項目を使うかを選ぶ。</summary>
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
