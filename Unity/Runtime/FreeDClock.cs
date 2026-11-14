using System.Diagnostics;

namespace CIFER.tech.FreeD.Unity
{
    /// <summary>
    /// FreeD の受信時刻と、合成の時刻をそろえる時計。
    /// FreeDReceiver がパケットを受けた時刻と、FreeDManager が Update で合成する時刻を、同じ時計で測る。
    /// DateTime は OS の時刻合わせで値が飛ぶことがあるので、Stopwatch を使う。
    /// </summary>
    public static class FreeDClock
    {
        private static readonly Stopwatch Timer = Stopwatch.StartNew();

        /// <summary>この時計を初めて使ってからの時間 (ms)。どのスレッドからでも読める。</summary>
        public static double NowMs
        {
            get
            {
                return Timer.Elapsed.TotalMilliseconds;
            }
        }
    }
}
