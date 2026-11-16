#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using CIFER.tech.RingBuffer;

namespace CIFER.tech.FreeD.Core
{
    public sealed class FreeDReceiver : IDisposable
    {
        // 送信が 60 fps より速くても足りるように、最低限の数と、遅延 1 秒あたりの数 (240 fps 相当) を持つ。
        private const int MinCapacity = 256;
        private const int CapacityPerDelaySec = 240;

        // FreeD D1 は 29 バイト。余裕を見た受信バッファの大きさ。
        private const int DatagramBufferSize = 64;

        private float delaySec;
        // 受信スレッドで起きた例外。スレッドの外へは投げられないので、TryTakeNext で投げる。
        private volatile Exception? receiveException;
        private RingBuffer<FreeDReceivedPacket>? buffer;
        private Socket? socket;

        public void Dispose()
        {
            Close();
        }

        public void Open(int port, float delaySec)
        {
            Close();

            this.delaySec = delaySec;
            receiveException = null;
            var receivedPackets = new RingBuffer<FreeDReceivedPacket>(MinCapacity + (int)(delaySec * CapacityPerDelaySec));
            buffer = receivedPackets;

            var openedSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            try
            {
                openedSocket.Bind(new IPEndPoint(IPAddress.Any, port));
            }
            catch (Exception e)
            {
                openedSocket.Dispose();
                throw new InvalidOperationException("FreeD: ポート " + port + " を開けない: " + e.Message, e);
            }

            socket = openedSocket;

            // ReceiveAsync は 1 回ごとに Task・UdpReceiveResult・byte[] を作るので、
            // 専用スレッドで同じバッファに受信し続けて、受信経路の確保をなくす。
            var receiveThread = new Thread(() => ReceiveLoop(openedSocket, receivedPackets))
            {
                IsBackground = true,
                Name = "FreeDReceiver:" + port
            };
            receiveThread.Start();
        }

        public void Close()
        {
            // Close すると、受信スレッドの Receive が例外で抜けて、スレッドが終わる。
            socket?.Close();
            socket = null;
        }

        private void ReceiveLoop(Socket receivingSocket, RingBuffer<FreeDReceivedPacket> receivedPackets)
        {
            var datagram = new byte[DatagramBufferSize];

            while (true)
            {
                int length;
                try
                {
                    length = receivingSocket.Receive(datagram);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (SocketException e)
                {
                    // バッファより大きなデータグラムは FreeD ではないので、捨てて受信を続ける。
                    if (e.SocketErrorCode == SocketError.MessageSize)
                    {
                        continue;
                    }

                    // Close で中断されたときも SocketException になる。そのときは正常な終了。
                    if (e.SocketErrorCode != SocketError.Interrupted && e.SocketErrorCode != SocketError.OperationAborted)
                    {
                        receiveException = new InvalidOperationException("FreeD: 受信に失敗して止まった: " + e.Message, e);
                    }

                    return;
                }

                if (FreeDCodec.TryDecode(new ReadOnlySpan<byte>(datagram, 0, length), out var packet))
                {
                    receivedPackets.Enqueue(new FreeDReceivedPacket
                    {
                        receivedMs = FreeDClock.NowMs,
                        packet = packet
                    });
                }
            }
        }

        /// <exception cref="InvalidOperationException">受信が途中で失敗して止まったとき。1 回だけ投げる。</exception>
        public bool TryTakeNext(out FreeDReceivedPacket received)
        {
            received = default;

            var failure = receiveException;
            if (failure != null)
            {
                receiveException = null;
                throw failure;
            }

            if (buffer == null)
            {
                return false;
            }

            var delayedNowMs = FreeDClock.NowMs - delaySec * 1000d;
            if (!buffer.TryPeek(out var peeked) || peeked.receivedMs > delayedNowMs)
            {
                return false;
            }

            return buffer.TryDequeue(out received);
        }
    }
}
