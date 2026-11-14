using UnityEngine;

/// <summary>
/// FreeDTracker が受け取ったズームとフォーカスの生の値 (24bit のエンコーダ値) を、
/// カメラの焦点距離 (mm) とピント距離 (m) に直して反映する。
/// 対応はレンズごとに違うので、実測した点を AnimationCurve に入れる (横軸 = 生の値)。
/// キーのない曲線と、まだ値が届いていない項目は反映しない。
/// FreeDTracker と同じオブジェクト (カメラ) に付ける。反映は、FreeDTracker が毎フレーム行う (Unity の Update などは使わない)。
/// </summary>
[RequireComponent(typeof(Camera))]
[RequireComponent(typeof(FreeDTracker))]
[DisallowMultipleComponent]
public class FreeDLens : MonoBehaviour
{
    [Tooltip("横軸 = ズームの生の値、縦軸 = 焦点距離 (mm)")]
    public AnimationCurve zoomToFocalLengthMm = new AnimationCurve();
    [Tooltip("横軸 = フォーカスの生の値、縦軸 = ピント距離 (m)")]
    public AnimationCurve focusToDistanceM = new AnimationCurve();

    // 状態の欄は FreeDLensEditor が枠で囲んで描く (Header は付けない)
    [FreeDReadOnly] public float focalLengthMm;
    [Tooltip("ピントが合う距離 (m)。被写界深度のエフェクトなど、使う側がこの値を読む"), FreeDReadOnly]
    public float focusDistanceM;

    private Camera _camera;

    /// <summary>FreeDTracker が毎フレーム呼ぶ。生の値を、カメラの焦点距離とピント距離に反映する。</summary>
    internal void Apply(bool hasZoom, uint zoomRaw, bool hasFocus, uint focusRaw)
    {
        if (_camera == null)
        {
            _camera = GetComponent<Camera>();
        }

        if (hasZoom && zoomToFocalLengthMm.length > 0)
        {
            focalLengthMm = zoomToFocalLengthMm.Evaluate(zoomRaw);
            _camera.usePhysicalProperties = true;
            _camera.focalLength = focalLengthMm;
        }
        if (hasFocus && focusToDistanceM.length > 0)
        {
            focusDistanceM = focusToDistanceM.Evaluate(focusRaw);
#if UNITY_2022_2_OR_NEWER
            _camera.focusDistance = focusDistanceM;
#endif
        }
    }
}
