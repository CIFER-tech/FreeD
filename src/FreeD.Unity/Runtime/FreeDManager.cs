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
            if (!receivers.Remove(receiver))
            {
                return;
            }

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
            trackers.Remove(tracker);
        }

        private void OnEnable()
        {
            if (!IsSingletonInstance)
            {
                return;
            }

            foreach (var receiver in receivers)
            {
                OpenReceiver(receiver);
            }
        }

        private void OnDisable()
        {
            if (!IsSingletonInstance)
            {
                return;
            }

            foreach (var receiver in receivers)
            {
                receiver.Close();
            }

            Array.Clear(mergedByCameraId, 0, mergedByCameraId.Length);
        }

        protected override void OnDestroy()
        {
            receivers.Clear();
            trackers.Clear();
            base.OnDestroy();
        }

        private void Update()
        {
            if (HasOverlappedField())
            {
                return;
            }

            // Tracker の Apply の中で登録が変わっても壊れないよう、foreach ではなく for で回す。
            for (var i = 0; i < receivers.Count; i++)
            {
                var receiver = receivers[i];

                // 1 つのポートに複数のカメラ ID が届くので、遅延を過ぎたパケットを古い順にすべて合成する。
                while (receiver.TryTakeNext(out var packet))
                {
                    ref var merged = ref mergedByCameraId[packet.packet.CameraId];

                    merged.packet = FreeDMerger.Merge(merged.packet, ~receiver.ClaimedFields, packet.packet, receiver.ClaimedFields);
                    merged.receivedFields |= receiver.ClaimedFields;
                }
            }

            for (var i = 0; i < trackers.Count; i++)
            {
                var tracker = trackers[i];
                var cameraId = tracker.CameraId;
                if ((uint)cameraId >= (uint)mergedByCameraId.Length)
                {
                    continue;
                }

                ref var merged = ref mergedByCameraId[cameraId];
                if (merged.receivedFields != FreeDField.None)
                {
                    tracker.Apply(merged.packet, merged.receivedFields);
                }
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

        private FreeDField GetClaimedFields()
        {
            var claimedFields = FreeDField.None;
            for (var i = 0; i < receivers.Count; i++)
            {
                claimedFields |= receivers[i].ClaimedFields;
            }

            return claimedFields;
        }

        /// <summary>同じ項目を 2 つのストリームが担当すると合成できないので、重なっていないかを調べる。</summary>
        private bool HasOverlappedField()
        {
            var claimedFields = FreeDField.None;
            var isOverlapped = false;

            for (var i = 0; i < receivers.Count; i++)
            {
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
    }
}
