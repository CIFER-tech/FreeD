using System;
using System.IO;
using UnityEngine;

/// <summary>
/// retroloc の原点と Unity の原点のずれを吸収する。FreeDReceiver を付けたカメラの親に付ける。
/// このオブジェクトの位置と水平の向き (Y 軸まわり) が「retroloc 原点が Unity のどこにあるか」を表す。
/// 手で置いてもよいし、実行中に目印へカメラを合わせて自動で求めてもよい。結果はファイルに保存する。
/// </summary>
[DisallowMultipleComponent]
public class FreeDOrigin : MonoBehaviour
{
    [Header("対象")]
    [Tooltip("空なら子の FreeDReceiver を使う")]
    public FreeDReceiver receiver;
    [Tooltip("合わせる目印。カメラをこの位置に置き、向きは目印の前 (青い矢印) に向ける")]
    public Transform targetA;
    [Tooltip("2 か所で合わせるときの 2 つめの目印")]
    public Transform targetB;

    [Header("保存")]
    [Tooltip("相対パスは、エディタではプロジェクトのフォルダ、ビルドでは exe のフォルダが基準")]
    public string saveFile = "freed-origin.json";
    [Tooltip("実行中に画面へ操作ボタンを出す")]
    public bool showButtons = true;

    [Header("状態 (読み取り専用)")]
    public string status = "";
    [Tooltip("2 か所で合わせたときの、2 点間の距離の誤差 (mm)")]
    public float lastErrorMm;

    [Serializable]
    private class Saved { public float x, y, z, yaw; }

    private const float MinSeparation = 0.3f;
    private Vector3 _scenePos;
    private Quaternion _sceneRot;
    private bool _hasA;
    private Vector3 _pA;
    private bool _panelOpen = true;

    string SavePath
    {
        get
        {
            if (Path.IsPathRooted(saveFile)) return saveFile;
            return Path.Combine(Path.GetDirectoryName(Application.dataPath), saveFile);
        }
    }

    void Awake()
    {
        _scenePos = transform.position;
        _sceneRot = transform.rotation;
        if (receiver == null) receiver = GetComponentInChildren<FreeDReceiver>();
        Load();
    }

    void Load()
    {
        try
        {
            if (string.IsNullOrEmpty(saveFile) || !File.Exists(SavePath)) return;
            var s = JsonUtility.FromJson<Saved>(File.ReadAllText(SavePath));
            transform.SetPositionAndRotation(new Vector3(s.x, s.y, s.z), Quaternion.Euler(0, s.yaw, 0));
            status = "保存した原点を読みました";
        }
        catch (Exception e) { status = "読めません: " + e.Message; Debug.LogWarning("FreeD 原点: " + e.Message); }
    }

    void Save()
    {
        try
        {
            var p = transform.position;
            File.WriteAllText(SavePath, JsonUtility.ToJson(new Saved { x = p.x, y = p.y, z = p.z, yaw = transform.eulerAngles.y }, true));
        }
        catch (Exception e) { status += " (保存できません: " + e.Message + ")"; Debug.LogWarning("FreeD 原点: " + e.Message); }
    }

    // 向き (Y 軸まわり, 度)。前 = +Z。真上・真下を向いて水平成分が小さいときは NaN。
    static float Yaw(Vector3 d)
    {
        if (d.x * d.x + d.z * d.z < 0.01f * d.sqrMagnitude) return float.NaN;
        return Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
    }

    bool CameraPose(out Vector3 p, out Vector3 fwd)
    {
        p = fwd = default;
        if (receiver == null) receiver = GetComponentInChildren<FreeDReceiver>();
        if (receiver == null) { status = "カメラ (FreeDReceiver) がありません"; return false; }
        if (!receiver.receiving) { status = "FreeD を受信していません"; return false; }
        p = transform.InverseTransformPoint(receiver.transform.position);
        fwd = transform.InverseTransformDirection(receiver.transform.forward);
        return true;
    }

    [ContextMenu("1か所で合わせる")]
    public void AlignOne()
    {
        if (targetA == null) { status = "目印 A がありません"; return; }
        if (!CameraPose(out Vector3 p, out Vector3 fwd)) return;
        float yawCam = Yaw(fwd);
        float yawT = Yaw(targetA.forward);
        if (float.IsNaN(yawCam)) { status = "カメラを水平に近い向きにしてください"; return; }
        if (float.IsNaN(yawT)) { status = "目印 A の前が真上か真下です"; return; }
        Apply(yawT - yawCam, targetA.position - Quaternion.Euler(0, yawT - yawCam, 0) * p);
        lastErrorMm = 0;
        status = "1 か所で合わせました";
    }

    [ContextMenu("2か所: A を記録")]
    public void RecordA()
    {
        if (targetA == null) { status = "目印 A がありません"; return; }
        if (!CameraPose(out Vector3 p, out _)) return;
        _pA = p; _hasA = true;
        status = "A を記録しました。カメラを目印 B に移して「B で合わせる」";
    }

    [ContextMenu("2か所: B を記録して合わせる")]
    public void RecordBAndAlign()
    {
        if (!_hasA) { status = "先に A を記録してください"; return; }
        if (targetA == null || targetB == null) { status = "目印 A・B がありません"; return; }
        if (!CameraPose(out Vector3 pB, out _)) return;
        Vector3 dL = pB - _pA, dW = targetB.position - targetA.position;
        if (new Vector2(dL.x, dL.z).magnitude < MinSeparation || new Vector2(dW.x, dW.z).magnitude < MinSeparation)
        { status = "A と B が近すぎます (" + MinSeparation + " m 以上離してください)"; return; }
        float yaw = Yaw(dW) - Yaw(dL);
        Quaternion r = Quaternion.Euler(0, yaw, 0);
        Vector3 pos = ((targetA.position - r * _pA) + (targetB.position - r * pB)) * 0.5f;
        Apply(yaw, pos);
        lastErrorMm = (dW.magnitude - dL.magnitude) * 1000f;
        _hasA = false;
        status = "2 か所で合わせました (距離の誤差 " + lastErrorMm.ToString("F1") + " mm)";
    }

    [ContextMenu("合わせを消す")]
    public void Clear()
    {
        try { if (File.Exists(SavePath)) File.Delete(SavePath); }
        catch (Exception e) { Debug.LogWarning("FreeD 原点: " + e.Message); }
        transform.SetPositionAndRotation(_scenePos, _sceneRot);
        _hasA = false; lastErrorMm = 0;
        status = "合わせを消しました";
    }

    void Apply(float yaw, Vector3 pos)
    {
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        Save();
    }

    void OnGUI()
    {
        if (!showButtons) return;
        GUILayout.BeginArea(new Rect(10, 75, 320, 260));
        _panelOpen = GUILayout.Toggle(_panelOpen, "原点合わせ");
        if (_panelOpen)
        {
            if (GUILayout.Button("1 か所で合わせる")) AlignOne();
            if (GUILayout.Button("2 か所: A を記録")) RecordA();
            if (GUILayout.Button("2 か所: B を記録して合わせる")) RecordBAndAlign();
            if (GUILayout.Button("合わせを消す")) Clear();
            if (!string.IsNullOrEmpty(status)) GUILayout.Label(status);
        }
        GUILayout.EndArea();
    }

    void OnDrawGizmos()
    {
        var t = transform;
        Gizmos.color = Color.red; Gizmos.DrawLine(t.position, t.position + t.right * 0.5f);
        Gizmos.color = Color.green; Gizmos.DrawLine(t.position, t.position + t.up * 0.5f);
        Gizmos.color = Color.blue; Gizmos.DrawLine(t.position, t.position + t.forward * 0.5f);
        Gizmos.color = Color.yellow;
        if (targetA != null) Gizmos.DrawLine(t.position, targetA.position);
        if (targetA != null && targetB != null) Gizmos.DrawLine(targetA.position, targetB.position);
    }
}
