using CIFER.tech.FreeD.Core;
using Xunit;

namespace CIFER.tech.FreeD.Tests;

public class FreeDCodecTests
{
    // 手計算で求めた期待値 (実装とは独立に作ったバイト列)。
    //   pan=90° → 90*32768=2949120=0x2D0000          tilt=-45° → -1474560 → 0xE98000     roll=0
    //   X=1.0 m → 64000=0x00FA00   Y=-0.5 m → -32000 → 0xFF8300   Z=2.0 m → 128000=0x01F400
    //   checksum = (0x40 - Σ) mod 256 = 0x51   (Σ = 2287 = 0x8EF → 下位 0xEF)
    private static readonly byte[] KnownPacket =
    {
        0xD1, 0x01,
        0x2D, 0x00, 0x00,
        0xE9, 0x80, 0x00,
        0x00, 0x00, 0x00,
        0x00, 0xFA, 0x00,
        0xFF, 0x83, 0x00,
        0x01, 0xF4, 0x00,
        0x01, 0x23, 0x45,
        0xAB, 0xCD, 0xEF,
        0x12, 0x34,
        0x51,
    };

    private static FreeDPacket KnownValues() => new FreeDPacket
    {
        CameraId = 1,
        PanDegrees = 90.0,
        TiltDegrees = -45.0,
        RollDegrees = 0.0,
        XMeters = 1.0,
        YMeters = -0.5,
        ZMeters = 2.0,
        ZoomRaw = 0x012345,
        FocusRaw = 0xABCDEF,
        UserData = 0x1234,
    };

    [Fact]
    public void Encode_ProducesTheHandComputedBytes()
    {
        var buffer = new byte[FreeDCodec.PacketLength];
        int n = FreeDCodec.Encode(KnownValues(), buffer);

        Assert.Equal(29, n);
        Assert.Equal(KnownPacket, buffer);
    }

    [Fact]
    public void Decode_ReadsTheHandComputedBytes()
    {
        Assert.True(FreeDCodec.TryDecode(KnownPacket, out FreeDPacket p));
        Assert.Equal((byte)1, p.CameraId);
        Approx(90.0, p.PanDegrees, 1e-9);
        Approx(-45.0, p.TiltDegrees, 1e-9);
        Approx(0.0, p.RollDegrees, 1e-9);
        Approx(1.0, p.XMeters, 1e-9);
        Approx(-0.5, p.YMeters, 1e-9);
        Approx(2.0, p.ZMeters, 1e-9);
        Assert.Equal(0x012345u, p.ZoomRaw);
        Assert.Equal(0xABCDEFu, p.FocusRaw);
        Assert.Equal((ushort)0x1234, p.UserData);
    }

    [Fact]
    public void RoundTrip_StaysWithinTheWireResolution()
    {
        var rng = new Random(2024);
        var buffer = new byte[FreeDCodec.PacketLength];
        for (int i = 0; i < 1000; i++)
        {
            var src = new FreeDPacket
            {
                CameraId = (byte)rng.Next(0, 256),
                PanDegrees = (rng.NextDouble() * 2 - 1) * 180,
                TiltDegrees = (rng.NextDouble() * 2 - 1) * 90,
                RollDegrees = (rng.NextDouble() * 2 - 1) * 180,
                XMeters = (rng.NextDouble() * 2 - 1) * 100,
                YMeters = (rng.NextDouble() * 2 - 1) * 100,
                ZMeters = (rng.NextDouble() * 2 - 1) * 100,
                ZoomRaw = (uint)rng.Next(0, 0x1000000),
                FocusRaw = (uint)rng.Next(0, 0x1000000),
                UserData = (ushort)rng.Next(0, 0x10000),
            };
            FreeDCodec.Encode(src, buffer);
            Assert.True(FreeDCodec.TryDecode(buffer, out FreeDPacket dst));

            const double angleRes = 0.5 / 32768.0;   // 半 LSB
            const double posRes = 0.5 / 64000.0;
            Assert.Equal(src.CameraId, dst.CameraId);
            Approx(src.PanDegrees, dst.PanDegrees, angleRes + 1e-12);
            Approx(src.TiltDegrees, dst.TiltDegrees, angleRes + 1e-12);
            Approx(src.RollDegrees, dst.RollDegrees, angleRes + 1e-12);
            Approx(src.XMeters, dst.XMeters, posRes + 1e-12);
            Approx(src.YMeters, dst.YMeters, posRes + 1e-12);
            Approx(src.ZMeters, dst.ZMeters, posRes + 1e-12);
            Assert.Equal(src.ZoomRaw, dst.ZoomRaw);
            Assert.Equal(src.FocusRaw, dst.FocusRaw);
            Assert.Equal(src.UserData, dst.UserData);
        }
    }

    [Fact]
    public void Decode_RejectsBadChecksumTypeAndLength()
    {
        var bad = (byte[])KnownPacket.Clone();
        bad[28] ^= 0x01;
        Assert.False(FreeDCodec.TryDecode(bad, out _));

        var wrongType = (byte[])KnownPacket.Clone();
        wrongType[0] = 0xD2;
        wrongType[28] = FreeDCodec.Checksum(wrongType.AsSpan(0, 28));
        Assert.False(FreeDCodec.TryDecode(wrongType, out _));

        Assert.False(FreeDCodec.TryDecode(KnownPacket.AsSpan(0, 28), out _));
        Assert.False(FreeDCodec.TryDecode(ReadOnlySpan<byte>.Empty, out _));
    }

    [Fact]
    public void Encode_SaturatesOutOfRangeValues_InsteadOfWrapping()
    {
        var buffer = new byte[FreeDCodec.PacketLength];
        FreeDCodec.Encode(new FreeDPacket { XMeters = 1000.0, YMeters = -1000.0, ZoomRaw = 0xFFFFFFFF }, buffer);
        Assert.True(FreeDCodec.TryDecode(buffer, out FreeDPacket p));
        Approx(8388607.0 / 64000.0, p.XMeters, 1e-9);
        Approx(-8388608.0 / 64000.0, p.YMeters, 1e-9);
        Assert.Equal(0xFFFFFFu, p.ZoomRaw);
    }

    [Fact]
    public void Encode_Throws_WhenBufferIsTooSmall()
    {
        Assert.Throws<ArgumentException>(() => FreeDCodec.Encode(new FreeDPacket(), new byte[10]));
    }

    private static void Approx(double expected, double actual, double tolerance)
        => Assert.True(Math.Abs(expected - actual) <= tolerance, $"expected {expected}, actual {actual}, tolerance {tolerance}");
}
