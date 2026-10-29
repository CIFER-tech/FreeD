using Xunit;

namespace FreeD.Tests;

public class FreeDMergerTests
{
    private const int PoseStream = 0;
    private const int LensStream = 1;

    private static FreeDMerger PoseAndLens() => new FreeDMerger(new[]
    {
        FreeDFields.Rotation | FreeDFields.Position,
        FreeDFields.Zoom | FreeDFields.Focus,
    });

    private static FreeDPacket Pose(double pan, double xMeters) => new FreeDPacket { PanDegrees = pan, XMeters = xMeters };
    private static FreeDPacket Lens(uint zoom, uint focus) => new FreeDPacket { ZoomRaw = zoom, FocusRaw = focus };

    [Fact]
    public void Update_CombinesPoseAndLensIntoOnePacket()
    {
        var merger = PoseAndLens();
        merger.Put(PoseStream, Pose(pan: 30, xMeters: 1.5), receivedMs: 0);
        merger.Put(LensStream, Lens(zoom: 1000, focus: 2000), receivedMs: 0);

        var filled = merger.Update(nowMs: 0, out FreeDPacket merged);

        Assert.Equal(FreeDFields.Rotation | FreeDFields.Position | FreeDFields.Zoom | FreeDFields.Focus, filled);
        Assert.Equal(30, merged.PanDegrees);
        Assert.Equal(1.5, merged.XMeters);
        Assert.Equal(1000u, merged.ZoomRaw);
        Assert.Equal(2000u, merged.FocusRaw);
    }

    [Fact]
    public void Constructor_RejectsFieldClaimedByTwoStreams()
    {
        var overlapping = new[] { FreeDFields.Rotation | FreeDFields.Zoom, FreeDFields.Zoom };

        Assert.Throws<ArgumentException>(() => new FreeDMerger(overlapping));
    }

    [Fact]
    public void Update_KeepsLastValueAfterStreamStops()
    {
        var merger = PoseAndLens();
        merger.Put(PoseStream, Pose(pan: 10, xMeters: 0), receivedMs: 0);
        merger.Put(LensStream, Lens(zoom: 500, focus: 0), receivedMs: 0);
        merger.Update(nowMs: 0, out _);

        merger.Put(PoseStream, Pose(pan: 20, xMeters: 0), receivedMs: 1000); // レンズだけ止まる
        merger.Update(nowMs: 1000, out FreeDPacket merged);

        Assert.Equal(20, merged.PanDegrees);
        Assert.Equal(500u, merged.ZoomRaw);
        Assert.True(merger.IsReceiving(PoseStream, nowMs: 1000));
        Assert.False(merger.IsReceiving(LensStream, nowMs: 1000));
    }

    [Fact]
    public void Update_HoldsPacketUntilItsDelayHasPassed()
    {
        var merger = PoseAndLens();
        merger.SetDelay(PoseStream, 50);
        merger.Put(PoseStream, Pose(pan: 45, xMeters: 0), receivedMs: 100);

        var filledBeforeDelay = merger.Update(nowMs: 149, out _);
        var filledAfterDelay = merger.Update(nowMs: 150, out FreeDPacket merged);

        Assert.Equal(FreeDFields.None, filledBeforeDelay);
        Assert.Equal(FreeDFields.Rotation | FreeDFields.Position, filledAfterDelay);
        Assert.Equal(45, merged.PanDegrees);
    }

    [Fact]
    public void Update_ReportsOnlyFieldsThatHaveReceivedAValue()
    {
        var merger = PoseAndLens();
        merger.Put(LensStream, Lens(zoom: 1, focus: 2), receivedMs: 0);

        var filled = merger.Update(nowMs: 0, out _);

        Assert.Equal(FreeDFields.Zoom | FreeDFields.Focus, filled);
    }
}
