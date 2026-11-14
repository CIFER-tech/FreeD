using System.Collections.Generic;
using FreeD;
using UnityEngine;

/// <summary>
/// FreeD 受信の寿命をすべて管理する。シーンに 1 つ置く。
/// 有効な FreeDReceiver の受信を開始・停止し、届いたパケットをカメラ ID ごとに FreeDMerger で合成して、
/// FreeDTracker に渡す。FreeDManager がなければ、受信も反映も行われない。
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class FreeDManager : MonoBehaviour
{
    /// <summary>この時間 (ms) パケットが来ない Receiver を、途切れとみなす。</summary>
    private const double TimeoutMs = 500;

    private struct Merged
    {
        public FreeDFields filled;
        public FreeDPacket packet;
        public bool isReceiving;
    }

    public static FreeDManager Instance { get; private set; }

    private readonly object _lock = new object();
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
    // 受信を開いた Receiver。FreeDMerger の streamIndex は、この並びの添字。
    private readonly List<FreeDReceiver> _receivers = new List<FreeDReceiver>();
    private readonly Dictionary<int, FreeDMerger> _mergersByCameraId = new Dictionary<int, FreeDMerger>();
    private readonly Dictionary<int, Merged> _mergedByCameraId = new Dictionary<int, Merged>();
    private int _openedSignature;
    private float _rateTimerSeconds;

    void OnEnable()
    {
        Instance = this;
        OpenReceivers();
    }

    void OnDisable()
    {
        CloseReceivers();
        if (Instance == this) Instance = null;
    }

    void OnDestroy() { CloseReceivers(); }

    void Update()
    {
        if (Signature() != _openedSignature)
        {
            CloseReceivers();
            OpenReceivers();
        }

        double nowMs = _stopwatch.Elapsed.TotalMilliseconds;
        lock (_lock)
        {
            MergeAll(nowMs);
            foreach (FreeDReceiver r in _receivers) r.receiving = nowMs - r.lastReceivedMs < TimeoutMs;
            PublishRates();
        }
    }

    /// <summary>カメラ ID のデータを合成した値を取る。その ID のパケットがまだ 1 つも届いていないときは false。</summary>
    /// <param name="filled">1 度でも値が入った項目。含まれない項目は packet の中身を使ってはいけない。</param>
    public bool TryGet(int cameraId, out FreeDFields filled, out FreeDPacket packet, out bool isReceiving)
    {
        if (_mergedByCameraId.TryGetValue(cameraId, out Merged merged))
        {
            filled = merged.filled; packet = merged.packet; isReceiving = merged.isReceiving;
            return true;
        }
        filled = FreeDFields.None; packet = default; isReceiving = false;
        return false;
    }

    void OpenReceivers()
    {
        _openedSignature = Signature();
        var claimed = FreeDFields.None;
        foreach (FreeDReceiver r in FreeDReceiver.Active)
        {
            FreeDFields overlapped = claimed & r.Fields;
            if (overlapped != FreeDFields.None)
            {
                Debug.LogError("FreeD: 同じ項目を複数の Receiver が担当している: " + overlapped, this);
                return;
            }
            claimed |= r.Fields;
        }

        _receivers.AddRange(FreeDReceiver.Active);
        foreach (FreeDReceiver r in _receivers) r.Open(OnPacket);
    }

    void CloseReceivers()
    {
        foreach (FreeDReceiver r in _receivers) r.Close();
        _receivers.Clear();
        _mergersByCameraId.Clear();
        _mergedByCameraId.Clear();
    }

    /// <summary>ポート・項目・本数のどれかが変わると値が変わる。再生中の設定変更で受信をやり直す目印。</summary>
    static int Signature()
    {
        unchecked
        {
            int hash = FreeDReceiver.Active.Count;
            foreach (FreeDReceiver r in FreeDReceiver.Active)
                hash = hash * 31 + r.port * 31 + (int)r.Fields;
            return hash;
        }
    }

    /// <summary>受信スレッドから呼ばれる。</summary>
    void OnPacket(FreeDReceiver receiver, FreeDPacket packet)
    {
        double nowMs = _stopwatch.Elapsed.TotalMilliseconds;
        lock (_lock)
        {
            int receiverIndex = _receivers.IndexOf(receiver);
            if (receiverIndex < 0) return;
            if (!_mergersByCameraId.TryGetValue(packet.CameraId, out FreeDMerger merger))
            {
                merger = new FreeDMerger(FieldsOfReceivers());
                _mergersByCameraId[packet.CameraId] = merger;
            }
            merger.Put(receiverIndex, packet, nowMs);
            receiver.RecordPacket(nowMs);
        }
    }

    List<FreeDFields> FieldsOfReceivers()
    {
        var fields = new List<FreeDFields>();
        foreach (FreeDReceiver r in _receivers) fields.Add(r.Fields);
        return fields;
    }

    /// <summary>_lock の中で呼ぶ。</summary>
    void MergeAll(double nowMs)
    {
        foreach (KeyValuePair<int, FreeDMerger> pair in _mergersByCameraId)
        {
            FreeDMerger merger = pair.Value;
            bool isReceiving = false;
            for (int i = 0; i < _receivers.Count; i++)
            {
                merger.SetDelay(i, _receivers[i].delayMs);
                isReceiving |= merger.IsReceiving(i, nowMs);
            }
            FreeDFields filled = merger.Update(nowMs, out FreeDPacket packet);
            _mergedByCameraId[pair.Key] = new Merged { filled = filled, packet = packet, isReceiving = isReceiving };
        }
    }

    /// <summary>_lock の中で呼ぶ。</summary>
    void PublishRates()
    {
        _rateTimerSeconds += Time.unscaledDeltaTime;
        if (_rateTimerSeconds < 1f) return;
        foreach (FreeDReceiver r in _receivers) r.PublishRates(_rateTimerSeconds);
        _rateTimerSeconds = 0f;
    }
}
