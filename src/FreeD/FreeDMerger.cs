using System;
using System.Collections.Generic;

namespace FreeD
{
    /// <summary>合成の対象になる項目。ストリームごとに、どの項目を使うかを選ぶ。</summary>
    [Flags]
    public enum FreeDFields
    {
        None = 0,
        Rotation = 1, // パン・チルト・ロール
        Position = 2, // X・Y・Z
        Zoom = 4,
        Focus = 8,
    }

    /// <summary>
    /// 複数の FreeD ストリーム (例: retroloc の姿勢と、レンズエンコーダのズーム・フォーカス) を 1 つの <see cref="FreeDPacket"/> にまとめる。
    /// 項目ごとに、担当するストリームで最後に届いた値を使う。途切れても値は保つ。
    /// ストリームごとに遅延を設定でき、早く届く側を遅らせて他のストリームとそろえられる。
    /// スレッドに対して安全ではない。複数のスレッドから呼ぶときは、呼び出し側でロックする。
    /// </summary>
    public sealed class FreeDMerger
    {
        private struct Pending
        {
            public double ReceivedMs;
            public FreeDPacket Packet;
        }

        private readonly FreeDFields[] _fieldsPerStream;
        private readonly double[] _delayMsPerStream;
        private readonly double[] _lastReceivedMs;
        private readonly Queue<Pending>[] _pendingPerStream;
        private FreeDPacket _merged;
        private FreeDFields _filled = FreeDFields.None;

        /// <summary>この時間 (ms) パケットが来ないストリームを、途切れとみなす。</summary>
        public double TimeoutMs { get; set; } = 500;

        /// <param name="fieldsPerStream">ストリームごとの担当項目。同じ項目を 2 つのストリームが担当してはいけない。</param>
        /// <exception cref="ArgumentException">担当項目が重なっているとき。</exception>
        public FreeDMerger(IReadOnlyList<FreeDFields> fieldsPerStream)
        {
            var claimed = FreeDFields.None;
            _fieldsPerStream = new FreeDFields[fieldsPerStream.Count];
            for (int i = 0; i < _fieldsPerStream.Length; i++)
            {
                var fields = fieldsPerStream[i];
                if ((claimed & fields) != FreeDFields.None)
                    throw new ArgumentException("同じ項目を複数のストリームが担当している: " + (claimed & fields), nameof(fieldsPerStream));
                claimed |= fields;
                _fieldsPerStream[i] = fields;
            }

            _delayMsPerStream = new double[_fieldsPerStream.Length];
            _lastReceivedMs = new double[_fieldsPerStream.Length];
            _pendingPerStream = new Queue<Pending>[_fieldsPerStream.Length];
            for (int i = 0; i < _pendingPerStream.Length; i++)
            {
                _lastReceivedMs[i] = double.NegativeInfinity;
                _pendingPerStream[i] = new Queue<Pending>();
            }
        }

        public void SetDelay(int streamIndex, double delayMs)
        {
            _delayMsPerStream[streamIndex] = Math.Max(0, delayMs);
        }

        /// <param name="receivedMs">受信した時刻 (ms)。<see cref="Update"/> の nowMs と同じ時計で測る。</param>
        public void Put(int streamIndex, in FreeDPacket packet, double receivedMs)
        {
            _lastReceivedMs[streamIndex] = receivedMs;
            _pendingPerStream[streamIndex].Enqueue(new Pending { ReceivedMs = receivedMs, Packet = packet });
        }

        /// <summary>遅延が過ぎたパケットを取り込み、合成した値を返す。</summary>
        /// <returns>1 度でも値が入った項目。含まれない項目は <paramref name="merged"/> の中身を使ってはいけない。</returns>
        public FreeDFields Update(double nowMs, out FreeDPacket merged)
        {
            for (int i = 0; i < _pendingPerStream.Length; i++)
            {
                var pending = _pendingPerStream[i];
                while (pending.Count > 0 && pending.Peek().ReceivedMs + _delayMsPerStream[i] <= nowMs)
                    Apply(i, pending.Dequeue().Packet);
            }
            merged = _merged;
            return _filled;
        }

        public bool IsReceiving(int streamIndex, double nowMs)
        {
            return nowMs - _lastReceivedMs[streamIndex] < TimeoutMs;
        }

        private void Apply(int streamIndex, in FreeDPacket packet)
        {
            var fields = _fieldsPerStream[streamIndex];
            if ((fields & FreeDFields.Rotation) != 0)
            {
                _merged.PanDegrees = packet.PanDegrees;
                _merged.TiltDegrees = packet.TiltDegrees;
                _merged.RollDegrees = packet.RollDegrees;
                _merged.CameraId = packet.CameraId;
                _merged.UserData = packet.UserData;
            }
            if ((fields & FreeDFields.Position) != 0)
            {
                _merged.XMeters = packet.XMeters;
                _merged.YMeters = packet.YMeters;
                _merged.ZMeters = packet.ZMeters;
            }
            if ((fields & FreeDFields.Zoom) != 0) _merged.ZoomRaw = packet.ZoomRaw;
            if ((fields & FreeDFields.Focus) != 0) _merged.FocusRaw = packet.FocusRaw;
            _filled |= fields;
        }
    }
}
