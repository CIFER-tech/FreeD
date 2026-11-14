using System.Collections.Generic;
using FreeD;
using UnityEngine;

/// <summary>
/// シーンの FreeDReceiver をまとめて管理する。FreeDManager が持つ。
/// シーンから Receiver を探し、有効なものの受信を開始・停止し、有効・無効やポート・使う項目の変化を見つける。
/// </summary>
internal class FreeDReceiverGroup
{
    // 無効なものも含む。あとで有効になったら、受信を開き直す。
    private readonly List<FreeDReceiver> _sceneReceivers = new List<FreeDReceiver>();

    // 受信を開いた Receiver と、開いたときの使う項目。添字が FreeDMerger のストリームの番号になる。
    private readonly List<FreeDReceiver> _openedReceivers = new List<FreeDReceiver>();
    private readonly List<FreeDFields> _openedFields = new List<FreeDFields>();

    private int _openedSettingsHash;

    public int OpenedCount
    {
        get
        {
            return _openedReceivers.Count;
        }
    }

    /// <summary>受信を開いたときの、Receiver ごとの使う項目。FreeDMerger を作るときに渡す。</summary>
    public IReadOnlyList<FreeDFields> OpenedFields
    {
        get
        {
            return _openedFields;
        }
    }

    /// <summary>受信を開いてから、Receiver の数・ポート・使う項目・有効か無効かのどれかが変わったか。</summary>
    public bool IsSettingsChanged
    {
        get
        {
            return SettingsHash() != _openedSettingsHash;
        }
    }

    public FreeDReceiver GetOpened(int streamIndex)
    {
        return _openedReceivers[streamIndex];
    }

    /// <param name="logContext">エラーを Console に出すときに、選ばれるオブジェクト。</param>
    public void Open(Object logContext)
    {
        _sceneReceivers.Clear();
#if UNITY_2022_2_OR_NEWER
        _sceneReceivers.AddRange(Object.FindObjectsByType<FreeDReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID));
#else
        _sceneReceivers.AddRange(Object.FindObjectsOfType<FreeDReceiver>(true));
#endif
        // 項目が重なって受信を始めないときも記録する。設定が直るまで、開き直さない。
        _openedSettingsHash = SettingsHash();

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
                Debug.LogError("FreeD: 同じ項目を複数の Receiver が担当している: " + overlappedFields, logContext);
                _openedReceivers.Clear();
                _openedFields.Clear();
                return;
            }

            claimedFields |= receiver.UsedFields;
            _openedReceivers.Add(receiver);
            _openedFields.Add(receiver.UsedFields);
        }

        foreach (FreeDReceiver receiver in _openedReceivers)
        {
            receiver.Open();
        }
    }

    public void Close()
    {
        foreach (FreeDReceiver receiver in _openedReceivers)
        {
            receiver.Close();
        }

        _openedReceivers.Clear();
        _openedFields.Clear();
    }

    private int SettingsHash()
    {
        unchecked
        {
            int hash = _sceneReceivers.Count;
            foreach (FreeDReceiver receiver in _sceneReceivers)
            {
                // 再生中に消された Receiver も、変化として扱う (開き直すときに、シーンから探し直す)。
                if (receiver == null)
                {
                    hash = hash * 31 + 1;
                    continue;
                }

                hash = hash * 31 + receiver.port * 31 + (int)receiver.UsedFields;
                hash = hash * 31 + (receiver.isActiveAndEnabled ? 3 : 2);
            }

            return hash;
        }
    }
}
