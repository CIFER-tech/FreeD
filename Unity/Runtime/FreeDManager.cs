using System.Collections.Generic;
using FreeD;
using UnityEngine;

/// <summary>
/// MVP の Presenter。シーンに 1 つ置く。FreeDReceiver (Model) と FreeDTracker (View) の寿命と毎フレームの処理をすべて持つ。
/// 毎フレーム、各 Receiver のパケットを取り出し、カメラ ID ごとに FreeDMerger で合成して、その ID の Tracker に反映する。
/// FreeDManager がなければ、受信も反映も行われない。
/// Receiver と Tracker は、有効か無効かを自分で持つ (Unity の enabled)。Manager は毎フレーム、それを読む。
/// Receiver と Tracker は、Manager が有効になったときにシーンから探す。再生中に新しく置いたものは、再生を始め直すまで対象にならない。
/// </summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public class FreeDManager : MonoBehaviour
{
    // 無効なものも含む。
    private readonly List<FreeDReceiver> _receivers = new List<FreeDReceiver>();
    private readonly List<FreeDTracker> _trackers = new List<FreeDTracker>();

    // Receiver ごとの、いま担当している項目。無効な Receiver は None。添字が FreeDMerger のストリームの番号になる。
    private readonly List<FreeDFields> _fieldsPerStream = new List<FreeDFields>();
    private bool _isFieldsOverlapped;

    private readonly Dictionary<int, FreeDMerger> _mergersByCameraId = new Dictionary<int, FreeDMerger>();
    // 値が 1 つも入っていない ID は入れない (Tracker に渡さない)。
    private readonly Dictionary<int, FreeDPacket> _mergedPacketsByCameraId = new Dictionary<int, FreeDPacket>();

    private void OnEnable()
    {
        _receivers.Clear();
        _trackers.Clear();
#if UNITY_2022_2_OR_NEWER
        _receivers.AddRange(FindObjectsByType<FreeDReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
        _trackers.AddRange(FindObjectsByType<FreeDTracker>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
#else
        _receivers.AddRange(FindObjectsOfType<FreeDReceiver>(true));
        _trackers.AddRange(FindObjectsOfType<FreeDTracker>(true));
#endif
        _fieldsPerStream.Clear();
        for (int streamIndex = 0; streamIndex < _receivers.Count; streamIndex++)
        {
            _fieldsPerStream.Add(FreeDFields.None);
        }
    }

    private void OnDisable()
    {
        // 再生中に消された Receiver も、ソケットを閉じる (Close は C# の項目だけを使うので、消えたあとでも呼べる)。
        foreach (FreeDReceiver receiver in _receivers)
        {
            receiver.Close();
        }
        _mergersByCameraId.Clear();
        _mergedPacketsByCameraId.Clear();
    }

    private void Update()
    {
        // 担当する項目が変わったら (有効・無効の切り替えを含む)、合成をやり直す。
        bool isFieldsChanged = false;
        for (int streamIndex = 0; streamIndex < _receivers.Count; streamIndex++)
        {
            FreeDReceiver receiver = _receivers[streamIndex];
            FreeDFields usedFields = FreeDFields.None;
            if (receiver == null)
            {
                receiver.Close();
            }
            else
            {
                receiver.SyncSocket();
                usedFields = receiver.isActiveAndEnabled ? receiver.UsedFields : FreeDFields.None;
            }

            if (_fieldsPerStream[streamIndex] != usedFields)
            {
                _fieldsPerStream[streamIndex] = usedFields;
                isFieldsChanged = true;
            }
        }

        if (isFieldsChanged)
        {
            _mergersByCameraId.Clear();
            _mergedPacketsByCameraId.Clear();

            // FreeDMerger は同じ項目を 2 つのストリームが担当すると合成できないので、重なっていたら合成しない。
            var claimedFields = FreeDFields.None;
            _isFieldsOverlapped = false;
            foreach (FreeDFields usedFields in _fieldsPerStream)
            {
                if ((claimedFields & usedFields) != FreeDFields.None)
                {
                    Debug.LogError("FreeD: 同じ項目を複数の Receiver が担当している: " + (claimedFields & usedFields), this);
                    _isFieldsOverlapped = true;
                }

                claimedFields |= usedFields;
            }
        }

        if (_isFieldsOverlapped)
        {
            return;
        }

        for (int streamIndex = 0; streamIndex < _receivers.Count; streamIndex++)
        {
            while (_receivers[streamIndex].TryDequeue(out FreeDReceivedPacket received))
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
            for (int streamIndex = 0; streamIndex < _receivers.Count; streamIndex++)
            {
                merger.SetDelay(streamIndex, _receivers[streamIndex].delayMs);
            }

            FreeDFields filledFields = merger.Update(nowMs, out FreeDPacket merged);
            if (filledFields != FreeDFields.None)
            {
                _mergedPacketsByCameraId[cameraIdAndMerger.Key] = merged;
            }
        }

        foreach (FreeDTracker tracker in _trackers)
        {
            if (tracker != null && tracker.isActiveAndEnabled && _mergedPacketsByCameraId.TryGetValue(tracker.cameraId, out FreeDPacket merged))
            {
                tracker.Apply(merged);
            }
        }
    }
}
