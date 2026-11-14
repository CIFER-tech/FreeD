using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using FreeD;
using UnityEngine;

/// <summary>
/// FreeD (UDP) をポート 1 つで受信する。ポートごとに 1 つ置く。
/// 受信の開始・停止は FreeDManager が行う (FreeDManager がシーンになければ何も受信しない)。
/// 受信したパケットは FreeDManager がカメラ ID ごとに合成して、FreeDTracker に渡す。
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

    // 状態の欄は FreeDReceiverEditor が枠で囲んで描く (Header は付けない)
    [FreeDReadOnly] public bool receiving;
    [FreeDReadOnly] public int packetsPerSecond;
    [Tooltip("直近1秒の受信間隔 (ms)"), FreeDReadOnly]
    public float meanIntervalMs, maxIntervalMs;

    /// <summary>有効な Receiver。FreeDManager がここから受信を開始する。</summary>
    internal static readonly List<FreeDReceiver> Active = new List<FreeDReceiver>();

    // 受信スレッドが書き、FreeDManager の Update が読む。FreeDManager の _lock で守る。
    [NonSerialized] internal int count;
    [NonSerialized] internal double sumIntervalMs, maxIntervalMsSoFar, lastReceivedMs = -1;

    [NonSerialized] private FreeDFields _lastFields;
    private UdpClient _udp;
    private Thread _thread;
    private volatile bool _isRunning;

    public FreeDFields Fields
    {
        get { return fields & AllFields; }
        set { fields = value; }
    }

    void OnEnable() { Active.Add(this); }
    void OnDisable() { Active.Remove(this); }

    void OnValidate()
    {
        // 今回新しく選んだ項目は、他の Receiver から外して譲らせる。
        FreeDFields added = Fields & ~_lastFields;
        if (added != FreeDFields.None)
        {
            foreach (FreeDReceiver other in FindOthers())
            {
                other.Fields &= ~added;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(other);
#endif
            }
        }
        _lastFields = Fields;
    }

    IEnumerable<FreeDReceiver> FindOthers()
    {
#if UNITY_2022_2_OR_NEWER
        FreeDReceiver[] all = FindObjectsByType<FreeDReceiver>(FindObjectsSortMode.None);
#else
        FreeDReceiver[] all = FindObjectsOfType<FreeDReceiver>();
#endif
        foreach (FreeDReceiver r in all)
            if (r != this) yield return r;
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
        receiving = false;
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

    /// <summary>パケットを受けた時刻を記録する。FreeDManager の _lock の中で呼ぶ。</summary>
    internal void RecordPacket(double nowMs)
    {
        count++;
        if (lastReceivedMs >= 0)
        {
            double intervalMs = nowMs - lastReceivedMs;
            sumIntervalMs += intervalMs;
            if (intervalMs > maxIntervalMsSoFar) maxIntervalMsSoFar = intervalMs;
        }
        lastReceivedMs = nowMs;
    }

    /// <summary>1 秒ぶんの受信間隔を状態の欄に出して、集計をやり直す。FreeDManager の _lock の中で呼ぶ。</summary>
    internal void PublishRates(float elapsedSeconds)
    {
        packetsPerSecond = Mathf.RoundToInt(count / elapsedSeconds);
        meanIntervalMs = count > 1 ? (float)(sumIntervalMs / (count - 1)) : 0f;
        maxIntervalMs = (float)maxIntervalMsSoFar;
        count = 0; sumIntervalMs = 0; maxIntervalMsSoFar = 0;
    }
}
