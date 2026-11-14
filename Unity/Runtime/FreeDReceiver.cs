using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Cifer.Collections;
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
    private const FreeDFields AllFields =
        FreeDFields.Position | FreeDFields.Rotation | FreeDFields.Zoom | FreeDFields.Focus;

    [Tooltip("送信側の FreeD送信 のポートと同じにする")] public int port = 40000;

    [Tooltip("このポートから使う項目 (同じ項目は 1 つの Receiver だけ)")]
    public FreeDFields fields = AllFields;

    [Tooltip("ms。他のポートより早く届くときに、この分だけ反映を遅らせてそろえる")]
    public float delayMs = 0f;

    // 受信したパケットを、FreeDManager が取りに来るまでためておく数。60 fps で数秒ぶん。
    // 取りに来ない間も、あふれたら古いものを捨てるので、メモリは増えない。
    private const int ReceivedPacketCapacity = 256;

    private readonly RingBuffer<FreeDReceivedPacket> receivedPackets = new(ReceivedPacketCapacity);

    private UdpClient client;
    private CancellationTokenSource tokenSource;

    /// <summary>いま担当している項目。無効なときは None。</summary>
    public FreeDFields ActiveFields => isActiveAndEnabled ? fields & AllFields : FreeDFields.None;

    public void Open()
    {
        try
        {
            client = new UdpClient(port);
        }
        catch (Exception e)
        {
            Debug.LogError("FreeD: ポート " + port + " を開けない: " + e.Message, this);
            Close();
            return;
        }

        tokenSource = new CancellationTokenSource();
        Task.Run(() => ReceiveTask(tokenSource.Token));
    }

    public void Close()
    {
        tokenSource.Cancel();

        client.Close();
        client.Dispose();
        client = null;
    }

    /// <summary>
    /// 受信を待つ。ReceiveAsync がその場で完了したとき (すでに届いていたとき) は Completed が呼ばれないので、
    /// ここで処理して、待ちに入るまで繰り返す。
    /// </summary>
    private async Task ReceiveTask(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var result = await client.ReceiveAsync();
            if (FreeDCodec.TryDecode(result.Buffer, out var packet))
            {
                receivedPackets.Enqueue(new FreeDReceivedPacket
                {
                    receivedMs = DateTime.UtcNow.Subtract(DateTime.UnixEpoch).TotalMilliseconds,
                    packet = packet
                });
            }
        }
    }

    /// <summary>受信して復号したパケットを、届いた順に 1 つずつ取り出す。</summary>
    public bool TryDequeue(out FreeDReceivedPacket received)
    {
        return receivedPackets.TryDequeue(out received);
    }
}