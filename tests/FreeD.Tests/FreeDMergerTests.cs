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

        var merged = FreeDMerger.Merge(pose, FreeDField.Rotation | FreeDField.Position, lens, FreeDField.Zoom | FreeDField.Focus);

        Assert.Equal(30, merged.PanDegrees);
        Assert.Equal(1.5, merged.XMeters);
        Assert.Equal(1000u, merged.ZoomRaw);
        Assert.Equal(2000u, merged.FocusRaw);
    }

    [Fact]
    public void Merge_DoesNotCopyCameraIdAndUserData()
    {
        var pose = new FreeDPacket { PanDegrees = 30, TiltDegrees = 20, RollDegrees = 10, CameraId = 2, UserData = 3 };

        var merged = FreeDMerger.Merge(pose, FreeDField.Rotation, new FreeDPacket(), FreeDField.None);

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

        var merged = FreeDMerger.Merge(a, FreeDField.None, a, FreeDField.None);

        Assert.Equal(0u, merged.ZoomRaw);
        Assert.Equal(0, merged.XMeters);
    }

    [Fact]
    public void Merge_OverlappingField_UsesSecondPacket()
    {
        var a = new FreeDPacket { ZoomRaw = 1 };
        var b = new FreeDPacket { ZoomRaw = 2 };

        var merged = FreeDMerger.Merge(a, FreeDField.Zoom, b, FreeDField.Zoom);

        Assert.Equal(2u, merged.ZoomRaw);
    }
}
