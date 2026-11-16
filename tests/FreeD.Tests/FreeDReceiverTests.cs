using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using CIFER.tech.FreeD.Core;
using Xunit;

namespace CIFER.tech.FreeD.Tests;

public class FreeDReceiverTests
{
    private const int ReceiveSettleMs = 500;

    [Fact]
    public void TryTakeNext_ReturnsSentPacketsOldestFirst()
    {
        using var receiver = new FreeDReceiver();
        var port = FindFreePort();
        receiver.Open(port, 0f);

        SendPackets(port, firstZoom: 1, count: 3);

        Assert.Equal(1u, TakeNextWithin(receiver).packet.ZoomRaw);
        Assert.Equal(2u, TakeNextWithin(receiver).packet.ZoomRaw);
        Assert.Equal(3u, TakeNextWithin(receiver).packet.ZoomRaw);
        Assert.False(receiver.TryTakeNext(out _), "3 つ取り出したあとは、空のはず");
    }

    [Fact]
    public void TryTakeNext_WhenMoreThanCapacityArrive_KeepsOnlyTheNewestCapacity()
    {
        using var receiver = new FreeDReceiver();
        var port = FindFreePort();
        receiver.Open(port, 0f);

        // 遅延 0 のときの容量は、FreeDReceiver の最低の数 (256)。
        const int capacity = 256;
        const int sentCount = 1000;
        SendPackets(port, firstZoom: 1, count: sentCount);

        // 取り出しが受信の最中に始まると、あふれずに全部取れてしまうので、受信が終わるのを待ってから取り出す。
        Thread.Sleep(ReceiveSettleMs);

        var takenZooms = new List<uint>();
        while (receiver.TryTakeNext(out var received))
        {
            takenZooms.Add(received.packet.ZoomRaw);
        }

        var expectedZooms = Enumerable.Range(sentCount - capacity + 1, capacity).Select(zoom => (uint)zoom).ToList();
        Assert.Equal(expectedZooms, takenZooms);
    }

    [Fact]
    public void TryTakeNext_IgnoresDatagramsThatAreNotFreeD()
    {
        using var receiver = new FreeDReceiver();
        var port = FindFreePort();
        receiver.Open(port, 0f);

        using (var client = new UdpClient())
        {
            client.Send(new byte[] { 1, 2, 3 }, 3, new IPEndPoint(IPAddress.Loopback, port));
            client.Send(new byte[200], 200, new IPEndPoint(IPAddress.Loopback, port));
        }

        SendPackets(port, firstZoom: 7, count: 1);

        Assert.Equal(7u, TakeNextWithin(receiver).packet.ZoomRaw);
    }

    [Fact]
    public void Properties_WhenOpen_ThrowInvalidOperationException()
    {
        using var receiver = new FreeDReceiver { Port = FindFreePort() };
        receiver.Open();

        Assert.Throws<InvalidOperationException>(() => receiver.Port = 1);
        Assert.Throws<InvalidOperationException>(() => receiver.DelaySec = 1f);
        Assert.Throws<InvalidOperationException>(() => receiver.ClaimedFields = FreeDField.Zoom);
    }

    [Fact]
    public void Properties_AfterClose_CanBeChanged()
    {
        using var receiver = new FreeDReceiver { Port = FindFreePort() };
        receiver.Open();
        receiver.Close();

        receiver.DelaySec = 1.5f;
        receiver.ClaimedFields = FreeDField.Zoom;

        Assert.Equal(1.5f, receiver.DelaySec);
        Assert.Equal(FreeDField.Zoom, receiver.ClaimedFields);
    }

    [Fact]
    public void Open_UsesDelaySecSetBeforeOpen()
    {
        var port = FindFreePort();
        using var receiver = new FreeDReceiver { Port = port, DelaySec = 0.5f };
        receiver.Open();

        SendPackets(port, firstZoom: 1, count: 1);
        Thread.Sleep(100);

        Assert.False(receiver.TryTakeNext(out _), "遅延 0.5 秒のうちは、取り出せないはず");
        Assert.Equal(1u, TakeNextWithin(receiver).packet.ZoomRaw);
    }

    [Fact]
    public void Open_WhenAlreadyOpen_ReopensAndKeepsReceiving()
    {
        var port = FindFreePort();
        using var receiver = new FreeDReceiver { Port = port };
        receiver.Open();

        receiver.Open();
        SendPackets(port, firstZoom: 5, count: 1);

        Assert.Equal(5u, TakeNextWithin(receiver).packet.ZoomRaw);
    }

    [Fact]
    public void Open_WithPortAndDelaySec_SetsProperties()
    {
        var port = FindFreePort();
        using var receiver = new FreeDReceiver();

        receiver.Open(port, 0f);
        SendPackets(port, firstZoom: 3, count: 1);

        Assert.Equal(port, receiver.Port);
        Assert.Equal(3u, TakeNextWithin(receiver).packet.ZoomRaw);
    }

    [Fact]
    public void Receiver_CanBeUsedThroughInterface()
    {
        var port = FindFreePort();
        using var concrete = new FreeDReceiver { Port = port };
        IFreeDReceiver receiver = concrete;
        receiver.ClaimedFields = FreeDField.Zoom;

        receiver.Open();
        SendPackets(port, firstZoom: 9, count: 1);

        Assert.Equal(FreeDField.Zoom, receiver.ClaimedFields);
        Assert.Equal(9u, TakeNextWithin(receiver).packet.ZoomRaw);
        receiver.Close();
    }

    private static FreeDReceivedPacket TakeNextWithin(IFreeDReceiver receiver)
    {
        var stopwatch = Stopwatch.StartNew();
        FreeDReceivedPacket received;
        while (!receiver.TryTakeNext(out received))
        {
            Assert.True(stopwatch.ElapsedMilliseconds < 3000, "3 秒待っても、パケットが届かなかった");
            Thread.Sleep(1);
        }

        return received;
    }

    private static void SendPackets(int port, uint firstZoom, int count)
    {
        using var client = new UdpClient();
        var buffer = new byte[FreeDCodec.PacketLength];
        var packet = new FreeDPacket { CameraId = 1 };
        for (var i = 0; i < count; i++)
        {
            packet.ZoomRaw = firstZoom + (uint)i;
            FreeDCodec.Encode(packet, buffer);
            client.Send(buffer, buffer.Length, new IPEndPoint(IPAddress.Loopback, port));
        }
    }

    private static int FindFreePort()
    {
        using var client = new UdpClient(0);
        return ((IPEndPoint)client.Client.LocalEndPoint!).Port;
    }
}
