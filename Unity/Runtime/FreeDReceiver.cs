using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using FreeD;
using UnityEngine;

/// <summary>
/// FreeD (UDP) を受信して、このオブジェクト (カメラ) の位置と向きに反映する。
/// retroloc のワールド (X=右, Y=前, Z=上, m) を Unity に変換する。
/// 回転は R = Rz(パン) · Rx(チルト) · Ry(ロール)。パンは反時計回りが正、チルトは上向きが正。
/// </summary>
[DisallowMultipleComponent]
public class FreeDReceiver : MonoBehaviour
{
    [Header("受信")]
    [Tooltip("retroloc の FreeD送信 のポートと同じにする")]
    public int port = 40000;
    [Tooltip("0 = 全カメラ")]
    public int cameraId = 0;

    [Header("符号 (向きが逆なら -1)")]
    public float panSign = 1f;
    public float tiltSign = 1f;
    public float rollSign = 1f;

    [Header("手動オフセット (カメラ取り付けのずれ)")]
    [Tooltip("トラッカーからカメラまで (トラッカー座標, m)")]
    public Vector3 positionOffset = Vector3.zero;
    [Tooltip("度。パン/チルト/ロール")]
    public Vector3 rotationOffset = Vector3.zero;

    [Header("状態 (読み取り専用)")]
    public bool receiving;
    public int packetsPerSecond;
    [Tooltip("直近1秒の受信間隔 (ms)")]
    public float meanIntervalMs, maxIntervalMs;
    public double pan, tilt, roll;
    public double xMm, yMm, zMm;

    private UdpClient _udp;
    private Thread _thread;
    private volatile bool _run;
    private readonly object _lock = new object();
    private FreeDPacket _latest;
    private bool _has;
    private int _count;
    private double _sumInterval, _maxInterval, _lastRx = -1;
    private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
    private float _rateTimer, _lastPacketTime;

    void OnEnable()
    {
        _run = true;
        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
            _udp.Client.ReceiveTimeout = 200;
        }
        catch (Exception e) { Debug.LogError("FreeD: ポート " + port + " を開けない: " + e.Message); return; }
        _thread = new Thread(Loop) { IsBackground = true, Name = "FreeDReceiver" };
        _thread.Start();
    }

    void OnDisable()
    {
        _run = false;
        try { _udp?.Close(); } catch { }
        _thread?.Join(500);
        _thread = null; _udp = null;
    }

    void Loop()
    {
        var any = new IPEndPoint(IPAddress.Any, 0);
        while (_run)
        {
            try
            {
                byte[] data = _udp.Receive(ref any);
                if (!FreeDCodec.TryDecode(data, out FreeDPacket p)) continue;
                if (cameraId != 0 && p.CameraId != cameraId) continue;
                double now = _sw.Elapsed.TotalMilliseconds;
                lock (_lock)
                {
                    _latest = p; _has = true; _count++;
                    if (_lastRx >= 0) { double d = now - _lastRx; _sumInterval += d; if (d > _maxInterval) _maxInterval = d; }
                    _lastRx = now;
                }
            }
            catch (SocketException) { }
            catch (ObjectDisposedException) { return; }
        }
    }

    void Update()
    {
        FreeDPacket p; bool has;
        lock (_lock) { p = _latest; has = _has; _has = false; }

        _rateTimer += Time.unscaledDeltaTime;
        if (_rateTimer >= 1f)
        {
            lock (_lock)
            {
                packetsPerSecond = Mathf.RoundToInt(_count / _rateTimer);
                meanIntervalMs = _count > 1 ? (float)(_sumInterval / (_count - 1)) : 0f;
                maxIntervalMs = (float)_maxInterval;
                _count = 0; _sumInterval = 0; _maxInterval = 0;
            }
            _rateTimer = 0f;
        }
        if (!has) { receiving = Time.unscaledTime - _lastPacketTime < 0.5f; return; }
        _lastPacketTime = Time.unscaledTime;
        receiving = true;

        pan = p.PanDegrees; tilt = p.TiltDegrees; roll = p.RollDegrees;
        xMm = p.XMeters * 1000; yMm = p.YMeters * 1000; zMm = p.ZMeters * 1000;

        // retroloc 座標 (X右 Y前 Z上) の姿勢。AngleAxis は数値的に標準の回転なので、そのまま合成できる。
        Quaternion q = Rot((float)p.PanDegrees * panSign, (float)p.TiltDegrees * tiltSign, (float)p.RollDegrees * rollSign);
        Vector3 pos = new Vector3((float)p.XMeters, (float)p.YMeters, (float)p.ZMeters);

        // トラッカー座標でのオフセットを適用してからUnityへ変換
        Quaternion qo = Rot(rotationOffset.x, rotationOffset.y, rotationOffset.z);
        pos += q * positionOffset;
        q = q * qo;

        transform.localPosition = new Vector3(pos.x, pos.z, pos.y);
        transform.localRotation = new Quaternion(-q.x, -q.z, -q.y, q.w);
    }

    static Quaternion Rot(float pan, float tilt, float roll)
    {
        return Quaternion.AngleAxis(pan, new Vector3(0, 0, 1))
             * Quaternion.AngleAxis(tilt, new Vector3(1, 0, 0))
             * Quaternion.AngleAxis(roll, new Vector3(0, 1, 0));
    }

    void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 420, 60),
            (receiving ? "FreeD 受信中 " : "FreeD 待機 ") + packetsPerSecond + " pkt/s  間隔 平均" + meanIntervalMs.ToString("F1") + " 最大" + maxIntervalMs.ToString("F1") + " ms\n" +
            string.Format("位置 mm {0:F1} {1:F1} {2:F1}\n角度 ° {3:F2} {4:F2} {5:F2}", xMm, yMm, zMm, pan, tilt, roll));
    }
}
