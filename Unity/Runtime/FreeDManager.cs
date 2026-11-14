using System.Collections.Generic;
using FreeD;
using UnityEngine;

/// <summary>
/// FreeD 受信を管理する。シーンに 1 つ置く。
/// 有効な FreeDReceiver の受信を開始・停止し、届いたパケットをカメラ ID ごとに FreeDMerger で合成して、
/// FreeDTracker に渡す。受信の状態 (pkt/s・間隔・途切れ) も集計する。
/// FreeDManager がなければ、受信も反映も行われない。
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

    /// <summary>Receiver (ポート) ごとの受信状態 (Inspector で見る用)。</summary>
    [System.Serializable]
    public class ReceiverStatus
    {
        public int port;
        public bool isReceiving;
        public int packetsPerSecond;
        [Tooltip("直近1秒の受信間隔 (ms)")]
        public float meanIntervalMs, maxIntervalMs;

        // 受信スレッドが書き、Update が読む。FreeDManager の _lock で守る。
        [System.NonSerialized] private int _count;
        [System.NonSerialized] private double _sumIntervalMs, _maxIntervalMsSoFar, _lastReceivedMs = double.NegativeInfinity;

        internal void RecordPacket(double nowMs)
        {
            _count++;
            if (_lastReceivedMs > double.NegativeInfinity)
            {
                double intervalMs = nowMs - _lastReceivedMs;
                _sumIntervalMs += intervalMs;
                if (intervalMs > _maxIntervalMsSoFar) _maxIntervalMsSoFar = intervalMs;
            }
            _lastReceivedMs = nowMs;
        }

        internal void UpdateIsReceiving(double nowMs) { isReceiving = nowMs - _lastReceivedMs < TimeoutMs; }

        /// <summary>1 秒ぶんの受信間隔を出して、集計をやり直す。</summary>
        internal void PublishRates(float elapsedSeconds)
        {
            packetsPerSecond = Mathf.RoundToInt(_count / elapsedSeconds);
            meanIntervalMs = _count > 1 ? (float)(_sumIntervalMs / (_count - 1)) : 0f;
            maxIntervalMs = (float)_maxIntervalMsSoFar;
            _count = 0; _sumIntervalMs = 0; _maxIntervalMsSoFar = 0;
        }
    }

    /// <summary>届いたカメラ ID ごとの状態 (Inspector で見る用)。</summary>
    [System.Serializable]
    public class CameraStatus
    {
        public int cameraId;
        public bool isReceiving;
        public string fields;
        [System.NonSerialized] internal FreeDFields lastFilled = (FreeDFields)(-1);
    }

    public static FreeDManager Instance { get; private set; }

    // 状態の欄は FreeDManagerEditor が枠で囲んで描く (Header は付けない)
    [FreeDReadOnly] public bool receiving;
    [Tooltip("全 Receiver が直近 1 秒に受けたパケットの合計"), FreeDReadOnly]
    public int totalPacketsPerSecond;
    public List<ReceiverStatus> receivers = new List<ReceiverStatus>();
    public List<CameraStatus> cameras = new List<CameraStatus>();

    private readonly object _lock = new object();
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
    // 受信を開いた Receiver。FreeDMerger の streamIndex と receivers の添字は、この並びと同じ。
    private readonly List<FreeDReceiver> _openedReceivers = new List<FreeDReceiver>();
    private readonly Dictionary<int, FreeDMerger> _mergersByCameraId = new Dictionary<int, FreeDMerger>();
    private readonly Dictionary<int, Merged> _mergedByCameraId = new Dictionary<int, Merged>();
    // シーンから見つけた有効な Receiver。項目の重なりなどで開けなかったものも含む。
    private readonly List<FreeDReceiver> _foundReceivers = new List<FreeDReceiver>();
    private bool _isReceiverSetChanged;
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
        if (_isReceiverSetChanged || Signature() != _openedSignature)
        {
            CloseReceivers();
            OpenReceivers();
        }

        double nowMs = _stopwatch.Elapsed.TotalMilliseconds;
        lock (_lock)
        {
            MergeAll(nowMs);
            PublishReceiverStatus(nowMs);
            PublishCameraStatus();
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

    /// <summary>FreeDReceiver が、有効・無効が変わったときに呼ぶ。次の Update で、受信を開き直す。</summary>
    internal void MarkReceiversChanged() { _isReceiverSetChanged = true; }

    void OpenReceivers()
    {
        _isReceiverSetChanged = false;
        FindEnabledReceivers();
        _openedSignature = Signature();
        if (HasOverlappedFields()) return;

        _openedReceivers.AddRange(_foundReceivers);
        foreach (FreeDReceiver r in _openedReceivers)
        {
            receivers.Add(new ReceiverStatus { port = r.port });
            r.Open(OnPacket);
        }
    }

    void FindEnabledReceivers()
    {
        _foundReceivers.Clear();
#if UNITY_2022_2_OR_NEWER
        FreeDReceiver[] all = FindObjectsByType<FreeDReceiver>(FindObjectsSortMode.InstanceID);
#else
        FreeDReceiver[] all = FindObjectsOfType<FreeDReceiver>();
#endif
        foreach (FreeDReceiver r in all)
            if (r.isActiveAndEnabled) _foundReceivers.Add(r);
    }

    /// <summary>同じ項目を 2 つの Receiver が担当していると、合成できない。</summary>
    bool HasOverlappedFields()
    {
        var claimed = FreeDFields.None;
        foreach (FreeDReceiver r in _foundReceivers)
        {
            FreeDFields overlapped = claimed & r.Fields;
            if (overlapped != FreeDFields.None)
            {
                Debug.LogError("FreeD: 同じ項目を複数の Receiver が担当している: " + overlapped, this);
                return true;
            }
            claimed |= r.Fields;
        }
        return false;
    }

    void CloseReceivers()
    {
        foreach (FreeDReceiver r in _openedReceivers) r.Close();
        _openedReceivers.Clear();
        _mergersByCameraId.Clear();
        _mergedByCameraId.Clear();
        receivers.Clear();
        cameras.Clear();
        receiving = false;
        totalPacketsPerSecond = 0;
    }

    /// <summary>ポート・項目・本数のどれかが変わると値が変わる。再生中の設定変更で受信をやり直す目印。</summary>
    int Signature()
    {
        unchecked
        {
            int hash = _foundReceivers.Count;
            foreach (FreeDReceiver r in _foundReceivers)
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
            int receiverIndex = _openedReceivers.IndexOf(receiver);
            if (receiverIndex < 0) return;
            if (!_mergersByCameraId.TryGetValue(packet.CameraId, out FreeDMerger merger))
            {
                merger = new FreeDMerger(FieldsOfOpenedReceivers());
                _mergersByCameraId[packet.CameraId] = merger;
            }
            merger.Put(receiverIndex, packet, nowMs);
            receivers[receiverIndex].RecordPacket(nowMs);
        }
    }

    List<FreeDFields> FieldsOfOpenedReceivers()
    {
        var fields = new List<FreeDFields>();
        foreach (FreeDReceiver r in _openedReceivers) fields.Add(r.Fields);
        return fields;
    }

    /// <summary>_lock の中で呼ぶ。</summary>
    void MergeAll(double nowMs)
    {
        foreach (KeyValuePair<int, FreeDMerger> pair in _mergersByCameraId)
        {
            FreeDMerger merger = pair.Value;
            bool isReceiving = false;
            for (int i = 0; i < _openedReceivers.Count; i++)
            {
                merger.SetDelay(i, _openedReceivers[i].delayMs);
                isReceiving |= merger.IsReceiving(i, nowMs);
            }
            FreeDFields filled = merger.Update(nowMs, out FreeDPacket packet);
            _mergedByCameraId[pair.Key] = new Merged { filled = filled, packet = packet, isReceiving = isReceiving };
        }
    }

    /// <summary>_lock の中で呼ぶ。</summary>
    void PublishReceiverStatus(double nowMs)
    {
        receiving = false;
        foreach (ReceiverStatus status in receivers)
        {
            status.UpdateIsReceiving(nowMs);
            receiving |= status.isReceiving;
        }

        _rateTimerSeconds += Time.unscaledDeltaTime;
        if (_rateTimerSeconds < 1f) return;
        totalPacketsPerSecond = 0;
        foreach (ReceiverStatus status in receivers)
        {
            status.PublishRates(_rateTimerSeconds);
            totalPacketsPerSecond += status.packetsPerSecond;
        }
        _rateTimerSeconds = 0f;
    }

    /// <summary>_lock の中で呼ぶ。</summary>
    void PublishCameraStatus()
    {
        foreach (KeyValuePair<int, Merged> pair in _mergedByCameraId)
        {
            CameraStatus status = FindOrAddCameraStatus(pair.Key);
            status.isReceiving = pair.Value.isReceiving;
            if (status.lastFilled == pair.Value.filled) continue;
            status.lastFilled = pair.Value.filled;
            status.fields = pair.Value.filled.ToString();
        }
    }

    /// <summary>cameras はカメラ ID の昇順に保つ。</summary>
    CameraStatus FindOrAddCameraStatus(int cameraId)
    {
        int insertIndex = 0;
        for (; insertIndex < cameras.Count; insertIndex++)
        {
            if (cameras[insertIndex].cameraId == cameraId) return cameras[insertIndex];
            if (cameras[insertIndex].cameraId > cameraId) break;
        }
        var status = new CameraStatus { cameraId = cameraId };
        cameras.Insert(insertIndex, status);
        return status;
    }
}
