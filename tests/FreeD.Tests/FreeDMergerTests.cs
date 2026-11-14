using CIFER.tech.FreeD.Core;
using Xunit;

namespace CIFER.tech.FreeD.Tests;

public class FreeDMergerTests
{
    [Fact]
    public void Merge_TakesEachPacketsOwnFields()
    {
        var pose = new FreeDPacket { PanDegrees = 30, XMeters = 1.5, ZoomRaw = 111 };
        var lens = new FreeDPacket { PanDegrees = 99, ZoomRaw = 1000, FocusRaw = 2000 };

        var merged = new FreeDPacket();
        FreeDMerger.Merge(merged, pose, FreeDField.Rotation | FreeDField.Position, lens, FreeDField.Zoom | FreeDField.Focus);

        Assert.Equal(30, merged.PanDegrees);
        Assert.Equal(1.5, merged.XMeters);
        Assert.Equal(1000u, merged.ZoomRaw);
        Assert.Equal(2000u, merged.FocusRaw);
    }

    [Fact]
    public void Merge_DoesNotCopyCameraIdAndUserData()
    {
        var pose = new FreeDPacket { PanDegrees = 30, TiltDegrees = 20, RollDegrees = 10, CameraId = 2, UserData = 3 };

        var merged = new FreeDPacket();
        FreeDMerger.Merge(merged, pose, FreeDField.Rotation, new FreeDPacket(), FreeDField.None);

        Assert.Equal(30, merged.PanDegrees);
        Assert.Equal(20, merged.TiltDegrees);
        Assert.Equal(10, merged.RollDegrees);
        Assert.Equal(0, merged.CameraId);
        Assert.Equal(0, merged.UserData);
    }

    [Fact]
    public void Merge_FieldsInNeitherPacketStayDefault()
    {
        var a = new FreeDPacket { ZoomRaw = 5, XMeters = 5 };

        var merged = new FreeDPacket();
        FreeDMerger.Merge(merged, a, FreeDField.None, a, FreeDField.None);

        Assert.Equal(0u, merged.ZoomRaw);
        Assert.Equal(0, merged.XMeters);
    }

    [Fact]
    public void Merge_OverlappingField_UsesSecondPacket()
    {
        var a = new FreeDPacket { ZoomRaw = 1 };
        var b = new FreeDPacket { ZoomRaw = 2 };

        var merged = new FreeDPacket();
        FreeDMerger.Merge(merged, a, FreeDField.Zoom, b, FreeDField.Zoom);

        Assert.Equal(2u, merged.ZoomRaw);
    }
    [Fact]
    public void CopyFields_ChangesOnlyTheGivenFields()
    {
        var destination = new FreeDPacket { PanDegrees = 1, XMeters = 2, ZoomRaw = 3, CameraId = 9 };
        var source = new FreeDPacket { PanDegrees = 10, XMeters = 20, ZoomRaw = 30, CameraId = 5 };

        FreeDMerger.CopyFields(destination, source, FreeDField.Position);

        Assert.Equal(1, destination.PanDegrees);
        Assert.Equal(20, destination.XMeters);
        Assert.Equal(3u, destination.ZoomRaw);
        Assert.Equal(9, destination.CameraId);
    }

    [Fact]
    public void Packet_CopyFromAndClear()
    {
        var source = new FreeDPacket { CameraId = 4, PanDegrees = 1, XMeters = 2, ZoomRaw = 3, FocusRaw = 4, UserData = 5 };
        var copy = new FreeDPacket();

        copy.CopyFrom(source);
        Assert.Equal(4, copy.CameraId);
        Assert.Equal(1, copy.PanDegrees);
        Assert.Equal(2, copy.XMeters);
        Assert.Equal(3u, copy.ZoomRaw);
        Assert.Equal(4u, copy.FocusRaw);
        Assert.Equal(5, copy.UserData);

        copy.Clear();
        Assert.Equal(0, copy.CameraId);
        Assert.Equal(0, copy.PanDegrees);
        Assert.Equal(0u, copy.ZoomRaw);
        Assert.Equal(4, source.CameraId);
    }
}
