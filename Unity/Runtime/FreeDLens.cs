using UnityEngine;

/// <summary>
/// FreeDReceiver が受け取ったズームとフォーカスの生の値 (24bit のエンコーダ値) を、
/// カメラの焦点距離 (mm) とピント距離 (m) に直して反映する。
/// 対応はレンズごとに違うので、実測した点を AnimationCurve に入れる (横軸 = 生の値)。
/// キーのない曲線と、まだ値が届いていない項目は反映しない。
/// </summary>
[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(FreeDReceiver))]
[DisallowMultipleComponent]
public class FreeDLens : MonoBehaviour
{
    [Tooltip("横軸 = ズームの生の値、縦軸 = 焦点距離 (mm)")]
    public AnimationCurve zoomToFocalLengthMm = new AnimationCurve();
    [Tooltip("横軸 = フォーカスの生の値、縦軸 = ピント距離 (m)")]
    public AnimationCurve focusToDistanceM = new AnimationCurve();

    [Header("状態 (読み取り専用)")]
    public float focalLengthMm;
    [Tooltip("ピントが合う距離 (m)。被写界深度のエフェクトなど、使う側がこの値を読む")]
    public float focusDistanceM;

    private Camera _camera;
    private FreeDReceiver _receiver;

    void Awake()
    {
        _camera = GetComponent<Camera>();
        _receiver = GetComponent<FreeDReceiver>();
    }

    void LateUpdate()
    {
        if (_receiver.hasZoom && zoomToFocalLengthMm.length > 0)
        {
            focalLengthMm = zoomToFocalLengthMm.Evaluate(_receiver.zoomRaw);
            _camera.usePhysicalProperties = true;
            _camera.focalLength = focalLengthMm;
        }
        if (_receiver.hasFocus && focusToDistanceM.length > 0)
        {
            focusDistanceM = focusToDistanceM.Evaluate(_receiver.focusRaw);
#if UNITY_2022_2_OR_NEWER
            _camera.focusDistance = focusDistanceM;
#endif
        }
    }
}
