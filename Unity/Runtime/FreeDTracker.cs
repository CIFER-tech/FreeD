using FreeD;
using UnityEngine;

/// <summary>
/// MVP の View。cameraId のカメラの FreeD の値を、このオブジェクト (カメラ) の位置・向き・レンズに反映する。
/// 反映は FreeDManager が毎フレーム呼ぶ。Unity のライフイベントは使わない。
/// 親のオブジェクトが retroloc の原点。親の Position と Rotation の Y が、原点の位置と水平の向きになる。
/// Scene ビューの青い矢印 (前) が retroloc の Y 方向、赤い矢印 (右) が X 方向。
/// ズームとフォーカスの生の値 (24bit のエンコーダ値) は、実測した点を入れた AnimationCurve で
/// 焦点距離 (mm) とピント距離 (m) に直す (対応はレンズごとに違う)。キーのない曲線は反映しない。
/// </summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public class FreeDTracker : MonoBehaviour
{
    // Scene ビューに描く原点の矢印の長さ (m)
    private const float GizmoArrowLengthM = 0.5f;

    [Tooltip("使うカメラ ID。送信側の FreeD送信 のカメラ ID と同じにする")]
    public int cameraId = 1;

    [Tooltip("角度にかける倍率。X=パン, Y=チルト, Z=ロール。向きが逆なら -1")]
    public Vector3 rotationScale = Vector3.one;

    [Tooltip("位置にかける倍率。X=右, Y=前, Z=上 (FreeD 座標)。向きが逆なら -1")]
    public Vector3 positionScale = Vector3.one;

    [Tooltip("トラッカーからカメラまで (Unity のカメラの軸: X=右, Y=上, Z=前, m)")]
    public Vector3 positionOffset = Vector3.zero;

    [Tooltip("度。Unity のカメラの軸まわり (X=チルト, Y=パン, Z=ロール)。Transform の Rotation と同じ向き")]
    public Vector3 rotationOffset = Vector3.zero;

    [Tooltip("横軸 = ズームの生の値、縦軸 = 焦点距離 (mm)")]
    public AnimationCurve zoomToFocalLengthMm = new AnimationCurve();

    [Tooltip("横軸 = フォーカスの生の値、縦軸 = ピント距離 (m)")]
    public AnimationCurve focusToDistanceM = new AnimationCurve();

    // Awake を使わないので、初めて反映するときに取る。
    private Camera _camera;

    internal void Apply(in FreeDPacket packet)
    {
        // retroloc のワールド座標 (X=右, Y=前, Z=上, m) での姿勢。
        // 回転は Rz(パン) · Rx(チルト) · Ry(ロール)。パンは反時計回り、チルトは上向きが正。
        // 回転を担当する Receiver がないときは角度が 0 なので、単位回転になる。
        Quaternion retrolocRotation =
            Quaternion.AngleAxis((float)packet.PanDegrees * rotationScale.x, Vector3.forward)
            * Quaternion.AngleAxis((float)packet.TiltDegrees * rotationScale.y, Vector3.right)
            * Quaternion.AngleAxis((float)packet.RollDegrees * rotationScale.z, Vector3.up);

        // retroloc の (X, Y, Z) を Unity の (x, z, y) に入れ替える。Y と Z の入れ替えで左右の手系が変わるので、回転の向きも反転する。
        var unityPosition = new Vector3(
            (float)packet.XMeters * positionScale.x,
            (float)packet.ZMeters * positionScale.z,
            (float)packet.YMeters * positionScale.y);
        var unityRotation = new Quaternion(-retrolocRotation.x, -retrolocRotation.z, -retrolocRotation.y, retrolocRotation.w);

        // オフセットは Unity のカメラのローカル軸で適用する。
        transform.localPosition = unityPosition + unityRotation * positionOffset;
        transform.localRotation = unityRotation * Quaternion.Euler(rotationOffset);

        if (_camera == null)
        {
            _camera = GetComponent<Camera>();
        }
        if (zoomToFocalLengthMm.length > 0)
        {
            _camera.usePhysicalProperties = true;
            _camera.focalLength = zoomToFocalLengthMm.Evaluate(packet.ZoomRaw);
        }
#if UNITY_2022_2_OR_NEWER
        // Camera.focusDistance は Unity 2022.2 から。
        if (focusToDistanceM.length > 0)
        {
            _camera.focusDistance = focusToDistanceM.Evaluate(packet.FocusRaw);
        }
#endif
    }

    private void OnDrawGizmos()
    {
        // 親が retroloc の原点。親がなければワールドの原点。
        Transform origin = transform.parent;
        Vector3 originPosition = origin != null ? origin.position : Vector3.zero;
        Quaternion originRotation = origin != null ? origin.rotation : Quaternion.identity;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(originPosition, originPosition + originRotation * Vector3.right * GizmoArrowLengthM);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(originPosition, originPosition + originRotation * Vector3.up * GizmoArrowLengthM);
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(originPosition, originPosition + originRotation * Vector3.forward * GizmoArrowLengthM);
    }
}
