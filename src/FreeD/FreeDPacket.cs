namespace CIFER.tech.FreeD.Core
{
    /// <summary>
    /// FreeD D1 メッセージの内容。角度は度、位置はメートル、ズーム・フォーカスは 24bit の生値。
    /// 項目ごとに別のストリームから受け取った値は、<see cref="MergeFrom"/> や <see cref="CopyFieldsFrom"/> で合成して入れる。
    /// 位置は右手系で、X=右、Y=前、Z=上。
    /// 角度の正の向きは、送信する機器の仕様による。
    /// </summary>
    public sealed class FreeDPacket
    {
        public byte CameraId;

        /// <summary>水平方向の回転。</summary>
        public double PanDegrees;

        /// <summary>上下方向の回転。</summary>
        public double TiltDegrees;

        /// <summary>光軸まわりの回転。</summary>
        public double RollDegrees;

        /// <summary>右が正。</summary>
        public double XMeters;

        /// <summary>前が正。</summary>
        public double YMeters;

        /// <summary>上が正。</summary>
        public double ZMeters;

        /// <summary>送信する機器ごとの値。焦点距離への換算式は仕様にない。</summary>
        public uint ZoomRaw;

        /// <summary>送信する機器ごとの値。ピント距離への換算式は仕様にない。</summary>
        public uint FocusRaw;

        public ushort UserData;

        /// <summary>すべての項目を、既定値 (0) に戻す。</summary>
        public void Clear()
        {
            CameraId = 0;
            PanDegrees = 0;
            TiltDegrees = 0;
            RollDegrees = 0;
            XMeters = 0;
            YMeters = 0;
            ZMeters = 0;
            ZoomRaw = 0;
            FocusRaw = 0;
            UserData = 0;
        }

        /// <summary>
        /// <paramref name="fieldA"/> の項目は <paramref name="packetA"/> から、<paramref name="fieldB"/> の項目は <paramref name="packetB"/> から取って、
        /// このオブジェクトに書き込む。確保はしない。
        /// どちらにも含まれない項目は既定値になる。両方に含まれる項目は <paramref name="packetB"/> の値を使う。
        /// このオブジェクトは、<paramref name="packetA"/> と <paramref name="packetB"/> とは別のオブジェクトにする。
        /// </summary>
        public void MergeFrom(FreeDPacket packetA, FreeDField fieldA, FreeDPacket packetB, FreeDField fieldB)
        {
            Clear();
            CopyFieldsFrom(packetA, fieldA);
            CopyFieldsFrom(packetB, fieldB);
        }

        /// <summary>
        /// <paramref name="field"/> の項目だけを、<paramref name="source"/> からこのオブジェクトに写す。残りの項目は、そのまま。
        /// カメラ ID とユーザーデータは写さない。確保はしない。
        /// </summary>
        public void CopyFieldsFrom(FreeDPacket source, FreeDField field)
        {
            if ((field & FreeDField.Rotation) != 0)
            {
                PanDegrees = source.PanDegrees;
                TiltDegrees = source.TiltDegrees;
                RollDegrees = source.RollDegrees;
            }

            if ((field & FreeDField.Position) != 0)
            {
                XMeters = source.XMeters;
                YMeters = source.YMeters;
                ZMeters = source.ZMeters;
            }

            if ((field & FreeDField.Zoom) != 0)
            {
                ZoomRaw = source.ZoomRaw;
            }

            if ((field & FreeDField.Focus) != 0)
            {
                FocusRaw = source.FocusRaw;
            }
        }

        /// <summary><paramref name="source"/> の項目を、すべてこのオブジェクトに写す。</summary>
        public void CopyFrom(FreeDPacket source)
        {
            CameraId = source.CameraId;
            PanDegrees = source.PanDegrees;
            TiltDegrees = source.TiltDegrees;
            RollDegrees = source.RollDegrees;
            XMeters = source.XMeters;
            YMeters = source.YMeters;
            ZMeters = source.ZMeters;
            ZoomRaw = source.ZoomRaw;
            FocusRaw = source.FocusRaw;
            UserData = source.UserData;
        }
    }
}
