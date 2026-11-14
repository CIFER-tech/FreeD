using System;
using UnityEngine;

/// <summary>Receiver (ポート) 1 つの受信状態。FreeDReceiver が持ち、Inspector に出す。</summary>
[Serializable]
public class FreeDReceiverStatus
{
    /// <summary>この時間 (ms) パケットが来ない Receiver を、途切れとみなす。</summary>
    private const double TimeoutMs = 500;

    /// <summary>pkt/s と受信間隔を集計しなおす間隔。</summary>
    private const float RatePeriodSeconds = 1f;

    [FreeDReadOnly] public bool isReceiving;
    [FreeDReadOnly] public int packetsPerSecond;
    [Tooltip("直近1秒の受信間隔 (ms)"), FreeDReadOnly]
    public float meanIntervalMs, maxIntervalMs;

    // メインスレッドだけが触る (FreeDReceiver.TryDequeue と Update)。
    [NonSerialized] private int _count;
    [NonSerialized] private double _sumIntervalMs, _maxIntervalMsSoFar, _lastReceivedMs = double.NegativeInfinity;
    [NonSerialized] private float _rateTimerSeconds;

    /// <summary>パケットを 1 つ受けたことを記録する。</summary>
    internal void RecordPacket(double receivedMs)
    {
        _count++;
        if (_lastReceivedMs > double.NegativeInfinity)
        {
            double intervalMs = receivedMs - _lastReceivedMs;
            _sumIntervalMs += intervalMs;
            if (intervalMs > _maxIntervalMsSoFar)
            {
                _maxIntervalMsSoFar = intervalMs;
            }
        }
        _lastReceivedMs = receivedMs;
    }

    /// <summary>毎フレーム呼ぶ。途切れの判定を更新し、1 秒たつたびに pkt/s と受信間隔を出して、集計をやり直す。</summary>
    internal void Update(double nowMs, float deltaSeconds)
    {
        isReceiving = nowMs - _lastReceivedMs < TimeoutMs;

        _rateTimerSeconds += deltaSeconds;
        if (_rateTimerSeconds < RatePeriodSeconds)
        {
            return;
        }
        packetsPerSecond = Mathf.RoundToInt(_count / _rateTimerSeconds);
        meanIntervalMs = _count > 1 ? (float)(_sumIntervalMs / (_count - 1)) : 0f;
        maxIntervalMs = (float)_maxIntervalMsSoFar;
        _count = 0;
        _sumIntervalMs = 0;
        _maxIntervalMsSoFar = 0;
        _rateTimerSeconds = 0f;
    }

    /// <summary>受信を止めたときに、記録も表示も空に戻す。</summary>
    internal void Reset()
    {
        isReceiving = false;
        packetsPerSecond = 0;
        meanIntervalMs = 0f;
        maxIntervalMs = 0f;
        _count = 0;
        _sumIntervalMs = 0;
        _maxIntervalMsSoFar = 0;
        _lastReceivedMs = double.NegativeInfinity;
        _rateTimerSeconds = 0f;
    }
}
