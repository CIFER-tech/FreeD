namespace CIFER.tech.FreeD.Core
{
    /// <summary>
    /// 2 つの <see cref="FreeDPacket"/> を、項目ごとにまとめる。
    /// 状態は持たない。遅延や途切れの扱いは、呼び出し側で行う。
    /// </summary>
    public static class FreeDMerger
    {
        /// <summary>
        /// <paramref name="fieldA"/> の項目は <paramref name="packetA"/> から、<paramref name="fieldB"/> の項目は <paramref name="packetB"/> から取った新しいパケットを返す。
        /// どちらにも含まれない項目は既定値になる。両方に含まれる項目は <paramref name="packetB"/> の値を使う。
        /// </summary>
        public static FreeDPacket Merge(in FreeDPacket packetA, FreeDField fieldA, in FreeDPacket packetB, FreeDField fieldB)
        {
            var merged = new FreeDPacket();
            CopyFields(ref merged, packetA, fieldA);
            CopyFields(ref merged, packetB, fieldB);
            return merged;
        }

        private static void CopyFields(ref FreeDPacket merged, in FreeDPacket source, FreeDField field)
        {
            if ((field & FreeDField.Rotation) != 0)
            {
                merged.PanDegrees = source.PanDegrees;
                merged.TiltDegrees = source.TiltDegrees;
                merged.RollDegrees = source.RollDegrees;
            }
            if ((field & FreeDField.Position) != 0)
            {
                merged.XMeters = source.XMeters;
                merged.YMeters = source.YMeters;
                merged.ZMeters = source.ZMeters;
            }
            if ((field & FreeDField.Zoom) != 0)
            {
                merged.ZoomRaw = source.ZoomRaw;
            }
            if ((field & FreeDField.Focus) != 0)
            {
                merged.FocusRaw = source.FocusRaw;
            }
        }
    }
}
