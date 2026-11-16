#nullable enable
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using CIFER.tech.RingBuffer;

namespace CIFER.tech.FreeD.Core
{
    public sealed class FreeDReceiver : IFreeDProvider, IDisposable
    {
        // 送信が 60 fps より速くても足りるように、最低限の数と、遅延 1 秒あたりの数 (240 fps 相当) を持つ。
        private const int MinCapacity = 256;
        private const int CapacityPerDelaySec = 240;

        // FreeD D1 は 29 バイト。余裕を見た受信バッファの大きさ。
        private const int DatagramBufferSize = 64;

        private int port = 40000;
        private float delaySec;
        private FreeDField claimedFields = FreeDField.Rotation | FreeDField.Position | FreeDField.Zoom | FreeDField.Focus;

        // 受信スレッドで起きた例外。スレッドの外へは投げられないので、TryTakeNext で投げる。
        private volatile Exception? receiveException;
        private RingBuffer<FreeDProvidedPacket>? buffer;
        private Socket? socket;

        public int Port
        {
            get => port;
            set
            {
                ThrowIfOpen();
                port = value;
            }
        }

        public float DelaySec
        {
            get => delaySec;
            set
            {
                ThrowIfOpen();
                delaySec = value;
            }
        }

        public FreeDField ClaimedFields
        {
            get => claimedFields;
            set
            {
                ThrowIfOpen();
                claimedFields = value;
            }
        }

        public void Dispose()
        {
            Close();
        }

        public void Open()
        {
            Close();

            receiveException = null;
            var receivedPackets = new RingBuffer<FreeDProvidedPacket>(MinCapacity + (int)(delaySec * CapacityPerDelaySec));
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

        private void ThrowIfOpen()
        {
            if (socket != null)
            {
                throw new InvalidOperationException("FreeD: 開いている間は変えられない。Close してから変える");
            }
        }

        private void ReceiveLoop(Socket receivingSocket, RingBuffer<FreeDProvidedPacket> receivedPackets)
        {
            // 受信スレッドで想定外の例外が出ると、プロセスごと落ちる。TryTakeNext で投げ直すために、必ず受け止める。
            try
            {
                ReceiveUntilClosed(receivingSocket, receivedPackets);
            }
            catch (Exception e)
            {
                receiveException = new InvalidOperationException("FreeD: 受信に失敗して止まった: " + e.Message, e);
            }
        }

        private void ReceiveUntilClosed(Socket receivingSocket, RingBuffer<FreeDProvidedPacket> receivedPackets)
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
                    receivedPackets.Enqueue(new FreeDProvidedPacket
                    {
                        timestampMs = FreeDClock.NowMs,
                        packet = packet
                    });
                }
            }
        }

        /// <exception cref="InvalidOperationException">受信が途中で失敗して止まったとき。1 回だけ投げる。</exception>
        public bool TryTakeNext(out FreeDProvidedPacket provided)
        {
            provided = default;

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
            if (!buffer.TryPeek(out var peeked) || peeked.timestampMs > delayedNowMs)
            {
                return false;
            }

            return buffer.TryDequeue(out provided);
        }
    }
}
