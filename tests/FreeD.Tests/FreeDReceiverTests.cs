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

    private static FreeDReceivedPacket TakeNextWithin(FreeDReceiver receiver)
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
