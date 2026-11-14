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

        var received = new FreeDPacket();
        Assert.Equal(1u, TakeNextWithin(receiver, received).ZoomRaw);
        Assert.Equal(2u, TakeNextWithin(receiver, received).ZoomRaw);
        Assert.Equal(3u, TakeNextWithin(receiver, received).ZoomRaw);
        Assert.False(receiver.TryTakeNext(received), "3 つ取り出したあとは、空のはず");
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
        WaitUntilQuiet(receiver);

        var received = new FreeDPacket();
        uint last = 0;
        var takenCount = 0;
        while (receiver.TryTakeNext(received))
        {
            Assert.True(received.ZoomRaw > last, $"古い順に取り出せていない: {last} のあとに {received.ZoomRaw}");
            last = received.ZoomRaw;
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

        var received = new FreeDPacket();
        Assert.Equal(7u, TakeNextWithin(receiver, received).ZoomRaw);
    }

    private static FreeDPacket TakeNextWithin(FreeDReceiver receiver, FreeDPacket received)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!receiver.TryTakeNext(received))
        {
            Assert.True(stopwatch.ElapsedMilliseconds < 3000, "3 秒待っても、パケットが届かなかった");
            Thread.Sleep(1);
        }

        return received;
    }

    // 受信スレッドが、送ったパケットをすべて処理し終えるまで待つ (しばらく届かなくなるまで)。
    private static void WaitUntilQuiet(FreeDReceiver receiver)
    {
        Thread.Sleep(500);
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
