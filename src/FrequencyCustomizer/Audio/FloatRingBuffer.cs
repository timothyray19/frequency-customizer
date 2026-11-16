namespace FrequencyCustomizer.Audio;

/// <summary>Thread-safe interleaved float FIFO shared by the capture and playback threads.</summary>
public sealed class FloatRingBuffer
{
    private readonly float[] _buf;
    private readonly object _lock = new();
    private int _read, _count;

    public FloatRingBuffer(int capacity) => _buf = new float[capacity];

    public int Capacity => _buf.Length;

    public int Count { get { lock (_lock) return _count; } }

    /// <summary>Appends samples; if full, the oldest samples are dropped.</summary>
    public void Write(ReadOnlySpan<float> data)
    {
        lock (_lock)
        {
            if (data.Length >= _buf.Length)
            {
                data[^_buf.Length..].CopyTo(_buf);
                _read = 0; _count = _buf.Length;
                return;
            }
            int overflow = _count + data.Length - _buf.Length;
            if (overflow > 0) Skip(overflow);

            int write = (_read + _count) % _buf.Length;
            int first = Math.Min(data.Length, _buf.Length - write);
            data[..first].CopyTo(_buf.AsSpan(write));
            data[first..].CopyTo(_buf.AsSpan(0));
            _count += data.Length;
        }
    }

    /// <summary>Reads up to dest.Length samples, returns the number read.</summary>
    public int Read(Span<float> dest)
    {
        lock (_lock)
        {
            int n = Math.Min(dest.Length, _count);
            int first = Math.Min(n, _buf.Length - _read);
            _buf.AsSpan(_read, first).CopyTo(dest);
            _buf.AsSpan(0, n - first).CopyTo(dest[first..]);
            _read = (_read + n) % _buf.Length;
            _count -= n;
            return n;
        }
    }

    /// <summary>Drops the oldest samples so at most <paramref name="keep"/> remain.</summary>
    public void TrimTo(int keep)
    {
        lock (_lock) { if (_count > keep) Skip(_count - keep); }
    }

    public void Clear() { lock (_lock) { _read = 0; _count = 0; } }

    private void Skip(int n)
    {
        _read = (_read + n) % _buf.Length;
        _count -= n;
    }
}
