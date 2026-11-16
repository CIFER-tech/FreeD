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
        private readonly List<IFreeDProvider> providers = new();
        private readonly List<IFreeDConsumer> consumers = new();
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
            foreach (var provider in providers.ToArray())
            {
                if (provider != null && providers.Contains(provider))
                {
                    OpenProvider(provider);
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
                UpdateProviders();
                UpdateConsumers();
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

            foreach (var provider in providers.ToArray())
            {
                if (provider != null && providers.Contains(provider))
                {
                    provider.Close();
                }
            }

            Array.Clear(mergedByCameraId, 0, mergedByCameraId.Length);
        }

        protected override void OnDestroy()
        {
            providers.Clear();
            consumers.Clear();
            base.OnDestroy();
        }

        public void AddProvider(IFreeDProvider provider)
        {
            if (provider == null || providers.Contains(provider))
            {
                return;
            }

            providers.Add(provider);
            if (isActiveAndEnabled)
            {
                OpenProvider(provider);
            }
        }

        public void RemoveProvider(IFreeDProvider provider)
        {
            var index = providers.IndexOf(provider);
            if (index < 0)
            {
                return;
            }

            RemoveAt(providers, index);
            provider.Close();

            // FreeDManager はシーンを切り替えても残るので、担当がいなくなった項目の値を、反映し続けないようにする。
            var orphanedFields = provider.ClaimedFields & ~GetClaimedFields();
            for (var i = 0; i < mergedByCameraId.Length; i++)
            {
                mergedByCameraId[i].receivedFields &= ~orphanedFields;
            }
        }

        public void AddConsumer(IFreeDConsumer consumer)
        {
            if (consumer != null && !consumers.Contains(consumer))
            {
                consumers.Add(consumer);
            }
        }

        public void RemoveConsumer(IFreeDConsumer consumer)
        {
            var index = consumers.IndexOf(consumer);
            if (index >= 0)
            {
                RemoveAt(consumers, index);
            }
        }

        private void OpenProvider(IFreeDProvider provider)
        {
            // ポートを開けないプロバイダがあっても、残りのプロバイダは開く。
            try
            {
                provider.Open();
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

            for (var i = 0; i < providers.Count; i++)
            {
                if (providers[i] == null)
                {
                    continue;
                }

                var providerFields = providers[i].ClaimedFields;
                var overlappedFields = claimedFields & providerFields;
                if (overlappedFields != FreeDField.None)
                {
                    Debug.LogError("FreeD: 同じ項目を複数のプロバイダが担当している: " + overlappedFields, this);
                    isOverlapped = true;
                }

                claimedFields |= providerFields;
            }

            return isOverlapped;
        }

        private void UpdateProviders()
        {
            // Apply などの中で登録が変わっても壊れないよう、foreach ではなく for で回す。
            // 回している間に足されたものは、次のフレームから使う (自分を解除して登録し直す実装で、止まらないようにするため)。
            var providerCount = providers.Count;
            for (var i = 0; i < providerCount; i++)
            {
                var provider = providers[i];
                if (provider == null)
                {
                    continue;
                }

                // 1 つのポートに複数のカメラ ID が届くので、遅延を過ぎたパケットを古い順にすべて合成する。
                while (provider.TryTakeNext(out var packet))
                {
                    // TryTakeNext の中で解除されたら、解除で外した項目を戻さない。再登録されていても、そのフレームでは使わない。
                    if (providers[i] != provider)
                    {
                        break;
                    }

                    ref var merged = ref mergedByCameraId[packet.packet.CameraId];

                    merged.packet = FreeDMerger.Merge(merged.packet, ~provider.ClaimedFields, packet.packet, provider.ClaimedFields);

                    // Merge は項目だけを写すので、CameraId は 0 になる。自作のコンシューマが Apply の中で読めるよう、戻す。
                    merged.packet.CameraId = packet.packet.CameraId;
                    merged.receivedFields |= provider.ClaimedFields;
                }
            }
        }

        private void UpdateConsumers()
        {
            var claimedFields = GetClaimedFields();
            var consumerCount = consumers.Count;
            for (var i = 0; i < consumerCount; i++)
            {
                var consumer = consumers[i];
                if (consumer == null)
                {
                    continue;
                }

                var cameraId = consumer.CameraId;
                if ((uint)cameraId >= (uint)mergedByCameraId.Length)
                {
                    continue;
                }

                // 登録したまま設定を変えて、担当でなくなった項目は、前の値を反映し続けない。
                ref var merged = ref mergedByCameraId[cameraId];
                var appliedFields = merged.receivedFields & claimedFields;
                if (appliedFields != FreeDField.None)
                {
                    consumer.Apply(merged.packet, appliedFields);
                }
            }
        }

        private FreeDField GetClaimedFields()
        {
            var claimedFields = FreeDField.None;
            for (var i = 0; i < providers.Count; i++)
            {
                if (providers[i] != null)
                {
                    claimedFields |= providers[i].ClaimedFields;
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

            providers.RemoveAll(static provider => provider == null);
            consumers.RemoveAll(static consumer => consumer == null);
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
