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
    private readonly FreeDReceiverGroup _receivers = new FreeDReceiverGroup();

    // 無効なものも含む。
    private readonly List<FreeDTracker> _sceneTrackers = new List<FreeDTracker>();

    private readonly Dictionary<int, FreeDMerger> _mergersByCameraId = new Dictionary<int, FreeDMerger>();
    // 値が 1 つも入っていない ID は入れない (Tracker に渡さない)。
    private readonly Dictionary<int, FreeDPacket> _mergedPacketsByCameraId = new Dictionary<int, FreeDPacket>();

    private void OnEnable()
    {
        StartReceiving();
    }

    private void OnDisable()
    {
        StopReceiving();
    }

    private void Update()
    {
        if (_receivers.IsSettingsChanged)
        {
            StopReceiving();
            StartReceiving();
        }

        for (int streamIndex = 0; streamIndex < _receivers.OpenedCount; streamIndex++)
        {
            while (_receivers.GetOpened(streamIndex).TryDequeue(out FreeDReceivedPacket received))
            {
                int cameraId = received.packet.CameraId;
                if (!_mergersByCameraId.TryGetValue(cameraId, out FreeDMerger merger))
                {
                    merger = new FreeDMerger(_receivers.OpenedFields);
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
            for (int streamIndex = 0; streamIndex < _receivers.OpenedCount; streamIndex++)
            {
                merger.SetDelay(streamIndex, _receivers.GetOpened(streamIndex).delayMs);
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

    private void StartReceiving()
    {
        _sceneTrackers.Clear();
#if UNITY_2022_2_OR_NEWER
        _sceneTrackers.AddRange(FindObjectsByType<FreeDTracker>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
#else
        _sceneTrackers.AddRange(FindObjectsOfType<FreeDTracker>(true));
#endif
        _receivers.Open(this);
    }

    private void StopReceiving()
    {
        _receivers.Close();
        _mergersByCameraId.Clear();
        _mergedPacketsByCameraId.Clear();
    }
}
