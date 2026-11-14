using System;
using UnityEngine;

/// <summary>Receiver (ポート) ごとの受信状態。FreeDManager が集計して、Inspector に出す。</summary>
[Serializable]
public class FreeDReceiverStatus
{
    /// <summary>この時間 (ms) パケットが来ない Receiver を、途切れとみなす。</summary>
    private const double TimeoutMs = 500;

    public int port;
    public bool isReceiving;
    public int packetsPerSecond;
    [Tooltip("直近1秒の受信間隔 (ms)")]
    public float meanIntervalMs, maxIntervalMs;

    // 受信タスクが書き、Update が読む。FreeDManager の _lock で守る。
    [NonSerialized] private int _count;
    [NonSerialized] private double _sumIntervalMs, _maxIntervalMsSoFar, _lastReceivedMs = double.NegativeInfinity;

    internal void RecordPacket(double nowMs)
    {
        _count++;
        if (_lastReceivedMs > double.NegativeInfinity)
        {
            double intervalMs = nowMs - _lastReceivedMs;
            _sumIntervalMs += intervalMs;
            if (intervalMs > _maxIntervalMsSoFar) _maxIntervalMsSoFar = intervalMs;
        }
        _lastReceivedMs = nowMs;
    }

    internal void UpdateIsReceiving(double nowMs) { isReceiving = nowMs - _lastReceivedMs < TimeoutMs; }

    /// <summary>1 秒ぶんの受信間隔を出して、集計をやり直す。</summary>
    internal void PublishRates(float elapsedSeconds)
    {
        packetsPerSecond = Mathf.RoundToInt(_count / elapsedSeconds);
        meanIntervalMs = _count > 1 ? (float)(_sumIntervalMs / (_count - 1)) : 0f;
        maxIntervalMs = (float)_maxIntervalMsSoFar;
        _count = 0; _sumIntervalMs = 0; _maxIntervalMsSoFar = 0;
    }
}
