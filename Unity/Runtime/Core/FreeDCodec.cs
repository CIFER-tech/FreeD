using System;

namespace CIFER.tech.FreeD.Core
{
    /// <summary>
    /// FreeD D1 メッセージ (29 バイト) の符号化・復号。
    /// <code>
    /// [0]      0xD1
    /// [1]      カメラ ID
    /// [2..4]   パン   24bit 符号付き (2 の補数, ビッグエンディアン) 単位 1/32768 度
    /// [5..7]   チルト 同上
    /// [8..10]  ロール 同上
    /// [11..13] X      24bit 符号付き 単位 1/64 mm
    /// [14..16] Y      同上
    /// [17..19] Z      同上
    /// [20..22] ズーム 24bit 符号なし (生値)
    /// [23..25] フォーカス 24bit 符号なし (生値)
    /// [26..27] ユーザーデータ 16bit
    /// [28]     チェックサム = (0x40 - Σ[0..27]) mod 256
    /// </code>
    /// 公式の仕様書原本は未確認なので、受信機側の軸の向き・符号は実機で確認すること
    /// (受信機に合わせた符号の反転は、呼び出し側で行う)。
    /// </summary>
    public static class FreeDCodec
    {
        public const int PacketLength = 29;
        public const byte MessageTypeD1 = 0xD1;

        private const double AngleScale = 32768.0;          // 1/32768 度
        private const double PositionScale = 64.0 * 1000.0; // 1/64 mm → m あたり 64000
        private const int Signed24Min = -8388608;
        private const int Signed24Max = 8388607;
        /// <summary>ズーム・フォーカスの生の値の最大 (24bit 符号なし)。</summary>
        public const uint Unsigned24Max = 0xFFFFFF;

        /// <summary>29 バイトを書き込み、書き込んだバイト数を返す。値は 24bit の範囲に飽和させる。</summary>
        public static int Encode(in FreeDPacket p, Span<byte> destination)
        {
            if (destination.Length < PacketLength)
            {
                    throw new ArgumentException($"destination must be at least {PacketLength} bytes.", nameof(destination));
            }

            destination[0] = MessageTypeD1;
            destination[1] = p.CameraId;
            WriteSigned24(destination, 2, ToRaw(p.PanDegrees, AngleScale));
            WriteSigned24(destination, 5, ToRaw(p.TiltDegrees, AngleScale));
            WriteSigned24(destination, 8, ToRaw(p.RollDegrees, AngleScale));
            WriteSigned24(destination, 11, ToRaw(p.XMeters, PositionScale));
            WriteSigned24(destination, 14, ToRaw(p.YMeters, PositionScale));
            WriteSigned24(destination, 17, ToRaw(p.ZMeters, PositionScale));
            WriteUnsigned24(destination, 20, p.ZoomRaw);
            WriteUnsigned24(destination, 23, p.FocusRaw);
            destination[26] = (byte)(p.UserData >> 8);
            destination[27] = (byte)p.UserData;
            destination[28] = Checksum(destination.Slice(0, PacketLength - 1));
            return PacketLength;
        }

        /// <summary>復号する。長さ・種別・チェックサムが不正なら false。</summary>
        public static bool TryDecode(ReadOnlySpan<byte> source, out FreeDPacket packet)
        {
            packet = default;
            if (source.Length < PacketLength)
            {
                return false;
            }
            if (source[0] != MessageTypeD1)
            {
                return false;
            }
            if (source[PacketLength - 1] != Checksum(source.Slice(0, PacketLength - 1)))
            {
                return false;
            }

            packet.CameraId = source[1];
            packet.PanDegrees = ReadSigned24(source, 2) / AngleScale;
            packet.TiltDegrees = ReadSigned24(source, 5) / AngleScale;
            packet.RollDegrees = ReadSigned24(source, 8) / AngleScale;
            packet.XMeters = ReadSigned24(source, 11) / PositionScale;
            packet.YMeters = ReadSigned24(source, 14) / PositionScale;
            packet.ZMeters = ReadSigned24(source, 17) / PositionScale;
            packet.ZoomRaw = ReadUnsigned24(source, 20);
            packet.FocusRaw = ReadUnsigned24(source, 23);
            packet.UserData = (ushort)((source[26] << 8) | source[27]);
            return true;
        }

        /// <summary>先頭 28 バイトからチェックサムを求める: (0x40 − 総和) mod 256。</summary>
        public static byte Checksum(ReadOnlySpan<byte> bytesBeforeChecksum)
        {
            var sum = 0;
            for (var i = 0; i < bytesBeforeChecksum.Length; i++)
            {
                sum += bytesBeforeChecksum[i];
            }
            return (byte)((0x40 - sum) & 0xFF);
        }

        private static int ToRaw(double value, double scale)
        {
            var raw = Math.Round(value * scale, MidpointRounding.AwayFromZero);
            if (double.IsNaN(raw))
            {
                return 0;
            }
            if (raw < Signed24Min)
            {
                return Signed24Min;
            }
            if (raw > Signed24Max)
            {
                return Signed24Max;
            }
            return (int)raw;
        }

        private static void WriteSigned24(Span<byte> d, int offset, int value)
        {
            d[offset] = (byte)((value >> 16) & 0xFF);
            d[offset + 1] = (byte)((value >> 8) & 0xFF);
            d[offset + 2] = (byte)(value & 0xFF);
        }

        private static void WriteUnsigned24(Span<byte> d, int offset, uint value)
        {
            if (value > Unsigned24Max)
            {
                value = Unsigned24Max;
            }
            d[offset] = (byte)((value >> 16) & 0xFF);
            d[offset + 1] = (byte)((value >> 8) & 0xFF);
            d[offset + 2] = (byte)(value & 0xFF);
        }

        private static int ReadSigned24(ReadOnlySpan<byte> s, int offset)
        {
            var v = (s[offset] << 16) | (s[offset + 1] << 8) | s[offset + 2];
            return v >= 0x800000 ? v - 0x1000000 : v;
        }

        private static uint ReadUnsigned24(ReadOnlySpan<byte> s, int offset)
            => (uint)((s[offset] << 16) | (s[offset + 1] << 8) | s[offset + 2]);
    }
}
