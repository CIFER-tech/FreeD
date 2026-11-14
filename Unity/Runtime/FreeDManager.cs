using System.Collections.Generic;
using FreeD;
using UnityEngine;

/// <summary>
/// FreeD 受信を管理する。シーンに 1 つ置く。
/// 有効な FreeDReceiver の受信を開始・停止し、毎フレーム各 Receiver のパケットを取りに行って、カメラ ID ごとに FreeDMerger で合成して、
/// FreeDTracker に渡す。受信の状態 (pkt/s・間隔・途切れ) も集計する。
/// FreeDManager がなければ、受信も反映も行われない。
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class FreeDManager : MonoBehaviour
{
    public static FreeDManager Instance { get; private set; }

    // 状態の欄は FreeDManagerEditor が枠で囲んで描く (Header は付けない)
    [FreeDReadOnly] public bool receiving;
    [Tooltip("全 Receiver が直近 1 秒に受けたパケットの合計"), FreeDReadOnly]
    public int totalPacketsPerSecond;
    public List<FreeDCameraStatus> cameras = new List<FreeDCameraStatus>();

    // 受信を開いた Receiver。FreeDMerger の streamIndex は、この並びの添字。
    private readonly List<FreeDReceiver> _openedReceivers = new List<FreeDReceiver>();
    private readonly Dictionary<int, FreeDMerger> _mergersByCameraId = new Dictionary<int, FreeDMerger>();
    private readonly Dictionary<int, FreeDMergedCamera> _mergedByCameraId = new Dictionary<int, FreeDMergedCamera>();
    // シーンから見つけた有効な Receiver。項目の重なりなどで開けなかったものも含む。
    private readonly List<FreeDReceiver> _foundReceivers = new List<FreeDReceiver>();
    // このフレームに Receiver から取り出したパケット。毎フレーム空にして使い回す。
    private readonly List<FreeDPolledPacket> _polledPackets = new List<FreeDPolledPacket>();
    private bool _isReceiverSetChanged;
    private int _openedSignature;

    private void OnEnable()
    {
        Instance = this;
        StartReceiving();
    }

    private void OnDisable()
    {
        StopReceiving();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDestroy()
    {
        StopReceiving();
    }

    private void Update()
    {
        if (IsRestartNeeded())
        {
            StopReceiving();
            StartReceiving();
        }

        double nowMs = FreeDClock.NowMs;
        PollReceivers();
        PutPolledPacketsToMergers();
        MergeAll(nowMs);
        UpdateReceiverSummary();
        UpdateCameraStatus();
    }

    /// <summary>カメラ ID のデータを合成した値を取る。その ID のパケットがまだ 1 つも届いていないときは false。</summary>
    /// <param name="filled">1 度でも値が入った項目。含まれない項目は packet の中身を使ってはいけない。</param>
    public bool TryGet(int cameraId, out FreeDFields filled, out FreeDPacket packet, out bool isReceiving)
    {
        if (_mergedByCameraId.TryGetValue(cameraId, out FreeDMergedCamera merged))
        {
            filled = merged.filled;
            packet = merged.packet;
            isReceiving = merged.isReceiving;
            return true;
        }
        filled = FreeDFields.None;
        packet = default;
        isReceiving = false;
        return false;
    }

    /// <summary>FreeDReceiver が、有効・無効が変わったときに呼ぶ。次の Update で、受信を開き直す。</summary>
    internal void MarkReceiversChanged()
    {
        _isReceiverSetChanged = true;
    }

    // ---- 開始・停止 ----

    private void StartReceiving()
    {
        _isReceiverSetChanged = false;
        FindEnabledReceivers();
        _openedSignature = Signature();
        if (HasOverlappedFields())
        {
            return;
        }

        foreach (FreeDReceiver receiver in _foundReceivers)
        {
            OpenReceiver(receiver);
        }
    }

    private void StopReceiving()
    {
        CloseReceivers();
        ClearState();
    }

    private void OpenReceiver(FreeDReceiver receiver)
    {
        _openedReceivers.Add(receiver);
        receiver.Open();
    }

    private void CloseReceivers()
    {
        foreach (FreeDReceiver receiver in _openedReceivers)
        {
            receiver.Close();
        }
        _openedReceivers.Clear();
    }

    private void ClearState()
    {
        _polledPackets.Clear();
        _mergersByCameraId.Clear();
        _mergedByCameraId.Clear();
        cameras.Clear();
        receiving = false;
        totalPacketsPerSecond = 0;
    }

    // ---- 受信する Receiver の決定 ----

    private void FindEnabledReceivers()
    {
        _foundReceivers.Clear();
#if UNITY_2022_2_OR_NEWER
        FreeDReceiver[] all = FindObjectsByType<FreeDReceiver>(FindObjectsSortMode.InstanceID);
#else
        FreeDReceiver[] all = FindObjectsOfType<FreeDReceiver>();
#endif
        foreach (FreeDReceiver receiver in all)
        {
            if (receiver.isActiveAndEnabled)
            {
                _foundReceivers.Add(receiver);
            }
        }
    }

    /// <summary>同じ項目を 2 つの Receiver が担当していると、合成できない。</summary>
    private bool HasOverlappedFields()
    {
        var claimed = FreeDFields.None;
        foreach (FreeDReceiver receiver in _foundReceivers)
        {
            FreeDFields overlapped = claimed & receiver.Fields;
            if (overlapped != FreeDFields.None)
            {
                Debug.LogError("FreeD: 同じ項目を複数の Receiver が担当している: " + overlapped, this);
                return true;
            }
            claimed |= receiver.Fields;
        }
        return false;
    }

    private bool IsRestartNeeded()
    {
        return _isReceiverSetChanged || Signature() != _openedSignature;
    }

    /// <summary>ポート・項目・本数のどれかが変わると値が変わる。再生中の設定変更で受信をやり直す目印。</summary>
    private int Signature()
    {
        unchecked
        {
            int hash = _foundReceivers.Count;
            foreach (FreeDReceiver receiver in _foundReceivers)
            {
                hash = hash * 31 + receiver.port * 31 + (int)receiver.Fields;
            }
            return hash;
        }
    }

    // ---- 毎フレームの処理 ----

    /// <summary>各 Receiver が保持しているパケットを、届いた順にすべて取り出す。取り出すだけで、中身は使わない。</summary>
    private void PollReceivers()
    {
        _polledPackets.Clear();
        for (int receiverIndex = 0; receiverIndex < _openedReceivers.Count; receiverIndex++)
        {
            while (_openedReceivers[receiverIndex].TryDequeue(out FreeDReceivedPacket received))
            {
                _polledPackets.Add(new FreeDPolledPacket { receiverIndex = receiverIndex, received = received });
            }
        }
    }

    /// <summary>取り出したパケットを、カメラ ID ごとの Merger に渡す。</summary>
    private void PutPolledPacketsToMergers()
    {
        foreach (FreeDPolledPacket polled in _polledPackets)
        {
            FreeDMerger merger = GetOrCreateMerger(polled.received.packet.CameraId);
            merger.Put(polled.receiverIndex, polled.received.packet, polled.received.receivedMs);
        }
    }

    private FreeDMerger GetOrCreateMerger(int cameraId)
    {
        if (!_mergersByCameraId.TryGetValue(cameraId, out FreeDMerger merger))
        {
            merger = new FreeDMerger(FieldsOfOpenedReceivers());
            _mergersByCameraId[cameraId] = merger;
        }
        return merger;
    }

    private List<FreeDFields> FieldsOfOpenedReceivers()
    {
        var fields = new List<FreeDFields>();
        foreach (FreeDReceiver receiver in _openedReceivers)
        {
            fields.Add(receiver.Fields);
        }
        return fields;
    }

    /// <summary>カメラ ID ごとに、遅延を過ぎたパケットを合成して、Tracker が TryGet で読める値にする。</summary>
    private void MergeAll(double nowMs)
    {
        foreach (KeyValuePair<int, FreeDMerger> pair in _mergersByCameraId)
        {
            FreeDMerger merger = pair.Value;
            ApplyDelays(merger);
            FreeDFields filled = merger.Update(nowMs, out FreeDPacket packet);
            _mergedByCameraId[pair.Key] = new FreeDMergedCamera
            {
                filled = filled,
                packet = packet,
                isReceiving = IsAnyReceiverReceiving(merger, nowMs),
            };
        }
    }

    private void ApplyDelays(FreeDMerger merger)
    {
        for (int receiverIndex = 0; receiverIndex < _openedReceivers.Count; receiverIndex++)
        {
            merger.SetDelay(receiverIndex, _openedReceivers[receiverIndex].delayMs);
        }
    }

    private bool IsAnyReceiverReceiving(FreeDMerger merger, double nowMs)
    {
        for (int receiverIndex = 0; receiverIndex < _openedReceivers.Count; receiverIndex++)
        {
            if (merger.IsReceiving(receiverIndex, nowMs))
            {
                return true;
            }
        }
        return false;
    }

    // ---- 状態の集計 (Inspector に出す値) ----

    /// <summary>各 Receiver の受信状態から、全体の受信中と合計 pkt/s を集める。</summary>
    private void UpdateReceiverSummary()
    {
        receiving = false;
        totalPacketsPerSecond = 0;
        foreach (FreeDReceiver receiver in _openedReceivers)
        {
            receiving |= receiver.status.isReceiving;
            totalPacketsPerSecond += receiver.status.packetsPerSecond;
        }
    }

    private void UpdateCameraStatus()
    {
        foreach (KeyValuePair<int, FreeDMergedCamera> pair in _mergedByCameraId)
        {
            FreeDCameraStatus status = FindOrAddCameraStatus(pair.Key);
            status.isReceiving = pair.Value.isReceiving;
            if (status.lastFilled == pair.Value.filled)
            {
                continue;
            }
            status.lastFilled = pair.Value.filled;
            status.fields = pair.Value.filled.ToString();
        }
    }

    /// <summary>cameras はカメラ ID の昇順に保つ。</summary>
    private FreeDCameraStatus FindOrAddCameraStatus(int cameraId)
    {
        int insertIndex = 0;
        for (; insertIndex < cameras.Count; insertIndex++)
        {
            if (cameras[insertIndex].cameraId == cameraId)
            {
                return cameras[insertIndex];
            }
            if (cameras[insertIndex].cameraId > cameraId)
            {
                break;
            }
        }
        var status = new FreeDCameraStatus { cameraId = cameraId };
        cameras.Insert(insertIndex, status);
        return status;
    }
}
