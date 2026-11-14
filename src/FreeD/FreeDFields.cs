using System;

namespace FreeD
{
    /// <summary>合成の対象になる項目。ストリームごとに、どの項目を使うかを選ぶ。</summary>
    [Flags]
    public enum FreeDFields
    {
        None = 0,
        Position = 1 << 0, // X・Y・Z
        Rotation = 1 << 1, // パン・チルト・ロール
        Zoom = 1 << 2,
        Focus = 1 << 3,
    }
}
