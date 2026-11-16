using CIFER.tech.FreeD.Core;
using UnityEngine;

namespace CIFER.tech.FreeD.Unity
{
    [DisallowMultipleComponent]
    public class FreeDTracker : MonoBehaviour, IFreeDTracker
    {
        private const float GizmoArrowLengthM = 0.5f;

        [SerializeField] private int cameraId = 1;
        [SerializeField] private Camera camera;
        [SerializeField] private Vector3 rotationScale = Vector3.one;
        [SerializeField] private Vector3 positionScale = Vector3.one;
        [SerializeField] private Vector3 positionOffset = Vector3.zero;
        [SerializeField] private Vector3 rotationOffset = Vector3.zero;

        [SerializeField]
        private AnimationCurve zoomToFocalLengthMm = AnimationCurve.Linear(0f, 0f, FreeDCodec.Unsigned24Max, 1f);

        [SerializeField]
        private AnimationCurve focusToDistanceM = AnimationCurve.Linear(0f, 0f, FreeDCodec.Unsigned24Max, 1f);

        public int CameraId
        {
            get => cameraId;
            set => cameraId = value;
        }

        public Vector3 RotationScale
        {
            get => rotationScale;
            set => rotationScale = value;
        }

        public Vector3 PositionScale
        {
            get => positionScale;
            set => positionScale = value;
        }

        public Vector3 PositionOffset
        {
            get => positionOffset;
            set => positionOffset = value;
        }

        public Vector3 RotationOffset
        {
            get => rotationOffset;
            set => rotationOffset = value;
        }

        public AnimationCurve ZoomToFocalLengthMm
        {
            get => zoomToFocalLengthMm;
            set => zoomToFocalLengthMm = value;
        }

        public AnimationCurve FocusToDistanceM
        {
            get => focusToDistanceM;
            set => focusToDistanceM = value;
        }

        /// <summary>届いていない項目は値が 0 のままなので、appliedFields の項目だけを反映する。</summary>
        public void Apply(in FreeDPacket packet, FreeDField appliedFields = FreeDField.All)
        {
            if ((appliedFields & FreeDField.Rotation) != 0)
            {
                // FreeD 座標 (X=右, Y=前, Z=上, m) での姿勢。
                // 回転は Rz(パン) · Rx(チルト) · Ry(ロール)。角度の正の向きは送信機器の仕様によるので、向きが逆なら rotationScale で反転する。
                var freeDRotation =
                    Quaternion.AngleAxis((float)packet.PanDegrees * rotationScale.x, Vector3.forward)
                    * Quaternion.AngleAxis((float)packet.TiltDegrees * rotationScale.y, Vector3.right)
                    * Quaternion.AngleAxis((float)packet.RollDegrees * rotationScale.z, Vector3.up);

                // FreeD の (X, Y, Z) を Unity の (x, z, y) に入れ替える。Y と Z の入れ替えで左右の手系が変わるので、回転の向きも反転する。
                var unityRotation = new Quaternion(-freeDRotation.x, -freeDRotation.z, -freeDRotation.y,
                    freeDRotation.w);
                transform.localRotation = unityRotation * Quaternion.Euler(rotationOffset);
            }

            if ((appliedFields & FreeDField.Position) != 0)
            {
                var unityPosition = new Vector3(
                    (float)packet.XMeters * positionScale.x,
                    (float)packet.ZMeters * positionScale.z,
                    (float)packet.YMeters * positionScale.y);

                // オフセットは Unity のカメラのローカル軸で適用する。
                // 向きは先に反映してあるので、今の向きから向きのオフセットを除けば、向きが届いていないときも使える。
                var offsetRotation = transform.localRotation * Quaternion.Inverse(Quaternion.Euler(rotationOffset));
                transform.localPosition = unityPosition + offsetRotation * positionOffset;
            }

            if (camera != null)
            {
                if ((appliedFields & FreeDField.Zoom) != 0 && zoomToFocalLengthMm.length > 0)
                {
                    camera.usePhysicalProperties = true;
                    camera.focalLength = zoomToFocalLengthMm.Evaluate(packet.ZoomRaw);
                }

#if UNITY_2022_2_OR_NEWER
                if ((appliedFields & FreeDField.Focus) != 0 && focusToDistanceM.length > 0)
                {
                    camera.usePhysicalProperties = true;
                    camera.focusDistance = focusToDistanceM.Evaluate(packet.FocusRaw);
                }
#endif
            }
        }

        private void OnEnable()
        {
            var manager = FreeDManager.Instance;
            if (manager != null)
            {
                manager.AddTracker(this);
            }
        }

        private void OnDisable()
        {
            var manager = FreeDManager.ExistingInstance;
            if (manager != null)
            {
                manager.RemoveTracker(this);
            }
        }

        private void OnDrawGizmos()
        {
            // 親が FreeD 座標の原点。親がなければワールドの原点。
            var origin = transform.parent;
            var originPosition = origin != null ? origin.position : Vector3.zero;
            var originRotation = origin != null ? origin.rotation : Quaternion.identity;

            Gizmos.color = Color.red;
            Gizmos.DrawLine(originPosition, originPosition + originRotation * Vector3.right * GizmoArrowLengthM);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(originPosition, originPosition + originRotation * Vector3.up * GizmoArrowLengthM);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(originPosition, originPosition + originRotation * Vector3.forward * GizmoArrowLengthM);
        }
    }
}