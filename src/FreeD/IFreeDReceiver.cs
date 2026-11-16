namespace CIFER.tech.FreeD.Core
{
    /// <summary>
    /// ClaimedFields と DelaySec は、Open の時点の値で動く。開いている間に変えると InvalidOperationException になるので、
    /// 変えるときは、Close してから変えて、Open し直す。
    /// </summary>
    public interface IFreeDReceiver
    {
        FreeDField ClaimedFields { get; set; }

        float DelaySec { get; set; }

        void Open();

        void Close();

        bool TryTakeNext(out FreeDReceivedPacket received);
    }
}
