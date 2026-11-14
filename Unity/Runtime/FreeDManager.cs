using System.Collections.Generic;
using FreeD;
using UnityEngine;

/// <summary>
/// MVP の Presenter。シーンに 1 つ置く。FreeDReceiver (Model) と FreeDTracker (View) の寿命と毎フレームの処理をすべて持つ。
/// 毎フレーム、各 Receiver のパケットを取り出し、カメラ ID ごとに FreeDMerger で合成して、その ID の Tracker に反映する。
/// FreeDManager がなければ、受信も反映も行われない。
/// Receiver と Tracker は受信を始めるときにシーンから探す。再生中に新しく置いたものは、再生を始め直すまで対象にならない。
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class FreeDManager : MonoBehaviour
{
    // 無効なものも含む。あとで有効になったら、受信を開き直す。
    private readonly List<FreeDReceiver> _sceneReceivers = new List<FreeDReceiver>();
    private readonly List<FreeDTracker> _sceneTrackers = new List<FreeDTracker>();

    // 受信を開いた Receiver と、開いたときの使う項目。添字が FreeDMerger のストリームの番号になる。
    private readonly List<FreeDReceiver> _openedReceivers = new List<FreeDReceiver>();
    private readonly List<FreeDFields> _fieldsPerStream = new List<FreeDFields>();

    private readonly Dictionary<int, FreeDMerger> _mergersByCameraId = new Dictionary<int, FreeDMerger>();
    // 値が 1 つも入っていない ID は入れない (Tracker に渡さない)。
    private readonly Dictionary<int, FreeDPacket> _mergedPacketsByCameraId = new Dictionary<int, FreeDPacket>();

    private int _openedSettingsHash;

    private void OnEnable()
    {
        OpenReceivers();
    }

    private void OnDisable()
    {
        CloseReceivers();
    }

    private void Update()
    {
        if (ReceiverSettingsHash() != _openedSettingsHash)
        {
            CloseReceivers();
            OpenReceivers();
        }

        for (int streamIndex = 0; streamIndex < _openedReceivers.Count; streamIndex++)
        {
            while (_openedReceivers[streamIndex].TryDequeue(out FreeDReceivedPacket received))
            {
                int cameraId = received.packet.CameraId;
                if (!_mergersByCameraId.TryGetValue(cameraId, out FreeDMerger merger))
                {
                    merger = new FreeDMerger(_fieldsPerStream);
                    _mergersByCameraId.Add(cameraId, merger);
                }
                merger.Put(streamIndex, received.packet, received.receivedMs);
            }
        }

        double nowMs = FreeDClock.NowMs;
        foreach (KeyValuePair<int, FreeDMerger> cameraIdAndMerger in _mergersByCameraId)
        {
            FreeDMerger merger = cameraIdAndMerger.Value;
            // delayMs は再生中に Inspector で変えられるので、毎フレーム渡す。
            for (int streamIndex = 0; streamIndex < _openedReceivers.Count; streamIndex++)
            {
                merger.SetDelay(streamIndex, _openedReceivers[streamIndex].delayMs);
            }
            FreeDFields filledFields = merger.Update(nowMs, out FreeDPacket merged);
            if (filledFields != FreeDFields.None)
            {
                _mergedPacketsByCameraId[cameraIdAndMerger.Key] = merged;
            }
        }

        foreach (FreeDTracker tracker in _sceneTrackers)
        {
            if (tracker == null || !tracker.isActiveAndEnabled)
            {
                continue;
            }
            if (_mergedPacketsByCameraId.TryGetValue(tracker.cameraId, out FreeDPacket merged))
            {
                tracker.Apply(merged);
            }
        }
    }

    private void OpenReceivers()
    {
        _sceneReceivers.Clear();
        _sceneTrackers.Clear();
#if UNITY_2022_2_OR_NEWER
        _sceneReceivers.AddRange(FindObjectsByType<FreeDReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
        _sceneTrackers.AddRange(FindObjectsByType<FreeDTracker>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
#else
        _sceneReceivers.AddRange(FindObjectsOfType<FreeDReceiver>(true));
        _sceneTrackers.AddRange(FindObjectsOfType<FreeDTracker>(true));
#endif
        // 項目が重なって受信を始めないときも記録する。設定が直るまで、開き直さない。
        _openedSettingsHash = ReceiverSettingsHash();

        // FreeDMerger は同じ項目を 2 つのストリームが担当すると合成できないので、重なっていたら 1 つも開かない。
        var claimedFields = FreeDFields.None;
        foreach (FreeDReceiver receiver in _sceneReceivers)
        {
            if (!receiver.isActiveAndEnabled)
            {
                continue;
            }
            FreeDFields overlappedFields = claimedFields & receiver.UsedFields;
            if (overlappedFields != FreeDFields.None)
            {
                Debug.LogError("FreeD: 同じ項目を複数の Receiver が担当している: " + overlappedFields, this);
                _openedReceivers.Clear();
                _fieldsPerStream.Clear();
                return;
            }
            claimedFields |= receiver.UsedFields;
            _openedReceivers.Add(receiver);
            _fieldsPerStream.Add(receiver.UsedFields);
        }

        foreach (FreeDReceiver receiver in _openedReceivers)
        {
            receiver.Open();
        }
    }

    private void CloseReceivers()
    {
        foreach (FreeDReceiver receiver in _openedReceivers)
        {
            receiver.Close();
        }
        _openedReceivers.Clear();
        _fieldsPerStream.Clear();
        _mergersByCameraId.Clear();
        _mergedPacketsByCameraId.Clear();
    }

    /// <summary>Receiver の数・ポート・使う項目・有効か無効かのどれかが変わると、値が変わる。</summary>
    private int ReceiverSettingsHash()
    {
        unchecked
        {
            int hash = _sceneReceivers.Count;
            foreach (FreeDReceiver receiver in _sceneReceivers)
            {
                // 再生中に消された Receiver も、変化として扱う (開き直すときに、シーンから探し直す)。
                bool isOpenable = receiver != null && receiver.isActiveAndEnabled;
                hash = hash * 31 + receiver.port * 31 + (int)receiver.UsedFields;
                hash = hash * 31 + (isOpenable ? 1 : 0);
            }
            return hash;
        }
    }
}
