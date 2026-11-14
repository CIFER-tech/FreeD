using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using FreeD;
using UnityEngine;

/// <summary>
/// MVP の Model。FreeD (UDP) をポート 1 つで受信して、復号したパケットをためておく。送り元のポートごとに 1 つ置く。
/// 姿勢は retroloc のポート 40000、ズームとフォーカスはレンズエンコーダのポート 41000、のように置き、Fields でそのポートから使う項目を選ぶ。
/// 受信の開始・停止とパケットの取り出しは FreeDManager が行う。Unity のライフイベントは使わない。
/// </summary>
[DisallowMultipleComponent]
public class FreeDReceiver : MonoBehaviour
{
    // Inspector で Everything を選ぶと全ビットが立った -1 になるので、項目のビットだけに絞って使う。
    private const FreeDFields AllFields = FreeDFields.Position | FreeDFields.Rotation | FreeDFields.Zoom | FreeDFields.Focus;

    // UDP のデータグラムが入る大きさ (イーサネットの MTU)。これより大きなデータグラムは受信でエラーになり、捨てる。
    private const int ReceiveBufferSize = 1500;

    [Tooltip("送信側の FreeD送信 のポートと同じにする")]
    public int port = 40000;

    [Tooltip("このポートから使う項目 (同じ項目は 1 つの Receiver だけ)")]
    public FreeDFields fields = AllFields;

    [Tooltip("ms。他のポートより早く届くときに、この分だけ反映を遅らせてそろえる")]
    public float delayMs = 0f;

    // 受信のたびに確保しないよう、バッファと SocketAsyncEventArgs は使い回す。
    private readonly byte[] _receiveBuffer = new byte[ReceiveBufferSize];
    private readonly FreeDReceivedPacketQueue _receivedPackets = new FreeDReceivedPacketQueue();
    private Socket _socket;
    private SocketAsyncEventArgs _receiveArgs;
    private CancellationTokenSource _cancellation;

    // 開こうとしたか (失敗したときも true)。開こうとしたときのポート。
    private bool _isOpenRequested;
    private int _requestedPort;

    internal FreeDFields UsedFields
    {
        get
        {
            return fields & AllFields;
        }
    }

    /// <summary>
    /// 有効か無効か、ポートが変わったかに合わせて、受信を開く・閉じる。FreeDManager が毎フレーム呼ぶ。
    /// 開くのに失敗したときは、無効にするかポートを変えるまで、開き直さない (毎フレーム、エラーを出さないため)。
    /// </summary>
    internal void SyncSocket()
    {
        bool shouldOpen = isActiveAndEnabled;
        if (_isOpenRequested && (!shouldOpen || _requestedPort != port))
        {
            Close();
        }

        if (!_isOpenRequested && shouldOpen)
        {
            _isOpenRequested = true;
            _requestedPort = port;
            Open();
        }
    }

    private void Open()
    {
        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        }
        catch (Exception e)
        {
            Debug.LogError("FreeD: ポート " + port + " を開けない: " + e.Message, this);
            CloseSocket();
            return;
        }

        _cancellation = new CancellationTokenSource();
        _receiveArgs = new SocketAsyncEventArgs();
        _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
        _receiveArgs.Completed += OnReceiveCompleted;
        ReceiveUntilPending(_cancellation.Token);
    }

    internal void Close()
    {
        _isOpenRequested = false;
        if (_cancellation != null)
        {
            _cancellation.Cancel();
        }
        // 受信待ちは、ソケットを閉じると OperationAborted で完了する。
        CloseSocket();
        if (_receiveArgs != null)
        {
            _receiveArgs.Completed -= OnReceiveCompleted;
            _receiveArgs.Dispose();
            _receiveArgs = null;
        }
        if (_cancellation != null)
        {
            _cancellation.Dispose();
            _cancellation = null;
        }
        _receivedPackets.Clear();
    }

    /// <summary>受信して復号したパケットを、届いた順に 1 つずつ取り出す。</summary>
    internal bool TryDequeue(out FreeDReceivedPacket received)
    {
        return _receivedPackets.TryDequeue(out received);
    }

    // Open の失敗時と Close の 2 か所から呼ぶ。
    private void CloseSocket()
    {
        if (_socket != null)
        {
            _socket.Close();
            _socket = null;
        }
    }

    /// <summary>
    /// 受信を待つ。ReceiveAsync がその場で完了したとき (すでに届いていたとき) は Completed が呼ばれないので、
    /// ここで処理して、待ちに入るまで繰り返す。
    /// </summary>
    private void ReceiveUntilPending(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            bool isPending;
            try
            {
                isPending = _socket.ReceiveAsync(_receiveArgs);
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            if (isPending)
            {
                return;
            }
            EnqueueReceived(_receiveArgs);
        }
    }

    // 受信スレッド (スレッドプール) で呼ばれる。
    private void OnReceiveCompleted(object sender, SocketAsyncEventArgs args)
    {
        CancellationTokenSource cancellation = _cancellation;
        if (cancellation == null || cancellation.IsCancellationRequested)
        {
            return;
        }
        EnqueueReceived(args);
        ReceiveUntilPending(cancellation.Token);
    }

    /// <summary>受信したデータグラムを復号して、受信時刻つきでためる。復号できないデータは捨てる。</summary>
    private void EnqueueReceived(SocketAsyncEventArgs args)
    {
        if (args.SocketError != SocketError.Success || args.BytesTransferred <= 0)
        {
            return;
        }
        var datagram = new ReadOnlySpan<byte>(_receiveBuffer, 0, args.BytesTransferred);
        if (FreeDCodec.TryDecode(datagram, out FreeDPacket packet))
        {
            _receivedPackets.Enqueue(new FreeDReceivedPacket { packet = packet, receivedMs = FreeDClock.NowMs });
        }
    }
}
