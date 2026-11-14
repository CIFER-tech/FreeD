using FreeD;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// カメラ ID を指定して、FreeDManager が合成した FreeD の値を受け取り、このオブジェクト (カメラ) の位置・向き・レンズに反映する。
/// 反映は FreeDManager が毎フレーム行う (Unity の Update などは使わない)。
/// 親オブジェクトが retroloc の原点になる。親の Position と Rotation の Y が、retroloc の原点の位置と水平の向きになる。
/// Scene ビューの青い矢印 (前) が retroloc の Y 方向、赤い矢印 (右) が X 方向。
/// retroloc のワールド (X=右, Y=前, Z=上, m) を Unity に変換する。
/// 回転は R = Rz(パン) · Rx(チルト) · Ry(ロール)。パンは反時計回りが正、チルトは上向きが正。
/// ズームとフォーカスの生の値 (24bit のエンコーダ値) は、実測した点を入れた AnimationCurve で、
/// カメラの焦点距離 (mm) とピント距離 (m) に直す (対応はレンズごとに違う)。キーのない曲線は反映しない。
/// </summary>
[RequireComponent(typeof(Camera))]
[DisallowMultipleComponent]
public class FreeDTracker : MonoBehaviour
{
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

    // 状態の欄は FreeDTrackerEditor が枠で囲んで描く (Header は付けない)
    [FreeDReadOnly] public bool receiving;

    [FormerlySerializedAs("panTiltRollDeg")] [Tooltip("度。X=パン, Y=チルト, Z=ロール (倍率をかけたあと)"), FreeDReadOnly]
    public Vector3 rotation;

    [FormerlySerializedAs("positionMm")] [Tooltip("m。FreeD 座標 (X=右, Y=前, Z=上) (倍率をかけたあと)"), FreeDReadOnly]
    public Vector3 position;

    [Tooltip("ズームのエンコーダの生の値 (24bit)"), FreeDReadOnly]
    public uint zoomRaw;

    [Tooltip("フォーカスのエンコーダの生の値 (24bit)"), FreeDReadOnly]
    public uint focusRaw;

    [FreeDReadOnly] public float focalLengthMm;

    [Tooltip("ピントが合う距離 (m)。被写界深度のエフェクトなど、使う側がこの値を読む"), FreeDReadOnly]
    public float focusDistanceM;

    private Camera _camera;

    /// <summary>FreeDManager が毎フレーム呼ぶ。合成した値を、このオブジェクトの位置・向き・レンズに反映する。</summary>
    internal void Apply(FreeDPacket packet)
    {
        position = new Vector3(
            (float)packet.XMeters * positionScale.x,
            (float)packet.YMeters * positionScale.y,
            (float)packet.ZMeters * positionScale.z);
        rotation = new Vector3(
            (float)packet.PanDegrees * rotationScale.x,
            (float)packet.TiltDegrees * rotationScale.y,
            (float)packet.RollDegrees * rotationScale.z);
        zoomRaw = packet.ZoomRaw;
        focusRaw = packet.FocusRaw;

        // retroloc 座標 (X右 Y前 Z上) の姿勢。AngleAxis は数値的に標準の回転なので、そのまま合成できる。
        // 回転を担当する Receiver がないときは角度が 0 なので、単位回転になる。
        Quaternion q = Rot(rotation);

        // Unity 座標へ変換してから、Unity のカメラのローカル軸でオフセットを適用する
        var uPos = new Vector3(position.x, position.z, position.y);
        var uRot = new Quaternion(-q.x, -q.z, -q.y, q.w);
        transform.localPosition = uPos + uRot * positionOffset;
        transform.localRotation = uRot * Quaternion.Euler(rotationOffset);

        if (_camera == null)
        {
            _camera = GetComponent<Camera>();
        }

        if (zoomToFocalLengthMm.length > 0)
        {
            focalLengthMm = zoomToFocalLengthMm.Evaluate(zoomRaw);
            _camera.usePhysicalProperties = true;
            _camera.focalLength = focalLengthMm;
        }

        if (focusToDistanceM.length > 0)
        {
            focusDistanceM = focusToDistanceM.Evaluate(focusRaw);
#if UNITY_2022_2_OR_NEWER
            _camera.focusDistance = focusDistanceM;
#endif
        }
    }

    /// <param name="panTiltRollDeg">X=パン, Y=チルト, Z=ロール (度)</param>
    private static Quaternion Rot(Vector3 panTiltRollDeg)
    {
        return Quaternion.AngleAxis(panTiltRollDeg.x, new Vector3(0, 0, 1))
               * Quaternion.AngleAxis(panTiltRollDeg.y, new Vector3(1, 0, 0))
               * Quaternion.AngleAxis(panTiltRollDeg.z, new Vector3(0, 1, 0));
    }

    private void OnDrawGizmos()
    {
        // 親が retroloc の原点。親がなければワールドの原点。
        Transform origin = transform.parent;
        Vector3 originPosition = origin != null ? origin.position : Vector3.zero;
        Quaternion originRotation = origin != null ? origin.rotation : Quaternion.identity;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(originPosition, originPosition + originRotation * Vector3.right * 0.5f);
        Gizmos.color = Color.green;
        Gizmos.DrawLine(originPosition, originPosition + originRotation * Vector3.up * 0.5f);
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(originPosition, originPosition + originRotation * Vector3.forward * 0.5f);
    }
}
