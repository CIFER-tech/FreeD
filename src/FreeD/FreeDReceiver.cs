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
        // この数だけ FreeDReceivedPacket を最初に作り、使い回す。
        private const int MinCapacity = 256;
        private const int CapacityPerDelaySec = 240;

        // FreeD D1 は 29 バイト。余裕を見た受信バッファの大きさ。
        private const int DatagramBufferSize = 64;

        private float delaySec;
        // 受信スレッドで起きた例外。スレッドの外へは投げられないので、TryTakeNext で投げる。
        private volatile Exception? receiveException;

        // 受信して、まだ取り出していないパケット (古い順) と、空いているパケット。
        // 2 つのキューの容量は、作ったパケットの数と同じなので、入れるときに古いものが捨てられることはない。
        private RingBuffer<FreeDReceivedPacket>? filledPackets;
        private RingBuffer<FreeDReceivedPacket>? freePackets;
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
            var packetCount = MinCapacity + (int)(delaySec * CapacityPerDelaySec);
            var filled = new RingBuffer<FreeDReceivedPacket>(packetCount);
            var free = new RingBuffer<FreeDReceivedPacket>(packetCount);
            for (var i = 0; i < packetCount; i++)
            {
                free.Enqueue(new FreeDReceivedPacket());
            }

            filledPackets = filled;
            freePackets = free;

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
            var receiveThread = new Thread(() => ReceiveLoop(openedSocket, filled, free))
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

        private void ReceiveLoop(Socket receivingSocket, RingBuffer<FreeDReceivedPacket> filled, RingBuffer<FreeDReceivedPacket> free)
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

                // 空いているパケットがなければ、取り出されていないいちばん古いものを使い回す (古いものを捨てる)。
                if (!free.TryDequeue(out var slot) && !filled.TryDequeue(out slot))
                {
                    continue;
                }

                if (FreeDCodec.TryDecode(new ReadOnlySpan<byte>(datagram, 0, length), slot.packet))
                {
                    slot.receivedMs = FreeDClock.NowMs;
                    filled.Enqueue(slot);
                }
                else
                {
                    free.Enqueue(slot);
                }
            }
        }

        /// <summary>
        /// 遅延を過ぎたパケットのうち、最も古い 1 つを、<paramref name="destination"/> に写して取り出す。なければ false。確保はしない。
        /// </summary>
        /// <exception cref="InvalidOperationException">受信が途中で失敗して止まったとき。1 回だけ投げる。</exception>
        public bool TryTakeNext(FreeDPacket destination)
        {
            var failure = receiveException;
            if (failure != null)
            {
                receiveException = null;
                throw failure;
            }

            if (filledPackets == null || freePackets == null)
            {
                return false;
            }

            var delayedNowMs = FreeDClock.NowMs - delaySec * 1000d;
            if (!filledPackets.TryPeek(out var peeked) || peeked.receivedMs > delayedNowMs)
            {
                return false;
            }

            if (!filledPackets.TryDequeue(out var slot))
            {
                return false;
            }

            destination.CopyFrom(slot.packet);
            freePackets.Enqueue(slot);
            return true;
        }
    }
}
