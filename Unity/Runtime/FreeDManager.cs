using System.Collections.Generic;
using FreeD;
using UnityEngine;

/// <summary>
/// FreeD 受信を管理する。シーンに 1 つ置く。
/// 有効な FreeDReceiver の受信を開始・停止し、毎フレーム各 Receiver のパケットを取りに行って、カメラ ID ごとに FreeDMerger で合成して、
/// FreeDTracker に反映する。
/// MVP の Presenter。Model (FreeDReceiver) と View (FreeDTracker、FreeDLens) は、互いを知らず、寿命と毎フレームの処理をすべてここで行う
/// (Receiver と Tracker は Unity の Update などを使わない)。
/// FreeDManager がなければ、受信も反映も行われない。
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class FreeDManager : MonoBehaviour
{
    // 状態の欄は FreeDManagerEditor が枠で囲んで描く (Header は付けない)
    [FreeDReadOnly] public bool receiving;
    [Tooltip("全 Receiver が直近 1 秒に受けたパケットの合計"), FreeDReadOnly]
    public int totalPacketsPerSecond;
    public List<FreeDCameraStatus> cameras = new List<FreeDCameraStatus>();

    // 受信を開いた Receiver。FreeDMerger の streamIndex は、この並びの添字。
    private readonly List<FreeDReceiver> _openedReceivers = new List<FreeDReceiver>();
    private readonly Dictionary<int, FreeDMerger> _mergersByCameraId = new Dictionary<int, FreeDMerger>();
    // カメラ ID ごとの、合成済みのパケット。値が 1 つも入っていない ID は入れない。
    private readonly Dictionary<int, FreeDPacket> _mergedPacketsByCameraId = new Dictionary<int, FreeDPacket>();
    // シーンから見つけた Receiver (無効なものも含む)。有効・無効の切り替えを、Signature で見つける。
    private readonly List<FreeDReceiver> _foundReceivers = new List<FreeDReceiver>();
    private readonly List<FreeDTracker> _foundTrackers = new List<FreeDTracker>();
    private int _openedSignature;

    private void OnEnable()
    {
        StartReceiving();
    }

    private void OnDisable()
    {
        StopReceiving();
    }

    private void OnDestroy()
    {
        StopReceiving();
    }

    private void Update()
    {
        if (Signature() != _openedSignature)
        {
            StopReceiving();
            StartReceiving();
        }

        PollReceivers();
        MergeAll(FreeDClock.NowMs);

        receiving = false;
        totalPacketsPerSecond = 0;
        foreach (FreeDReceiver receiver in _openedReceivers)
        {
            receiver.status.Update(FreeDClock.NowMs, Time.unscaledDeltaTime);
            receiving |= receiver.status.isReceiving;
            totalPacketsPerSecond += receiver.status.packetsPerSecond;
        }

        foreach (FreeDTracker tracker in _foundTrackers)
        {
            if (tracker == null || !tracker.isActiveAndEnabled)
            {
                continue;
            }
            if (_mergedPacketsByCameraId.TryGetValue(tracker.cameraId, out FreeDPacket merged))
            {
                tracker.Apply(merged, FindOrAddCameraStatus(tracker.cameraId).isReceiving);
            }
            else
            {
                tracker.receiving = false;
            }
        }
    }

    private void StartReceiving()
    {
        FindReceiversAndTrackers();
        _openedSignature = Signature();
        if (HasOverlappedFields())
        {
            return;
        }

        foreach (FreeDReceiver receiver in _foundReceivers)
        {
            if (!receiver.isActiveAndEnabled)
            {
                continue;
            }
            _openedReceivers.Add(receiver);
            receiver.Open();
        }
    }

    private void StopReceiving()
    {
        foreach (FreeDReceiver receiver in _openedReceivers)
        {
            receiver.Close();
        }
        _openedReceivers.Clear();
        _mergersByCameraId.Clear();
        _mergedPacketsByCameraId.Clear();
        cameras.Clear();
        receiving = false;
        totalPacketsPerSecond = 0;
    }

    /// <summary>シーンの Receiver と Tracker を探す。無効なものも含める (あとで有効になったら、受信を開き直す)。</summary>
    private void FindReceiversAndTrackers()
    {
        _foundReceivers.Clear();
        _foundTrackers.Clear();
#if UNITY_2022_2_OR_NEWER
        _foundReceivers.AddRange(FindObjectsByType<FreeDReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
        _foundTrackers.AddRange(FindObjectsByType<FreeDTracker>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
#else
        _foundReceivers.AddRange(FindObjectsOfType<FreeDReceiver>(true));
        _foundTrackers.AddRange(FindObjectsOfType<FreeDTracker>(true));
#endif
        foreach (FreeDTracker tracker in _foundTrackers)
        {
            tracker.lens = tracker.GetComponent<FreeDLens>();
        }
    }

    /// <summary>同じ項目を 2 つの Receiver が担当していると、合成できない。</summary>
    private bool HasOverlappedFields()
    {
        var claimed = FreeDFields.None;
        foreach (FreeDReceiver receiver in _foundReceivers)
        {
            if (!receiver.isActiveAndEnabled)
            {
                continue;
            }
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

    /// <summary>ポート・項目・本数・有効か無効かのどれかが変わると値が変わる。再生中の設定変更で受信をやり直す目印。</summary>
    private int Signature()
    {
        unchecked
        {
            int hash = _foundReceivers.Count;
            foreach (FreeDReceiver receiver in _foundReceivers)
            {
                hash = hash * 31 + receiver.port * 31 + (int)receiver.Fields;
                hash = hash * 31 + (receiver.isActiveAndEnabled ? 1 : 0);
            }
            return hash;
        }
    }

    /// <summary>各 Receiver が保持しているパケットを、届いた順にすべて取り出して、カメラ ID ごとの Merger に渡す。</summary>
    private void PollReceivers()
    {
        for (var i = 0; i < _openedReceivers.Count; i++)
        {
            while (_openedReceivers[i].TryDequeue(out var received))
            {
                int cameraId = received.packet.CameraId;
                if (!_mergersByCameraId.TryGetValue(cameraId, out FreeDMerger merger))
                {
                    var fields = new List<FreeDFields>();
                    foreach (FreeDReceiver receiver in _openedReceivers)
                    {
                        fields.Add(receiver.Fields);
                    }
                    merger = new FreeDMerger(fields);
                    _mergersByCameraId[cameraId] = merger;
                }
                merger.Put(i, received.packet, received.receivedMs);
            }
        }
    }

    /// <summary>カメラ ID ごとに、遅延を過ぎたパケットを合成して、Tracker に渡す値にする。Inspector に出す状態も更新する。</summary>
    private void MergeAll(double nowMs)
    {
        foreach (KeyValuePair<int, FreeDMerger> pair in _mergersByCameraId)
        {
            FreeDMerger merger = pair.Value;
            bool isReceiving = false;
            for (int receiverIndex = 0; receiverIndex < _openedReceivers.Count; receiverIndex++)
            {
                merger.SetDelay(receiverIndex, _openedReceivers[receiverIndex].delayMs);
                isReceiving |= merger.IsReceiving(receiverIndex, nowMs);
            }
            FreeDFields filled = merger.Update(nowMs, out FreeDPacket packet);
            if (filled != FreeDFields.None)
            {
                _mergedPacketsByCameraId[pair.Key] = packet;
            }

            FreeDCameraStatus status = FindOrAddCameraStatus(pair.Key);
            status.isReceiving = isReceiving;
            if (status.lastFilled != filled)
            {
                status.lastFilled = filled;
                status.fields = filled.ToString();
            }
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
