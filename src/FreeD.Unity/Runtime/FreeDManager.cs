using System;
using System.Collections.Generic;
using CIFER.tech.FreeD.Core;
using UnityEngine;

namespace CIFER.tech.FreeD.Unity
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class FreeDManager : SingletonBehaviour<FreeDManager>
    {
        private readonly List<IFreeDReceiver> receivers = new();
        private readonly List<IFreeDTracker> trackers = new();
        private readonly FreeDMergedCamera[] mergedByCameraId = new FreeDMergedCamera[byte.MaxValue + 1];

        // Update で回している間の解除は、要素を詰めると次の要素を飛ばすので、その場では null にして、回し終えてから詰める。
        private bool isUpdating;
        private bool hasRemovedWhileUpdating;

        private void OnEnable()
        {
            if (!IsSingletonInstance)
            {
                return;
            }

            // Open の中で登録が変わっても壊れないよう、写しを回す。まれにしか起きないので、確保は許す。
            foreach (var receiver in receivers.ToArray())
            {
                if (receiver != null && receivers.Contains(receiver))
                {
                    OpenReceiver(receiver);
                }
            }
        }

        private void Update()
        {
            if (HasOverlappedField())
            {
                return;
            }

            isUpdating = true;
            try
            {
                UpdateReceivers();
                UpdateTrackers();
            }
            finally
            {
                isUpdating = false;
                RemoveNullEntries();
            }
        }

        private void OnDisable()
        {
            if (!IsSingletonInstance)
            {
                return;
            }

            foreach (var receiver in receivers.ToArray())
            {
                if (receiver != null && receivers.Contains(receiver))
                {
                    receiver.Close();
                }
            }

            Array.Clear(mergedByCameraId, 0, mergedByCameraId.Length);
        }

        protected override void OnDestroy()
        {
            receivers.Clear();
            trackers.Clear();
            base.OnDestroy();
        }

        public void AddReceiver(IFreeDReceiver receiver)
        {
            if (receiver == null || receivers.Contains(receiver))
            {
                return;
            }

            receivers.Add(receiver);
            if (isActiveAndEnabled)
            {
                OpenReceiver(receiver);
            }
        }

        public void RemoveReceiver(IFreeDReceiver receiver)
        {
            var index = receivers.IndexOf(receiver);
            if (index < 0)
            {
                return;
            }

            RemoveAt(receivers, index);
            receiver.Close();

            // FreeDManager はシーンを切り替えても残るので、担当がいなくなった項目の値を、反映し続けないようにする。
            var orphanedFields = receiver.ClaimedFields & ~GetClaimedFields();
            for (var i = 0; i < mergedByCameraId.Length; i++)
            {
                mergedByCameraId[i].receivedFields &= ~orphanedFields;
            }
        }

        public void AddTracker(IFreeDTracker tracker)
        {
            if (tracker != null && !trackers.Contains(tracker))
            {
                trackers.Add(tracker);
            }
        }

        public void RemoveTracker(IFreeDTracker tracker)
        {
            var index = trackers.IndexOf(tracker);
            if (index >= 0)
            {
                RemoveAt(trackers, index);
            }
        }

        private void OpenReceiver(IFreeDReceiver receiver)
        {
            // ポートを開けない Receiver があっても、残りの Receiver は開く。
            try
            {
                receiver.Open();
            }
            catch (InvalidOperationException e)
            {
                Debug.LogException(e, this);
            }
        }

        /// <summary>同じ項目を 2 つのストリームが担当すると合成できないので、重なっていないかを調べる。</summary>
        private bool HasOverlappedField()
        {
            var claimedFields = FreeDField.None;
            var isOverlapped = false;

            for (var i = 0; i < receivers.Count; i++)
            {
                if (receivers[i] == null)
                {
                    continue;
                }

                var receiverFields = receivers[i].ClaimedFields;
                var overlappedFields = claimedFields & receiverFields;
                if (overlappedFields != FreeDField.None)
                {
                    Debug.LogError("FreeD: 同じ項目を複数の Receiver が担当している: " + overlappedFields, this);
                    isOverlapped = true;
                }

                claimedFields |= receiverFields;
            }

            return isOverlapped;
        }

        private void UpdateReceivers()
        {
            // Apply などの中で登録が変わっても壊れないよう、foreach ではなく for で回す。
            // 回している間に足されたものは、次のフレームから使う (自分を解除して登録し直す実装で、止まらないようにするため)。
            var receiverCount = receivers.Count;
            for (var i = 0; i < receiverCount; i++)
            {
                var receiver = receivers[i];
                if (receiver == null)
                {
                    continue;
                }

                // 1 つのポートに複数のカメラ ID が届くので、遅延を過ぎたパケットを古い順にすべて合成する。
                while (receiver.TryTakeNext(out var packet))
                {
                    // TryTakeNext の中で解除されたら、解除で外した項目を戻さない。再登録されていても、そのフレームでは使わない。
                    if (receivers[i] != receiver)
                    {
                        break;
                    }

                    ref var merged = ref mergedByCameraId[packet.packet.CameraId];

                    merged.packet = FreeDMerger.Merge(merged.packet, ~receiver.ClaimedFields, packet.packet, receiver.ClaimedFields);

                    // Merge は項目だけを写すので、CameraId は 0 になる。自作の Tracker が Apply の中で読めるよう、戻す。
                    merged.packet.CameraId = packet.packet.CameraId;
                    merged.receivedFields |= receiver.ClaimedFields;
                }
            }
        }

        private void UpdateTrackers()
        {
            var claimedFields = GetClaimedFields();
            var trackerCount = trackers.Count;
            for (var i = 0; i < trackerCount; i++)
            {
                var tracker = trackers[i];
                if (tracker == null)
                {
                    continue;
                }

                var cameraId = tracker.CameraId;
                if ((uint)cameraId >= (uint)mergedByCameraId.Length)
                {
                    continue;
                }

                // 登録したまま設定を変えて、担当でなくなった項目は、前の値を反映し続けない。
                ref var merged = ref mergedByCameraId[cameraId];
                var appliedFields = merged.receivedFields & claimedFields;
                if (appliedFields != FreeDField.None)
                {
                    tracker.Apply(merged.packet, appliedFields);
                }
            }
        }

        private FreeDField GetClaimedFields()
        {
            var claimedFields = FreeDField.None;
            for (var i = 0; i < receivers.Count; i++)
            {
                if (receivers[i] != null)
                {
                    claimedFields |= receivers[i].ClaimedFields;
                }
            }

            return claimedFields;
        }

        private void RemoveNullEntries()
        {
            if (!hasRemovedWhileUpdating)
            {
                return;
            }

            receivers.RemoveAll(static receiver => receiver == null);
            trackers.RemoveAll(static tracker => tracker == null);
            hasRemovedWhileUpdating = false;
        }

        private void RemoveAt<TItem>(List<TItem> items, int index) where TItem : class
        {
            if (!isUpdating)
            {
                items.RemoveAt(index);
                return;
            }

            items[index] = null;
            hasRemovedWhileUpdating = true;
        }
    }
}
