namespace CIFER.tech.FreeD.Core
{
    /// <summary>状態は持たない。遅延や途切れの扱いは、呼び出し側で行う。</summary>
    public static class FreeDMerger
    {
        /// <summary>どちらにも含まれない項目は既定値になる。両方に含まれる項目は <paramref name="packetB"/> の値を使う。</summary>
        public static FreeDPacket Merge(in FreeDPacket packetA, FreeDField fieldsA, in FreeDPacket packetB, FreeDField fieldsB)
        {
            var merged = new FreeDPacket();
            CopyFields(ref merged, packetA, fieldsA);
            CopyFields(ref merged, packetB, fieldsB);
            return merged;
        }

        private static void CopyFields(ref FreeDPacket merged, in FreeDPacket source, FreeDField copiedFields)
        {
            if ((copiedFields & FreeDField.Rotation) != 0)
            {
                merged.PanDegrees = source.PanDegrees;
                merged.TiltDegrees = source.TiltDegrees;
                merged.RollDegrees = source.RollDegrees;
            }
            if ((copiedFields & FreeDField.Position) != 0)
            {
                merged.XMeters = source.XMeters;
                merged.YMeters = source.YMeters;
                merged.ZMeters = source.ZMeters;
            }
            if ((copiedFields & FreeDField.Zoom) != 0)
            {
                merged.ZoomRaw = source.ZoomRaw;
            }
            if ((copiedFields & FreeDField.Focus) != 0)
            {
                merged.FocusRaw = source.FocusRaw;
            }
        }
    }
}
