using CIFER.tech.FreeD.Core;
using UnityEngine;
using UnityEngine.Serialization;

namespace CIFER.tech.FreeD.Unity
{
    /// <summary>受信の開始・停止とパケットの取り出しは FreeDManager が行うので、Unity のライフイベントでは、登録と設定の受け渡しだけをする。</summary>
    public sealed class FreeDReceiverForUnity : MonoBehaviour
    {
        [SerializeField] private int port = 40000;
        [SerializeField] private float delaySec = 0f;

        // 保存済みのシーンの値を失わないよう、元の名前 field を引き継ぐ。
        [FormerlySerializedAs("field")]
        [SerializeField]
        private FreeDField claimedFields = FreeDField.Position | FreeDField.Rotation | FreeDField.Zoom | FreeDField.Focus;

        private readonly FreeDReceiver receiver = new FreeDReceiver();

        public int Port
        {
            get => port;
            set => port = value;
        }

        public float DelaySec
        {
            get => delaySec;
            set => delaySec = value;
        }

        public FreeDField ClaimedFields
        {
            get => claimedFields;
            set => claimedFields = value;
        }

        private void OnEnable()
        {
            var manager = FreeDManager.Instance;
            if (manager == null)
            {
                return;
            }

            // 開いている間は設定を変えられないので、登録 (= Open) の前に渡す。インスペクタの変更は、次に有効になったときから効く。
            receiver.Close();
            receiver.Port = port;
            receiver.DelaySec = delaySec;
            receiver.ClaimedFields = claimedFields;
            manager.AddReceiver(receiver);
        }

        private void OnDisable()
        {
            var manager = FreeDManager.ExistingInstance;
            if (manager != null)
            {
                manager.RemoveReceiver(receiver);
            }

            // FreeDManager が先に壊れていても、ポートを開けたままにしない。
            receiver.Close();
        }
    }
}
