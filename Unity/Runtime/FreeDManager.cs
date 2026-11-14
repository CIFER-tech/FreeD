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

    private bool _isFieldsOverlapped;

    private readonly Dictionary<int, FreeDMerger> _mergersByCameraId = new Dictionary<int, FreeDMerger>();

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
    }

    private void OnDisable()
    {
        // 再生中に消された Receiver も、ソケットを閉じる (Close は C# の項目だけを使うので、消えたあとでも呼べる)。
        foreach (FreeDReceiver receiver in _receivers)
        {
            receiver.Close();
        }
        _mergersByCameraId.Clear();
    }

    private void Update()
    {
        // 担当する項目が変わったら (有効・無効の切り替えを含む)、合成をやり直す。
        bool isFieldsChanged = false;
        for (int streamIndex = 0; streamIndex < _receivers.Count; streamIndex++)
        {
            FreeDReceiver receiver = _receivers[streamIndex];
            if (receiver == null)
            {
                // 再生中に消された Receiver。ソケットを閉じて、一覧から外す (ストリームの番号が変わるので、合成をやり直す)。
                receiver.Close();
                _receivers.RemoveAt(streamIndex);
                streamIndex--;
                isFieldsChanged = true;
                continue;
            }

            isFieldsChanged |= receiver.Sync();
        }

        if (isFieldsChanged)
        {
            _mergersByCameraId.Clear();

            // FreeDMerger は同じ項目を 2 つのストリームが担当すると合成できないので、重なっていたら合成しない。
            var claimedFields = FreeDFields.None;
            _isFieldsOverlapped = false;
            foreach (FreeDReceiver receiver in _receivers)
            {
                FreeDFields overlappedFields = claimedFields & receiver.ActiveFields;
                if (overlappedFields != FreeDFields.None)
                {
                    Debug.LogError("FreeD: 同じ項目を複数の Receiver が担当している: " + overlappedFields, this);
                    _isFieldsOverlapped = true;
                }

                claimedFields |= receiver.ActiveFields;
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
                    var activeFieldsPerStream = new List<FreeDFields>();
                    foreach (FreeDReceiver receiver in _receivers)
                    {
                        activeFieldsPerStream.Add(receiver.ActiveFields);
                    }

                    merger = new FreeDMerger(activeFieldsPerStream);
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

            // 値が 1 つも入っていない ID は、Tracker に渡さない。
            FreeDFields filledFields = merger.Update(nowMs, out FreeDPacket merged);
            if (filledFields == FreeDFields.None)
            {
                continue;
            }

            foreach (FreeDTracker tracker in _trackers)
            {
                if (tracker != null && tracker.isActiveAndEnabled && tracker.cameraId == cameraIdAndMerger.Key)
                {
                    tracker.Apply(merged);
                }
            }
        }
    }
}
