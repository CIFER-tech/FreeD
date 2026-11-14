using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
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
    private Thread _thread;
    private volatile bool _isRunning;

    public FreeDFields Fields
    {
        get { return fields & AllFields; }
        set { fields = value; }
    }

    // 有効・無効が変わったことを Manager に知らせる。受信を開き直すのは Manager。
    void OnEnable() { NotifyManager(); }
    void OnDisable() { NotifyManager(); }

    static void NotifyManager()
    {
        if (FreeDManager.Instance != null) FreeDManager.Instance.MarkReceiversChanged();
    }

    /// <summary>FreeDManager が呼ぶ。受信を始め、パケットが届くたびに onPacket を受信スレッドから呼ぶ。</summary>
    internal void Open(Action<FreeDReceiver, FreeDPacket> onPacket)
    {
        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            _udp.Client.ReceiveTimeout = 200;
        }
        catch (Exception e)
        {
            Debug.LogError("FreeD: ポート " + port + " を開けない: " + e.Message, this);
            _udp = null;
            return;
        }
        _isRunning = true;
        UdpClient udp = _udp;
        _thread = new Thread(() => Loop(udp, onPacket)) { IsBackground = true, Name = "FreeDReceiver " + port };
        _thread.Start();
    }

    /// <summary>FreeDManager が呼ぶ。受信を止めてポートを閉じる。</summary>
    internal void Close()
    {
        _isRunning = false;
        if (_udp != null) { try { _udp.Close(); } catch { } }
        if (_thread != null) _thread.Join(500);
        _udp = null;
        _thread = null;
    }

    void Loop(UdpClient udp, Action<FreeDReceiver, FreeDPacket> onPacket)
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (_isRunning)
        {
            try
            {
                byte[] data = udp.Receive(ref any);
                if (FreeDCodec.TryDecode(data, out FreeDPacket p)) onPacket(this, p);
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { return; }
        }
    }
}
