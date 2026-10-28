using UnityEngine;

/// <summary>
/// retroloc の原点が Unity のどこにあるかを表す。FreeDReceiver を付けたカメラの親に付ける。
/// このオブジェクトの位置と水平の向き (Y 軸まわり) が retroloc の原点の位置と向きになる。
/// 青い矢印 (前) が retroloc の Y 方向、赤い矢印 (右) が X 方向。
/// 床は水平とみなすので、傾き (X・Z の回転) は 0 に保つ。
/// </summary>
[DisallowMultipleComponent]
public class FreeDOrigin : MonoBehaviour
{
    [Tooltip("retroloc の原点にあたる場所に置いた Unity のオブジェクト (ステージの隅など)。入れると、このオブジェクトがその位置と水平の向きに合う。空なら、このオブジェクトの Position と Rotation の Y を直接入れる")]
    [UnityEngine.Serialization.FormerlySerializedAs("place")]
    public Transform originMarker;

    void OnValidate() { Apply(); }
    void LateUpdate() { Apply(); }

    void Apply()
    {
        if (originMarker != null)
            transform.SetPositionAndRotation(originMarker.position, Quaternion.Euler(0, originMarker.eulerAngles.y, 0));
        else if (transform.eulerAngles.x != 0 || transform.eulerAngles.z != 0)
            transform.rotation = Quaternion.Euler(0, transform.eulerAngles.y, 0);
    }

    void OnDrawGizmos()
    {
        var t = transform;
        Gizmos.color = Color.red; Gizmos.DrawLine(t.position, t.position + t.right * 0.5f);
        Gizmos.color = Color.green; Gizmos.DrawLine(t.position, t.position + t.up * 0.5f);
        Gizmos.color = Color.blue; Gizmos.DrawLine(t.position, t.position + t.forward * 0.5f);
        if (originMarker != null) { Gizmos.color = Color.yellow; Gizmos.DrawLine(t.position, originMarker.position); }
    }
}
