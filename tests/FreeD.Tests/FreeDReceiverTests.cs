using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using CIFER.tech.FreeD.Core;
using Xunit;

namespace CIFER.tech.FreeD.Tests;

public class FreeDReceiverTests
{
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
    public void TryTakeNext_WhenMoreThanCapacityArrive_KeepsTheNewestAndNeverFails()
    {
        using var receiver = new FreeDReceiver();
        var port = FindFreePort();
        receiver.Open(port, 0f);

        // 容量 (最低 256) を超える数を、取り出さずに送る。古いものは捨てられる。
        const int sentCount = 1000;
        SendPackets(port, firstZoom: 1, count: sentCount);

        uint last = 0;
        var takenCount = 0;
        var stopwatch = Stopwatch.StartNew();
        while (last < sentCount)
        {
            Assert.True(stopwatch.ElapsedMilliseconds < 5000, $"5 秒待っても、最後のパケットが届かなかった (最後に取り出した値: {last})");
            if (!receiver.TryTakeNext(out var received))
            {
                Thread.Sleep(1);
                continue;
            }

            Assert.True(received.packet.ZoomRaw > last, $"古い順に取り出せていない: {last} のあとに {received.packet.ZoomRaw}");
            last = received.packet.ZoomRaw;
            takenCount++;
        }

        Assert.Equal((uint)sentCount, last);
        Assert.InRange(takenCount, 1, sentCount);
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
