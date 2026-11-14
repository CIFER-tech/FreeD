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
    [Tooltip("受信を開いた Receiver の数。0 なら、Receiver がないか、ポートを開けていない"), FreeDReadOnly]
    public int openedReceiverCount;
    [Tooltip("全 Receiver が直近 1 秒に受けたパケットの合計"), FreeDReadOnly]
    public int totalPacketsPerSecond;
    public List<CameraStatus> cameras = new List<CameraStatus>();

    private readonly object _lock = new object();
    private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
    // 受信を開いた Receiver。FreeDMerger の streamIndex は、この並びの添字。
    private readonly List<FreeDReceiver> _receivers = new List<FreeDReceiver>();
    private readonly Dictionary<int, FreeDMerger> _mergersByCameraId = new Dictionary<int, FreeDMerger>();
    private readonly Dictionary<int, Merged> _mergedByCameraId = new Dictionary<int, Merged>();
    // シーンから見つけた有効な Receiver。重なりなどで開けなかったものも含む。
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
            foreach (FreeDReceiver r in _receivers) r.receiving = nowMs - r.lastReceivedMs < TimeoutMs;
            PublishRates();
            PublishStatus();
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
        var claimed = FreeDFields.None;
        foreach (FreeDReceiver r in _foundReceivers)
        {
            FreeDFields overlapped = claimed & r.Fields;
            if (overlapped != FreeDFields.None)
            {
                Debug.LogError("FreeD: 同じ項目を複数の Receiver が担当している: " + overlapped, this);
                return;
            }
            claimed |= r.Fields;
        }

        _receivers.AddRange(_foundReceivers);
        foreach (FreeDReceiver r in _receivers) r.Open(OnPacket);
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

    void CloseReceivers()
    {
        foreach (FreeDReceiver r in _receivers) r.Close();
        _receivers.Clear();
        _mergersByCameraId.Clear();
        _mergedByCameraId.Clear();
        cameras.Clear();
        receiving = false;
        openedReceiverCount = 0;
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
        totalPacketsPerSecond = 0;
        foreach (FreeDReceiver r in _receivers)
        {
            r.PublishRates(_rateTimerSeconds);
            totalPacketsPerSecond += r.packetsPerSecond;
        }
        _rateTimerSeconds = 0f;
    }

    /// <summary>_lock の中で呼ぶ。</summary>
    void PublishStatus()
    {
        openedReceiverCount = _receivers.Count;
        receiving = false;
        foreach (FreeDReceiver r in _receivers) receiving |= r.receiving;

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
