using System;
using System.Collections.Generic;
using CIFER.tech.FreeD.Core;
using UnityEngine;

namespace CIFER.tech.FreeD.Unity
{
    [DisallowMultipleComponent]
    public sealed class FreeDManager : MonoBehaviour
    {
        private static FreeDManager instance;
        private static bool isQuitting;

        private readonly List<IFreeDReceiver> receivers = new();
        private readonly List<IFreeDTracker> trackers = new();
        private readonly FreeDMergedCamera[] mergedByCameraId = new FreeDMergedCamera[byte.MaxValue + 1];

        /// <summary>Receiver や Tracker の OnEnable が、FreeDManager の Awake より先に走っても登録できるよう、なければ作る。</summary>
        public static FreeDManager Instance
        {
            get
            {
                if (instance != null)
                {
                    return instance;
                }

                // 終了中に作ると、作ったものがシーンに残る。
                if (isQuitting)
                {
                    return null;
                }

                instance = FindExistingInstance();
                if (instance == null)
                {
                    instance = new GameObject(nameof(FreeDManager)).AddComponent<FreeDManager>();
                }

                return instance;
            }
        }

        /// <summary>解除のときに使う。なければ作らない (シーンを閉じるときに、作ってしまわないため)。</summary>
        public static FreeDManager ExistingInstance
        {
            get
            {
                return instance;
            }
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
            if (receivers.Remove(receiver))
            {
                receiver.Close();
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            // ドメインのリロードなしで再生すると、前の再生の値が残る。
            instance = null;
            isQuitting = false;
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        private static void OnApplicationQuitting()
        {
            isQuitting = true;
        }

        private static FreeDManager FindExistingInstance()
        {
#if UNITY_6000_5_OR_NEWER
            var found = FindObjectsByType<FreeDManager>(FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
            var found = FindObjectsByType<FreeDManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#else
            var found = FindObjectsOfType<FreeDManager>(true);
#endif
            return found.Length > 0 ? found[0] : null;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Debug.LogWarning("FreeD: FreeDManager が複数ある。あとのものを壊す", this);
                Destroy(this);
                return;
            }

            instance = this;

            // DontDestroyOnLoad は、ルートの GameObject にしか効かない。
            if (transform.parent != null)
            {
                Debug.LogWarning("FreeD: FreeDManager をルートの GameObject に置かないと、シーンの切り替えで壊れる", this);
                return;
            }

            DontDestroyOnLoad(gameObject);
        }

        private void OnEnable()
        {
            if (instance != this)
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
            if (instance != this)
            {
                return;
            }

            foreach (var receiver in receivers)
            {
                receiver.Close();
            }

            Array.Clear(mergedByCameraId, 0, mergedByCameraId.Length);
        }

        private void OnDestroy()
        {
            if (instance != this)
            {
                return;
            }

            receivers.Clear();
            trackers.Clear();
            instance = null;
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
