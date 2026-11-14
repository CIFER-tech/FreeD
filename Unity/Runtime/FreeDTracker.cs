using FreeD;
using UnityEngine;

/// <summary>
/// FreeDManager からカメラ ID を指定して FreeD の値を受け取り、このオブジェクトの位置と向きに反映する。
/// 親オブジェクトが retroloc の原点になる。親の Position と Rotation の Y が、retroloc の原点の位置と水平の向きになる。
/// Scene ビューの青い矢印 (前) が retroloc の Y 方向、赤い矢印 (右) が X 方向。
/// retroloc のワールド (X=右, Y=前, Z=上, m) を Unity に変換する。
/// 回転は R = Rz(パン) · Rx(チルト) · Ry(ロール)。パンは反時計回りが正、チルトは上向きが正。
/// ズームとフォーカスの生の値は zoomRaw / focusRaw に出る。カメラに反映するには FreeDLens を付ける。
/// </summary>
[DisallowMultipleComponent]
public class FreeDTracker : MonoBehaviour
{
    [Tooltip("使うカメラ ID。送信側の FreeD送信 のカメラ ID と同じにする")]
    public int cameraId = 1;

    [Tooltip("角度にかける倍率。X=パン, Y=チルト, Z=ロール。向きが逆なら -1")]
    public Vector3 rotationScale = Vector3.one;
    [Tooltip("トラッカーからカメラまで (Unity のカメラの軸: X=右, Y=上, Z=前, m)")]
    public Vector3 positionOffset = Vector3.zero;
    [Tooltip("度。Unity のカメラの軸まわり (X=チルト, Y=パン, Z=ロール)。Transform の Rotation と同じ向き")]
    public Vector3 rotationOffset = Vector3.zero;

    // 状態の欄は FreeDTrackerEditor が枠で囲んで描く (Header は付けない)
    [FreeDReadOnly] public bool receiving;
    [Tooltip("度。X=パン, Y=チルト, Z=ロール (倍率をかけたあと)"), FreeDReadOnly] public Vector3 panTiltRollDeg;
    [Tooltip("mm。FreeD 座標 (X=右, Y=前, Z=上)"), FreeDReadOnly] public Vector3 positionMm;
    [FreeDReadOnly] public bool hasZoom;
    [Tooltip("ズームのエンコーダの生の値 (24bit)。FreeDLens が焦点距離に直す"), FreeDReadOnly] public uint zoomRaw;
    [FreeDReadOnly] public bool hasFocus;
    [Tooltip("フォーカスのエンコーダの生の値 (24bit)。FreeDLens がピント距離に直す"), FreeDReadOnly] public uint focusRaw;

    void LateUpdate()
    {
        FreeDManager manager = FreeDManager.Instance;
        if (manager == null) return;
        if (!manager.TryGet(cameraId, out FreeDFields filled, out FreeDPacket packet, out bool isReceiving))
        {
            receiving = false;
            return;
        }
        receiving = isReceiving;
        ApplyPose(filled, packet);
    }

    void ApplyPose(FreeDFields filled, FreeDPacket p)
    {
        bool hasRotation = (filled & FreeDFields.Rotation) != 0;
        bool hasPosition = (filled & FreeDFields.Position) != 0;
        if ((filled & FreeDFields.Zoom) != 0) { zoomRaw = p.ZoomRaw; hasZoom = true; }
        if ((filled & FreeDFields.Focus) != 0) { focusRaw = p.FocusRaw; hasFocus = true; }

        float panDeg = (float)p.PanDegrees * rotationScale.x;
        float tiltDeg = (float)p.TiltDegrees * rotationScale.y;
        float rollDeg = (float)p.RollDegrees * rotationScale.z;
        if (hasRotation) panTiltRollDeg = new Vector3(panDeg, tiltDeg, rollDeg);
        if (hasPosition) positionMm = new Vector3((float)(p.XMeters * 1000), (float)(p.YMeters * 1000), (float)(p.ZMeters * 1000));

        // retroloc 座標 (X右 Y前 Z上) の姿勢。AngleAxis は数値的に標準の回転なので、そのまま合成できる。
        // 回転を担当する Receiver がないときは p の角度が 0 なので、単位回転になる。
        Quaternion q = Rot(panDeg, tiltDeg, rollDeg);
        Vector3 pos = new Vector3((float)p.XMeters, (float)p.YMeters, (float)p.ZMeters);

        // Unity 座標へ変換してから、Unity のカメラのローカル軸でオフセットを適用する
        Vector3 uPos = new Vector3(pos.x, pos.z, pos.y);
        Quaternion uRot = new Quaternion(-q.x, -q.z, -q.y, q.w);
        if (hasPosition) transform.localPosition = uPos + uRot * positionOffset;
        if (hasRotation) transform.localRotation = uRot * Quaternion.Euler(rotationOffset);
    }

    static Quaternion Rot(float pan, float tilt, float roll)
    {
        return Quaternion.AngleAxis(pan, new Vector3(0, 0, 1))
             * Quaternion.AngleAxis(tilt, new Vector3(1, 0, 0))
             * Quaternion.AngleAxis(roll, new Vector3(0, 1, 0));
    }

    void OnDrawGizmos()
    {
        // 親が retroloc の原点。親がなければワールドの原点。
        Transform origin = transform.parent;
        Vector3 position = origin != null ? origin.position : Vector3.zero;
        Quaternion rotation = origin != null ? origin.rotation : Quaternion.identity;
        Gizmos.color = Color.red; Gizmos.DrawLine(position, position + rotation * Vector3.right * 0.5f);
        Gizmos.color = Color.green; Gizmos.DrawLine(position, position + rotation * Vector3.up * 0.5f);
        Gizmos.color = Color.blue; Gizmos.DrawLine(position, position + rotation * Vector3.forward * 0.5f);
    }
}
