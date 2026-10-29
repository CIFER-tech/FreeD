namespace FreeD
{
    /// <summary>
    /// FreeD D1 メッセージの内容。角度は度、位置はメートル、ズーム・フォーカスは 24bit の生値。
    /// ズーム・フォーカスは別系統のレンズエンコーダ側の値をそのまま通すための領域で、
    /// retroloc は扱わない (既定 0)。別のストリームの値は <see cref="FreeDMerger"/> で合成して入れる。
    /// </summary>
    public struct FreeDPacket
    {
        public byte CameraId;
        public double PanDegrees;
        public double TiltDegrees;
        public double RollDegrees;
        public double XMeters;
        public double YMeters;
        public double ZMeters;
        public uint ZoomRaw;
        public uint FocusRaw;
        public ushort UserData;
    }
}
