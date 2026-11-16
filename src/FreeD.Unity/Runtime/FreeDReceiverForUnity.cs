using CIFER.tech.FreeD.Core;
using UnityEngine;

namespace CIFER.tech.FreeD.Unity
{
    /// <summary>受信の開始・停止とパケットの取り出しは FreeDManager が行うので、Unity のライフイベントは使わない。</summary>
    public sealed class FreeDReceiverForUnity : MonoBehaviour
    {
        [SerializeField] private int port = 40000;
        [SerializeField] private float delaySec = 0f;

        [SerializeField]
        private FreeDField field = FreeDField.Position | FreeDField.Rotation | FreeDField.Zoom | FreeDField.Focus;

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

        public FreeDField Field
        {
            get => field;
            set => field = value;
        }

        private readonly FreeDReceiver receiver = new FreeDReceiver();

        public void Open()
        {
            receiver.Open(port, delaySec);
        }

        /// <summary>このコンポーネントが壊されたあとでも呼べる。</summary>
        public void Close()
        {
            receiver.Close();
        }

        public bool TryTakeNext(out FreeDReceivedPacket received)
        {
            return receiver.TryTakeNext(out received);
        }
    }
}