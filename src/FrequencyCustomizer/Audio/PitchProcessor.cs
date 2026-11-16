using SoundTouch;

namespace FrequencyCustomizer.Audio;

/// <summary>
/// Device-independent DSP: pitch shift (tempo unchanged), sample-rate conversion
/// between the capture and playback devices, and channel mapping.
/// Used by <see cref="AudioEngine"/> and by the offline tests.
/// </summary>
public sealed class PitchProcessor
{
    private readonly SoundTouchProcessor _st = new();
    private readonly int _inCh, _outCh;
    private float[] _recv = new float[8192];
    private float[] _mapped = new float[8192];
    private double _pitch = 1.0, _tempo = 1.0;

    public PitchProcessor(int inRate, int inChannels, int outRate, int outChannels)
    {
        _inCh = inChannels;
        _outCh = outChannels;
        _st.SampleRate = inRate;
        _st.Channels = inChannels;
        _st.Tempo = 1.0;
        _st.Pitch = 1.0;
        _st.Rate = (double)inRate / outRate; // pure resampling to the output device rate
        // Shorter WSOLA windows: lower latency, still clean for shifts under a semitone.
        _st.SetSetting(SettingId.SequenceDurationMs, 40);
        _st.SetSetting(SettingId.SeekWindowDurationMs, 15);
        _st.SetSetting(SettingId.OverlapDurationMs, 8);
    }

    public int OutputChannels => _outCh;

    /// <summary>Pitch ratio (1.0 = unchanged, 432/440 = 0.9818…).</summary>
    public double Pitch
    {
        get => _pitch;
        set { if (Math.Abs(value - _pitch) > 1e-9) { _pitch = value; _st.Pitch = value; } }
    }

    /// <summary>Tempo factor used only for tiny clock-drift corrections (≈1.0).</summary>
    public double Tempo
    {
        get => _tempo;
        set { if (Math.Abs(value - _tempo) > 1e-5) { _tempo = value; _st.Tempo = value; } }
    }

    /// <summary>Approximate delay inside SoundTouch, in input frames.</summary>
    public int LatencyFrames => _st.GetSetting(SettingId.InitialLatency);

    /// <summary>
    /// Feeds interleaved input frames and returns whatever processed output is ready,
    /// interleaved with the output channel count. The returned span is valid until the next call.
    /// </summary>
    public ReadOnlySpan<float> Process(ReadOnlySpan<float> input, int frames)
    {
        if (frames > 0) _st.PutSamples(input, frames);

        int total = 0;
        while (true)
        {
            EnsureSize(ref _recv, (total + 2048) * _inCh);
            int got = _st.ReceiveSamples(_recv.AsSpan(total * _inCh), 2048);
            if (got <= 0) break;
            total += got;
        }
        return MapChannels(_recv.AsSpan(0, total * _inCh), total);
    }

    public void Clear() => _st.Clear();

    private ReadOnlySpan<float> MapChannels(ReadOnlySpan<float> src, int frames)
    {
        if (_inCh == _outCh) return src;
        EnsureSize(ref _mapped, frames * _outCh);
        var dst = _mapped.AsSpan(0, frames * _outCh);
        for (int f = 0; f < frames; f++)
        {
            var i = src.Slice(f * _inCh, _inCh);
            var o = dst.Slice(f * _outCh, _outCh);
            if (_inCh == 1) o.Fill(i[0]);
            else if (_outCh == 1) { float s = 0; foreach (var v in i) s += v; o[0] = s / _inCh; }
            else for (int c = 0; c < _outCh; c++) o[c] = c < _inCh ? i[c] : 0f; // extra speakers silent
        }
        return dst;
    }

    private static void EnsureSize(ref float[] arr, int size)
    {
        if (arr.Length < size) Array.Resize(ref arr, Math.Max(size, arr.Length * 2));
    }
}
