/// <summary>FreeDManager が、あるフレームに Receiver から取り出したパケットと、取り出した Receiver の番号。</summary>
internal struct FreeDPolledPacket
{
    public int receiverIndex;
    public FreeDReceivedPacket received;
}
