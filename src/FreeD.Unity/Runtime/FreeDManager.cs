using System;
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
#if UNITY_6000_5_OR_NEWER
            receivers.AddRange(FindObjectsByType<FreeDReceiverForUnity>(FindObjectsInactive.Include));
            trackers.AddRange(FindObjectsByType<FreeDTracker>(FindObjectsInactive.Include));
#elif UNITY_2022_2_OR_NEWER
            receivers.AddRange(FindObjectsByType<FreeDReceiverForUnity>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            trackers.AddRange(FindObjectsByType<FreeDTracker>(FindObjectsInactive.Include, FindObjectsSortMode.None));
#else
            receivers.AddRange(FindObjectsOfType<FreeDReceiverForUnity>(true));
            trackers.AddRange(FindObjectsOfType<FreeDTracker>(true));
#endif

            // 控えとして無効にしてある Receiver は、受信にも項目の重なりの検査にも使わない。
            receivers.RemoveAll(receiver => !receiver.isActiveAndEnabled);

            foreach (var receiver in receivers)
            {
                // ポートを開けない Receiver があっても、残りの Receiver は開く。
                try
                {
                    receiver.Open();
                }
                catch (InvalidOperationException e)
                {
                    Debug.LogException(e, receiver);
                }
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
            CloseDestroyedReceivers();

            if (HasOverlappedField(out var claimedFields))
            {
                return;
            }

            foreach (var receiver in receivers)
            {
                // 1 つのポートに複数のカメラ ID が届くので、遅延を過ぎたパケットを古い順にすべて合成する。
                while (receiver.TryTakeNext(out var packet))
                {
                    var cameraId = packet.packet.CameraId;
                    mergedPackets.TryGetValue(cameraId, out var mergedPacket);

                    // この Receiver の担当項目だけを新しい値にし、残りは今までの値を保つ。
                    mergedPackets[cameraId] =
                        FreeDMerger.Merge(mergedPacket, ~receiver.Field, packet.packet, receiver.Field);
                }
            }

            foreach (var tracker in trackers)
            {
                if (tracker == null || !tracker.isActiveAndEnabled)
                {
                    continue;
                }

                if (mergedPackets.TryGetValue(tracker.CameraId, out var mergedPacket))
                {
                    tracker.Apply(mergedPacket, claimedFields);
                }
            }
        }

        /// <summary>シーンのアンロードなどで壊された Receiver は、受信を止めて、一覧から外す。</summary>
        private void CloseDestroyedReceivers()
        {
            for (var i = receivers.Count - 1; i >= 0; i--)
            {
                var receiver = receivers[i];
                if (receiver != null)
                {
                    continue;
                }

                receiver.Close();
                receivers.RemoveAt(i);
            }
        }

        /// <summary>
        /// 同じ項目を 2 つのストリームが担当すると合成できないので、重なっていないかを調べる。
        /// claimedFields は、いずれかの Receiver が担当している項目。
        /// </summary>
        private bool HasOverlappedField(out FreeDField claimedFields)
        {
            claimedFields = FreeDField.None;
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