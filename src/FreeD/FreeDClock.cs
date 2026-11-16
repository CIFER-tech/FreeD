using System.Diagnostics;

namespace CIFER.tech.FreeD.Core
{
    /// <summary>受信時刻と合成の時刻を、同じ時計で測る。DateTime は OS の時刻合わせで値が飛ぶことがあるので、Stopwatch を使う。</summary>
    public static class FreeDClock
    {
        private static readonly Stopwatch Timer = Stopwatch.StartNew();

        /// <summary>どのスレッドからでも読める。</summary>
        public static double NowMs
        {
            get
            {
                return Timer.Elapsed.TotalMilliseconds;
            }
        }
    }
}
