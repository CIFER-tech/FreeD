namespace CIFER.tech.FreeD.Core
{
    /// <summary>
    /// FreeD D1 メッセージの内容。角度は度、位置はメートル、ズーム・フォーカスは 24bit の生値。
    /// 項目ごとに別のストリームから受け取った値は、<see cref="FreeDMerger"/> で合成して入れる。
    /// 位置は右手系で、X=右、Y=前、Z=上。
    /// 角度の正の向きは、送信する機器の仕様による。
    /// </summary>
    public struct FreeDPacket
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
    }
}
