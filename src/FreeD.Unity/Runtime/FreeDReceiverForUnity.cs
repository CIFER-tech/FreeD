using System;
using CIFER.tech.FreeD.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace CIFER.tech.FreeD.Unity
{
    /// <summary>
    /// FreeDReceiver は sealed で、MonoBehaviour とは同時に継承できないので、IFreeDProvider を実装して FreeDReceiver に委譲する。
    /// 受信の開始・停止とパケットの取り出しは FreeDManager が行うので、Unity のライフイベントでは、登録だけをする。
    /// </summary>
    public sealed class FreeDReceiverForUnity : MonoBehaviour, IFreeDProvider
    {
        [SerializeField] private int port = 40000;
        [SerializeField] private float delaySec = 0f;

        // 保存済みのシーンの値を失わないよう、元の名前 field を引き継ぐ。
        [FormerlySerializedAs("field")]
        [SerializeField]
        private FreeDField claimedFields = FreeDField.Position | FreeDField.Rotation | FreeDField.Zoom | FreeDField.Focus;

        private readonly FreeDReceiver receiver = new FreeDReceiver();

        // Instance は、なければ作るので、解除では使えない (シーンを閉じるときに、作ってしまう)。登録した相手を覚えておく。
        private FreeDManager manager;

        private bool isOpen;

        public int Port
        {
            get => port;
            set
            {
                ThrowIfOpen();
                port = value;
            }
        }

        public float DelaySec
        {
            get => delaySec;
            set
            {
                ThrowIfOpen();
                delaySec = value;
            }
        }

        public FreeDField ClaimedFields
        {
            get => claimedFields;
            set
            {
                ThrowIfOpen();
                claimedFields = value;
            }
        }

        private void OnEnable()
        {
            manager = FreeDManager.Instance;
            manager.AddProvider(this);
        }

        private void OnDisable()
        {
            if (manager != null)
            {
                manager.RemoveProvider(this);
                manager = null;
            }

            // FreeDManager が先に壊れていても、ポートを開けたままにしない。
            Close();
        }

        public void Open()
        {
            // 開いている間は設定を変えられないので、Open の前に渡す。インスペクタの変更は、次に開くときから効く。
            Close();
            receiver.Port = port;
            receiver.DelaySec = delaySec;
            receiver.ClaimedFields = claimedFields;
            receiver.Open();
            isOpen = true;
        }

        public void Close()
        {
            receiver.Close();
            isOpen = false;
        }

        public bool TryTakeNext(out FreeDProvidedPacket provided)
        {
            return receiver.TryTakeNext(out provided);
        }

        private void ThrowIfOpen()
        {
            if (isOpen)
            {
                throw new InvalidOperationException("FreeD: 開いている間は変えられない。Close してから変える");
            }
        }
    }
}
