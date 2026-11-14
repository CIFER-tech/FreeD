using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using FreeD;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// FreeD (UDP) を受信して、このオブジェクト (カメラ) の位置と向きに反映する。
/// ストリーム (ポート) を複数並べて、回転・位置・ズーム・フォーカスのどれをどのストリームから使うかを選べる
/// (例: 姿勢は retroloc のポート 40000、ズームとフォーカスはレンズエンコーダのポート 40001)。
/// retroloc のワールド (X=右, Y=前, Z=上, m) を Unity に変換する。
/// 回転は R = Rz(パン) · Rx(チルト) · Ry(ロール)。パンは反時計回りが正、チルトは上向きが正。
/// ズームとフォーカスの生の値は zoomRaw / focusRaw に出る。カメラに反映するには FreeDLens を付ける。
/// </summary>
[DisallowMultipleComponent]
public class FreeDReceiver : MonoBehaviour, ISerializationCallbackReceiver
{
    [Serializable]
    public class Stream : ISerializationCallbackReceiver
    {
        [Tooltip("送信側の FreeD送信 のポートと同じにする")]
        public int port = 40000;
        [Tooltip("0 = 全カメラ")]
        public int cameraId = 0;

        [Tooltip("このストリームから使う項目 (同じ項目は 1 つのストリームだけ)")]
        public FreeDFields fields = AllFields;

        [Tooltip("ms。他のストリームより早く届くときに、この分だけ反映を遅らせてそろえる")]
        public float delayMs = 0f;

        [Header("状態 (読み取り専用)")]
        [FreeDReadOnly] public bool receiving;
        [FreeDReadOnly] public int packetsPerSecond;
        [Tooltip("直近1秒の受信間隔 (ms)"), FreeDReadOnly]
        public float meanIntervalMs, maxIntervalMs;

        // 受信スレッドが書き、Update が読む。FreeDReceiver の _lock で守る。
        [NonSerialized] internal int count;
        [NonSerialized] internal double sumIntervalMs, maxIntervalMsSoFar, lastReceivedMs = -1;

        // Everything を選ぶと全ビットが立った -1 になるので、項目のビットだけに絞って返す。
        private const FreeDFields AllFields = FreeDFields.Rotation | FreeDFields.Position | FreeDFields.Zoom | FreeDFields.Focus;

        public FreeDFields Fields
        {
            get { return fields & AllFields; }
            set { fields = value; }
        }

        // 1.3 までの設定 (項目ごとのチェックボックス)。fields を持たない古いシーンを読んだときに fields へ移す。
        // 古い値は _isFieldsSet が false のときだけ使うので、新しく作ったストリームでは既定の全項目 (true) のまま fields になる。
        [SerializeField, HideInInspector, FormerlySerializedAs("rotation")] private bool _legacyRotation = true;
        [SerializeField, HideInInspector, FormerlySerializedAs("position")] private bool _legacyPosition = true;
        [SerializeField, HideInInspector, FormerlySerializedAs("zoom")] private bool _legacyZoom = true;
        [SerializeField, HideInInspector, FormerlySerializedAs("focus")] private bool _legacyFocus = true;
        [SerializeField, HideInInspector] private bool _isFieldsSet;

        public void OnBeforeSerialize() { _isFieldsSet = true; }

        public void OnAfterDeserialize()
        {
            if (_isFieldsSet) return;
            fields = (_legacyRotation ? FreeDFields.Rotation : 0) | (_legacyPosition ? FreeDFields.Position : 0)
                   | (_legacyZoom ? FreeDFields.Zoom : 0) | (_legacyFocus ? FreeDFields.Focus : 0);
            _isFieldsSet = true;
        }
    }

    [Header("受信")]
    public List<Stream> streams = new List<Stream> { new Stream() };

    [Header("符号 (向きが逆なら -1)")]
    public float panSign = 1f;
    public float tiltSign = 1f;
    public float rollSign = 1f;

    [Header("手動オフセット (カメラ取り付けのずれ。Unity 座標)")]
    [Tooltip("トラッカーからカメラまで (Unity のカメラの軸: X=右, Y=上, Z=前, m)")]
    public Vector3 positionOffset = Vector3.zero;
    [Tooltip("度。Unity のカメラの軸まわり (X=チルト, Y=パン, Z=ロール)。Transform の Rotation と同じ向き")]
    public Vector3 rotationOffset = Vector3.zero;

    [Header("状態 (読み取り専用)")]
    [FreeDReadOnly] public bool receiving;
    [FreeDReadOnly] public double pan, tilt, roll;
    [FreeDReadOnly] public double xMm, yMm, zMm;
    [Tooltip("ズーム・フォーカスのエンコーダの生の値 (24bit)。FreeDLens が焦点距離とピント距離に直す")]
    [FreeDReadOnly] public uint zoomRaw, focusRaw;
    [FreeDReadOnly] public bool hasZoom, hasFocus;

    // 1.2 までの設定 (ストリームが 1 本だけだった)。読み込んだときに streams の 1 本目へ移す。
    [SerializeField, HideInInspector, FormerlySerializedAs("port")] private int _legacyPort = -1;
    [SerializeField, HideInInspector, FormerlySerializedAs("cameraId")] private int _legacyCameraId;

    private readonly List<UdpClient> _udps = new List<UdpClient>();
    private readonly List<Thread> _threads = new List<Thread>();
    private volatile bool _run;
    private readonly object _lock = new object();
    private FreeDMerger _merger;
    private int _startedSignature;
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    private float _rateTimer;
    // OnValidate で「今回新しくチェックされた項目」を見つけるための、前回のチェックの状態
    [NonSerialized] private List<FreeDFields> _lastFields;

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        if (_legacyPort < 0) return;
        streams = new List<Stream> { new Stream { port = _legacyPort, cameraId = _legacyCameraId } };
        _legacyPort = -1;
    }

    void OnValidate() { ResolveOverlaps(); }

    /// <summary>
    /// 同じ項目を 2 つのストリームが使わないようにする。
    /// 今回チェックされた項目は、他のストリームのチェックを外して譲らせる。追加されたばかりのストリームは、他が使っている項目を譲る側にする。
    /// </summary>
    void ResolveOverlaps()
    {
        for (int i = 0; i < streams.Count; i++)
        {
            bool isExisting = _lastFields != null && i < _lastFields.Count;
            if (!isExisting) continue;
            FreeDFields added = streams[i].Fields & ~_lastFields[i];
            for (int j = 0; j < streams.Count; j++)
                if (j != i) streams[j].Fields &= ~added;
        }
        for (int i = 0; i < streams.Count; i++)
        {
            bool isNew = _lastFields == null || i >= _lastFields.Count;
            if (!isNew) continue;
            FreeDFields claimedByOthers = FreeDFields.None;
            for (int j = 0; j < streams.Count; j++)
                if (j != i) claimedByOthers |= streams[j].Fields;
            streams[i].Fields &= ~claimedByOthers;
        }

        _lastFields = new List<FreeDFields>();
        foreach (Stream s in streams) _lastFields.Add(s.Fields);
    }

    void OnEnable() { StartReceiving(); }
    void OnDisable() { StopReceiving(); }

    void StartReceiving()
    {
        _startedSignature = Signature();
        var fields = new List<FreeDFields>();
        foreach (Stream s in streams) fields.Add(s.Fields);
        try { _merger = new FreeDMerger(fields); }
        catch (ArgumentException e) { Debug.LogError("FreeD: " + e.Message); return; }

        _run = true;
        for (int i = 0; i < streams.Count; i++)
        {
            Stream stream = streams[i];
            int streamIndex = i;
            UdpClient udp;
            try
            {
                udp = new UdpClient(new IPEndPoint(IPAddress.Any, stream.port));
                udp.Client.ReceiveTimeout = 200;
            }
            catch (Exception e) { Debug.LogError("FreeD: ポート " + stream.port + " を開けない: " + e.Message); continue; }
            var thread = new Thread(() => Loop(streamIndex, stream, udp)) { IsBackground = true, Name = "FreeDReceiver " + stream.port };
            _udps.Add(udp);
            _threads.Add(thread);
            thread.Start();
        }
    }

    void StopReceiving()
    {
        _run = false;
        foreach (UdpClient udp in _udps) { try { udp.Close(); } catch { } }
        foreach (Thread thread in _threads) thread.Join(500);
        _udps.Clear(); _threads.Clear();
        _merger = null;
    }

    /// <summary>ポート・カメラ ID・項目・本数のどれかが変わると値が変わる。再生中の設定変更で受信をやり直す目印。</summary>
    int Signature()
    {
        unchecked
        {
            int hash = streams.Count;
            foreach (Stream s in streams)
                hash = hash * 31 + (s.port * 31 + s.cameraId) * 31 + (int)s.Fields;
            return hash;
        }
    }

    void Loop(int streamIndex, Stream stream, UdpClient udp)
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (_run)
        {
            try
            {
                byte[] data = udp.Receive(ref any);
                if (!FreeDCodec.TryDecode(data, out FreeDPacket p)) continue;
                if (stream.cameraId != 0 && p.CameraId != stream.cameraId) continue;
                double now = _sw.Elapsed.TotalMilliseconds;
                lock (_lock)
                {
                    _merger.Put(streamIndex, p, now);
                    stream.count++;
                    if (stream.lastReceivedMs >= 0)
                    {
                        double d = now - stream.lastReceivedMs;
                        stream.sumIntervalMs += d;
                        if (d > stream.maxIntervalMsSoFar) stream.maxIntervalMsSoFar = d;
                    }
                    stream.lastReceivedMs = now;
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { return; }
        }
    }

    void Update()
    {
        if (Signature() != _startedSignature) { StopReceiving(); StartReceiving(); }
        if (_merger == null) return;

        double nowMs = _sw.Elapsed.TotalMilliseconds;
        FreeDFields filled;
        FreeDPacket p;
        lock (_lock)
        {
            for (int i = 0; i < streams.Count; i++) _merger.SetDelay(i, streams[i].delayMs);
            filled = _merger.Update(nowMs, out p);
            receiving = false;
            for (int i = 0; i < streams.Count; i++)
            {
                streams[i].receiving = _merger.IsReceiving(i, nowMs);
                receiving |= streams[i].receiving;
            }
        }
        UpdateRates();

        if ((filled & FreeDFields.Zoom) != 0) { zoomRaw = p.ZoomRaw; hasZoom = true; }
        if ((filled & FreeDFields.Focus) != 0) { focusRaw = p.FocusRaw; hasFocus = true; }
        ApplyPose(filled, p);
    }

    void UpdateRates()
    {
        _rateTimer += Time.unscaledDeltaTime;
        if (_rateTimer < 1f) return;
        lock (_lock)
        {
            foreach (Stream s in streams)
            {
                s.packetsPerSecond = Mathf.RoundToInt(s.count / _rateTimer);
                s.meanIntervalMs = s.count > 1 ? (float)(s.sumIntervalMs / (s.count - 1)) : 0f;
                s.maxIntervalMs = (float)s.maxIntervalMsSoFar;
                s.count = 0; s.sumIntervalMs = 0; s.maxIntervalMsSoFar = 0;
            }
        }
        _rateTimer = 0f;
    }

    void ApplyPose(FreeDFields filled, FreeDPacket p)
    {
        bool hasRotation = (filled & FreeDFields.Rotation) != 0;
        bool hasPosition = (filled & FreeDFields.Position) != 0;
        if (hasRotation) { pan = p.PanDegrees; tilt = p.TiltDegrees; roll = p.RollDegrees; }
        if (hasPosition) { xMm = p.XMeters * 1000; yMm = p.YMeters * 1000; zMm = p.ZMeters * 1000; }

        // retroloc 座標 (X右 Y前 Z上) の姿勢。AngleAxis は数値的に標準の回転なので、そのまま合成できる。
        // 回転のストリームがないときは p の角度が 0 なので、単位回転になる。
        Quaternion q = Rot((float)p.PanDegrees * panSign, (float)p.TiltDegrees * tiltSign, (float)p.RollDegrees * rollSign);
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

    void OnGUI()
    {
        var text = new System.Text.StringBuilder();
        text.Append(receiving ? "FreeD 受信中\n" : "FreeD 待機\n");
        foreach (Stream s in streams)
        {
            text.AppendFormat("  :{0} {1} {2} pkt/s  間隔 平均{3:F1} 最大{4:F1} ms\n",
                s.port, s.receiving ? "受信中" : "途切れ", s.packetsPerSecond, s.meanIntervalMs, s.maxIntervalMs);
        }
        text.AppendFormat("位置 mm {0:F1} {1:F1} {2:F1}\n角度 ° {3:F2} {4:F2} {5:F2}\n", xMm, yMm, zMm, pan, tilt, roll);
        text.AppendFormat("ズーム {0}  フォーカス {1}", hasZoom ? zoomRaw.ToString() : "-", hasFocus ? focusRaw.ToString() : "-");
        GUI.Label(new Rect(10, 10, 460, 40 + 18 * streams.Count), text.ToString());
    }
}
