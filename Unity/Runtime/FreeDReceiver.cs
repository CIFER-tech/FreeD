using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using FreeD;
using UnityEngine;

/// <summary>
/// FreeD (UDP) をポート 1 つで受信して、復号したパケットを保持するだけ。ポートごとに 1 つ置く。保持したパケットは、FreeDManager が毎フレーム取りに来る。
/// 受信の開始・停止、合成、受信状態の集計はすべて FreeDManager が行う (FreeDManager がシーンになければ何も受信しない)。
/// 姿勢は retroloc のポート 40000、ズームとフォーカスはレンズエンコーダのポート 41000、のように
/// 送り元ごとに Receiver を置き、Fields でそのポートから使う項目を選ぶ。
/// 受信は SocketAsyncEventArgs と使い回す受信バッファで行い、パケットごとに GC を起こさない。
/// </summary>
[DisallowMultipleComponent]
public class FreeDReceiver : MonoBehaviour
{
    // Everything を選ぶと全ビットが立った -1 になるので、項目のビットだけに絞って使う。
    private const FreeDFields AllFields = FreeDFields.Position | FreeDFields.Rotation | FreeDFields.Zoom | FreeDFields.Focus;

    // UDP のデータグラムが入る大きさ。バッファより大きなデータグラムは受信でエラーになる。
    private const int ReceiveBufferSize = 1500;

    [Tooltip("送信側の FreeD送信 のポートと同じにする")]
    public int port = 40000;

    [Tooltip("このポートから使う項目 (同じ項目は 1 つの Receiver だけ)")]
    public FreeDFields fields = AllFields;

    [Tooltip("ms。他のポートより早く届くときに、この分だけ反映を遅らせてそろえる")]
    public float delayMs = 0f;

    private readonly byte[] _receiveBuffer = new byte[ReceiveBufferSize];
    // 受信が積み、FreeDManager が Update で取り出す。
    private readonly FreeDReceivedPacketQueue _receivedPackets = new FreeDReceivedPacketQueue();
    private Socket _socket;
    private SocketAsyncEventArgs _receiveArgs;
    private CancellationTokenSource _cancellation;

    public FreeDFields Fields
    {
        get
        {
            return fields & AllFields;
        }
        set
        {
            fields = value;
        }
    }

    // 有効・無効が変わったことを Manager に知らせる。受信を開き直すのは Manager。
    private void OnEnable()
    {
        NotifyManager();
    }

    private void OnDisable()
    {
        NotifyManager();
    }

    private static void NotifyManager()
    {
        if (FreeDManager.Instance != null)
        {
            FreeDManager.Instance.MarkReceiversChanged();
        }
    }

    /// <summary>FreeDManager が呼ぶ。受信を始める。復号したパケットは保持し、Manager が毎フレーム取りに来る。</summary>
    internal void Open()
    {
        try
        {
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            _socket.Bind(new IPEndPoint(IPAddress.Any, port));
        }
        catch (Exception e)
        {
            Debug.LogError("FreeD: ポート " + port + " を開けない: " + e.Message, this);
            DisposeSocket();
            return;
        }

        _cancellation = new CancellationTokenSource();
        _receiveArgs = new SocketAsyncEventArgs();
        _receiveArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
        _receiveArgs.Completed += OnReceiveCompleted;
        ReceiveNext(_cancellation.Token);
    }

    /// <summary>FreeDManager が毎フレーム呼ぶ。受信して復号したパケットを、届いた順に 1 つずつ取り出す。</summary>
    internal bool TryDequeue(out FreeDReceivedPacket received)
    {
        return _receivedPackets.TryDequeue(out received);
    }

    /// <summary>FreeDManager が呼ぶ。受信を止めてポートを閉じる。</summary>
    internal void Close()
    {
        if (_cancellation != null)
        {
            _cancellation.Cancel();
        }
        // 受信待ちは、ソケットを閉じると OperationAborted で終わる。
        DisposeSocket();
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

    private void DisposeSocket()
    {
        if (_socket != null)
        {
            _socket.Close();
            _socket = null;
        }
    }

    /// <summary>受信を待つ。すぐに届いていれば (同期で完了すれば) その場で処理して、続けて待つ。待ちに入ったら戻る。</summary>
    private void ReceiveNext(CancellationToken token)
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
            Store(_receiveArgs);
        }
    }

    private void OnReceiveCompleted(object sender, SocketAsyncEventArgs args)
    {
        CancellationTokenSource cancellation = _cancellation;
        if (cancellation == null || cancellation.IsCancellationRequested)
        {
            return;
        }
        Store(args);
        ReceiveNext(cancellation.Token);
    }

    /// <summary>受信したデータグラムを復号して、受信時刻つきで保持する。復号できないデータは捨てる。</summary>
    private void Store(SocketAsyncEventArgs args)
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
