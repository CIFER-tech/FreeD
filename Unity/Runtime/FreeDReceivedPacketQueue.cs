/// <summary>
/// 受信したパケットを積む、固定サイズの輪になった待ち行列。受信スレッドが積み、FreeDManager が Update で取り出す。
/// 要素の配列を最初に 1 度だけ作るので、積む・取り出すで GC が起きない (ConcurrentQueue は、一定数ごとに領域を作る)。
/// 満杯のときは、いちばん古いパケットを捨てる。
/// </summary>
internal class FreeDReceivedPacketQueue
{
    // 60 fps で数秒ぶん。FreeDManager が取りに来ない間も、メモリは増えない。
    private const int Capacity = 256;

    private readonly FreeDReceivedPacket[] _items = new FreeDReceivedPacket[Capacity];
    private readonly object _lock = new object();
    private int _head;
    private int _count;

    public void Enqueue(in FreeDReceivedPacket item)
    {
        lock (_lock)
        {
            int tail = (_head + _count) % Capacity;
            _items[tail] = item;
            if (_count == Capacity)
            {
                _head = (_head + 1) % Capacity;
            }
            else
            {
                _count++;
            }
        }
    }

    public bool TryDequeue(out FreeDReceivedPacket item)
    {
        lock (_lock)
        {
            if (_count == 0)
            {
                item = default;
                return false;
            }
            item = _items[_head];
            _head = (_head + 1) % Capacity;
            _count--;
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _head = 0;
            _count = 0;
        }
    }
}
