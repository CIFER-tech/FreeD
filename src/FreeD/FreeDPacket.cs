namespace CIFER.tech.FreeD.Core
{
    /// <summary>位置は右手系で、X=右、Y=前、Z=上。角度の正の向きは、送信する機器の仕様による。</summary>
    public struct FreeDPacket
    {
        public byte CameraId;

        public double PanDegrees;

        public double TiltDegrees;

        public double RollDegrees;

        public double XMeters;

        public double YMeters;

        public double ZMeters;

        /// <summary>送信する機器ごとの値。焦点距離への換算式は仕様にない。</summary>
        public uint ZoomRaw;

        /// <summary>送信する機器ごとの値。ピント距離への換算式は仕様にない。</summary>
        public uint FocusRaw;

        public ushort UserData;
    }
}
