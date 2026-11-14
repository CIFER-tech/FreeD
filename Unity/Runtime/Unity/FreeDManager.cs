using System.Collections.Generic;
using CIFER.tech.FreeD.Core;
using UnityEngine;

namespace CIFER.tech.FreeD.Unity
{
    [DisallowMultipleComponent]
    public sealed class FreeDManager : MonoBehaviour
    {
        private readonly List<FreeDReceiverForUnity> receivers = new();
        private readonly List<FreeDTracker> trackers = new();
        private readonly Dictionary<int, FreeDPacket> mergedPackets = new();

        private void OnEnable()
        {
#if UNITY_6000_0_OR_NEWER
            receivers.AddRange(FindObjectsByType<FreeDReceiverForUnity>(FindObjectsInactive.Include));
            trackers.AddRange(FindObjectsByType<FreeDTracker>(FindObjectsInactive.Include));
#elif UNITY_2022_2_OR_NEWER
            receivers.AddRange(FindObjectsByType<FreeDReceiverForUnity>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            trackers.AddRange(FindObjectsByType<FreeDTracker>(FindObjectsInactive.Include, FindObjectsSortMode.None));
#else
            receivers.AddRange(FindObjectsOfType<FreeDReceiverForUnity>(true));
            trackers.AddRange(FindObjectsOfType<FreeDTracker>(true));
#endif

            foreach (var receiver in receivers)
            {
                receiver.Open();
            }
        }

        private void OnDisable()
        {
            foreach (var receiver in receivers)
            {
                receiver.Close();
            }

            mergedPackets.Clear();
            receivers.Clear();
            trackers.Clear();
        }

        private void Update()
        {
            if (HasOverlappedField())
            {
                return;
            }

            foreach (var receiver in receivers)
            {
                if (!receiver.TryTakeLatest(out var packet))
                {
                    continue;
                }

                var cameraId = packet.packet.CameraId;
                mergedPackets.TryGetValue(cameraId, out var mergedPacket);

                // この Receiver の担当項目だけを新しい値にし、残りは今までの値を保つ。
                mergedPackets[cameraId] =
                    FreeDMerger.Merge(mergedPacket, ~receiver.Field, packet.packet, receiver.Field);
            }

            foreach (var tracker in trackers)
            {
                if (tracker == null || !tracker.isActiveAndEnabled)
                {
                    continue;
                }

                if (mergedPackets.TryGetValue(tracker.CameraId, out var mergedPacket))
                {
                    tracker.Apply(mergedPacket);
                }
            }
        }

        /// <summary>同じ項目を 2 つのストリームが担当すると合成できないので、重なっていないかを調べる。</summary>
        private bool HasOverlappedField()
        {
            var claimedFields = FreeDField.None;
            var isOverlapped = false;

            foreach (var receiver in receivers)
            {
                var overlappedFields = claimedFields & receiver.Field;
                if (overlappedFields != FreeDField.None)
                {
                    Debug.LogError("FreeD: 同じ項目を複数の Receiver が担当している: " + overlappedFields, this);
                    isOverlapped = true;
                }

                claimedFields |= receiver.Field;
            }

            return isOverlapped;
        }
    }
}