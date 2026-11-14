using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FreeD;
using UnityEngine;

/// <summary>
/// FreeD (UDP) をポート 1 つで受信して、パケットを FreeDManager に渡すだけ。ポートごとに 1 つ置く。
/// 受信の開始・停止、合成、受信状態の集計はすべて FreeDManager が行う (FreeDManager がシーンになければ何も受信しない)。
/// 姿勢は retroloc のポート 40000、ズームとフォーカスはレンズエンコーダのポート 41000、のように
/// 送り元ごとに Receiver を置き、Fields でそのポートから使う項目を選ぶ。
/// </summary>
[DisallowMultipleComponent]
public class FreeDReceiver : MonoBehaviour
{
    // Everything を選ぶと全ビットが立った -1 になるので、項目のビットだけに絞って使う。
    private const FreeDFields AllFields = FreeDFields.Position | FreeDFields.Rotation | FreeDFields.Zoom | FreeDFields.Focus;

    [Tooltip("送信側の FreeD送信 のポートと同じにする")]
    public int port = 40000;

    [Tooltip("このポートから使う項目 (同じ項目は 1 つの Receiver だけ)")]
    public FreeDFields fields = AllFields;

    [Tooltip("ms。他のポートより早く届くときに、この分だけ反映を遅らせてそろえる")]
    public float delayMs = 0f;

    private UdpClient _udp;
    private Task _receiveTask;
    private CancellationTokenSource _cancellation;

    public FreeDFields Fields
    {
        get { return fields & AllFields; }
        set { fields = value; }
    }

    // 有効・無効が変わったことを Manager に知らせる。受信を開き直すのは Manager。
    private void OnEnable() { NotifyManager(); }
    private void OnDisable() { NotifyManager(); }

    private static void NotifyManager()
    {
        if (FreeDManager.Instance != null) FreeDManager.Instance.MarkReceiversChanged();
    }

    /// <summary>FreeDManager が呼ぶ。受信を始め、パケットが届くたびに onPacket を受信タスク (スレッドプール) から呼ぶ。</summary>
    internal void Open(Action<FreeDReceiver, FreeDPacket> onPacket)
    {
        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
        }
        catch (Exception e)
        {
            Debug.LogError("FreeD: ポート " + port + " を開けない: " + e.Message, this);
            _udp = null;
            return;
        }
        _cancellation = new CancellationTokenSource();
        UdpClient udp = _udp;
        CancellationToken token = _cancellation.Token;
        _receiveTask = Task.Run(() => ReceiveLoopAsync(udp, onPacket, token));
    }

    /// <summary>FreeDManager が呼ぶ。受信を止めてポートを閉じる。</summary>
    internal void Close()
    {
        if (_cancellation == null) return;
        _cancellation.Cancel();
        try { _receiveTask.Wait(500); } catch { }
        _udp.Close();
        _cancellation.Dispose();
        _udp = null;
        _receiveTask = null;
        _cancellation = null;
    }

    private async Task ReceiveLoopAsync(UdpClient udp, Action<FreeDReceiver, FreeDPacket> onPacket, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await ReceiveAsync(udp, token);
            }
            catch (OperationCanceledException) { return; }
            catch (ObjectDisposedException) { return; }
            catch (SocketException) { continue; }
            if (FreeDCodec.TryDecode(result.Buffer, out FreeDPacket p)) onPacket(this, p);
        }
    }

    /// <summary>UdpClient.ReceiveAsync はキャンセルできないので、トークンが取り消されたら、ソケットを閉じて待ちを終わらせる。</summary>
    private static async Task<UdpReceiveResult> ReceiveAsync(UdpClient udp, CancellationToken token)
    {
        using (token.Register(() => udp.Close()))
        {
            try
            {
                return await udp.ReceiveAsync();
            }
            catch (Exception) when (token.IsCancellationRequested)
            {
                throw new OperationCanceledException(token);
            }
        }
    }
}
