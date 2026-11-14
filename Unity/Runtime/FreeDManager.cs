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
    public List<FreeDReceiverStatus> receivers = new List<FreeDReceiverStatus>();
    public List<FreeDCameraStatus> cameras = new List<FreeDCameraStatus>();

    // 受信を開いた Receiver。FreeDMerger の streamIndex と receivers の添字は、この並びと同じ。
    private readonly List<FreeDReceiver> _openedReceivers = new List<FreeDReceiver>();
    private readonly Dictionary<int, FreeDMerger> _mergersByCameraId = new Dictionary<int, FreeDMerger>();
    private readonly Dictionary<int, FreeDMergedCamera> _mergedByCameraId = new Dictionary<int, FreeDMergedCamera>();
    // シーンから見つけた有効な Receiver。項目の重なりなどで開けなかったものも含む。
    private readonly List<FreeDReceiver> _foundReceivers = new List<FreeDReceiver>();
    private bool _isReceiverSetChanged;
    private int _openedSignature;
    private float _rateTimerSeconds;

    private void OnEnable()
    {
        Instance = this;
        OpenReceivers();
    }

    private void OnDisable()
    {
        CloseReceivers();
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void OnDestroy()
    {
        CloseReceivers();
    }

    private void Update()
    {
        if (_isReceiverSetChanged || Signature() != _openedSignature)
        {
            CloseReceivers();
            OpenReceivers();
        }

        double nowMs = FreeDClock.NowMs;
        PollReceivers();
        MergeAll(nowMs);
        PublishReceiverStatus(nowMs);
        PublishCameraStatus();
    }

    /// <summary>カメラ ID のデータを合成した値を取る。その ID のパケットがまだ 1 つも届いていないときは false。</summary>
    /// <param name="filled">1 度でも値が入った項目。含まれない項目は packet の中身を使ってはいけない。</param>
    public bool TryGet(int cameraId, out FreeDFields filled, out FreeDPacket packet, out bool isReceiving)
    {
        if (_mergedByCameraId.TryGetValue(cameraId, out FreeDMergedCamera merged))
        {
            filled = merged.filled; packet = merged.packet; isReceiving = merged.isReceiving;
            return true;
        }
        filled = FreeDFields.None; packet = default; isReceiving = false;
        return false;
    }

    /// <summary>FreeDReceiver が、有効・無効が変わったときに呼ぶ。次の Update で、受信を開き直す。</summary>
    internal void MarkReceiversChanged()
    {
        _isReceiverSetChanged = true;
    }

    private void OpenReceivers()
    {
        _isReceiverSetChanged = false;
        FindEnabledReceivers();
        _openedSignature = Signature();
        if (HasOverlappedFields())
        {
            return;
        }

        _openedReceivers.AddRange(_foundReceivers);
        foreach (FreeDReceiver r in _openedReceivers)
        {
            receivers.Add(new FreeDReceiverStatus { port = r.port });
            r.Open();
        }
    }

    private void FindEnabledReceivers()
    {
        _foundReceivers.Clear();
#if UNITY_2022_2_OR_NEWER
        FreeDReceiver[] all = FindObjectsByType<FreeDReceiver>(FindObjectsSortMode.InstanceID);
#else
        FreeDReceiver[] all = FindObjectsOfType<FreeDReceiver>();
#endif
        foreach (FreeDReceiver r in all)
        {
            if (r.isActiveAndEnabled)
            {
                _foundReceivers.Add(r);
            }
        }
    }

    /// <summary>同じ項目を 2 つの Receiver が担当していると、合成できない。</summary>
    private bool HasOverlappedFields()
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

    private void CloseReceivers()
    {
        foreach (FreeDReceiver r in _openedReceivers)
        {
            r.Close();
        }
        _openedReceivers.Clear();
        _mergersByCameraId.Clear();
        _mergedByCameraId.Clear();
        receivers.Clear();
        cameras.Clear();
        receiving = false;
        totalPacketsPerSecond = 0;
    }

    /// <summary>ポート・項目・本数のどれかが変わると値が変わる。再生中の設定変更で受信をやり直す目印。</summary>
    private int Signature()
    {
        unchecked
        {
            int hash = _foundReceivers.Count;
            foreach (FreeDReceiver r in _foundReceivers)
            {
                hash = hash * 31 + r.port * 31 + (int)r.Fields;
            }
            return hash;
        }
    }

    /// <summary>各 Receiver が保持しているパケットを、届いた順にすべて取り出して、カメラ ID ごとの Merger に渡す。</summary>
    private void PollReceivers()
    {
        for (int receiverIndex = 0; receiverIndex < _openedReceivers.Count; receiverIndex++)
        {
            while (_openedReceivers[receiverIndex].TryDequeue(out FreeDReceivedPacket received))
            {
                FreeDMerger merger = GetOrCreateMerger(received.packet.CameraId);
                merger.Put(receiverIndex, received.packet, received.receivedMs);
                receivers[receiverIndex].RecordPacket(received.receivedMs);
            }
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
        foreach (FreeDReceiver r in _openedReceivers)
        {
            fields.Add(r.Fields);
        }
        return fields;
    }

    private void MergeAll(double nowMs)
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
            _mergedByCameraId[pair.Key] = new FreeDMergedCamera { filled = filled, packet = packet, isReceiving = isReceiving };
        }
    }

    private void PublishReceiverStatus(double nowMs)
    {
        receiving = false;
        foreach (FreeDReceiverStatus status in receivers)
        {
            status.UpdateIsReceiving(nowMs);
            receiving |= status.isReceiving;
        }

        _rateTimerSeconds += Time.unscaledDeltaTime;
        if (_rateTimerSeconds < 1f)
        {
            return;
        }
        totalPacketsPerSecond = 0;
        foreach (FreeDReceiverStatus status in receivers)
        {
            status.PublishRates(_rateTimerSeconds);
            totalPacketsPerSecond += status.packetsPerSecond;
        }
        _rateTimerSeconds = 0f;
    }

    private void PublishCameraStatus()
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
