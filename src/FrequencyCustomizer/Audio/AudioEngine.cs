using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace FrequencyCustomizer.Audio;

/// <summary>
/// Live pipeline: WASAPI capture (e.g. "CABLE Output") → PitchProcessor → ring buffer → WASAPI playback (real speakers).
/// </summary>
public sealed class AudioEngine : IDisposable
{
    private const int TargetBufferMs = 50;   // steady-state playback cushion
    private const int MaxBufferMs = 250;     // beyond this we drop audio to catch up
    private const double MaxTempoTrim = 0.002; // ±0.2 % drift correction

    private static readonly Guid FloatSubType = new("00000003-0000-0010-8000-00aa00389b71");
    private static readonly Guid PcmSubType = new("00000001-0000-0010-8000-00aa00389b71");

    private WasapiCapture? _capture;
    private WasapiOut? _player;
    private PitchProcessor? _proc;
    private FloatRingBuffer? _ring;
    private RingWaveProvider? _provider;
    private WaveFormat? _inFormat;
    private float[] _inFloats = new float[8192];
    private int _targetSamples, _maxSamples, _outRate, _outCh;
    private double _smoothedLevel;
    private volatile bool _stopping;

    private double _ratio = 1.0;
    private volatile bool _bypass;

    public event Action<string>? Faulted;

    public bool IsRunning => _player != null;

    public double Ratio { get => _ratio; set => Interlocked.Exchange(ref _ratio, value); }
    public bool Bypass { get => _bypass; set => _bypass = value; }

    /// <summary>Total extra delay added by the app (buffer + pitch shifter), in ms.</summary>
    public double LatencyMs
    {
        get
        {
            var ring = _ring; var proc = _proc; var fmt = _inFormat;
            if (ring == null || proc == null || fmt == null || _outCh == 0) return 0;
            return ring.Count / (double)_outCh / _outRate * 1000.0
                   + proc.LatencyFrames / (double)fmt.SampleRate * 1000.0;
        }
    }

    public int Underruns => _provider?.Underruns ?? 0;

    public string FormatInfo { get; private set; } = "";

    public void Start(MMDevice input, MMDevice output)
    {
        Stop();
        _stopping = false;

        _capture = new WasapiCapture(input, true, 10);
        _inFormat = _capture.WaveFormat;
        if (!IsFloat(_inFormat) && !IsPcm(_inFormat))
            throw new NotSupportedException($"Unsupported capture format: {_inFormat}");

        var mix = output.AudioClient.MixFormat;
        _outRate = mix.SampleRate;
        _outCh = mix.Channels;
        var outFormat = WaveFormat.CreateIeeeFloatWaveFormat(_outRate, _outCh);

        _targetSamples = _outRate * TargetBufferMs / 1000 * _outCh;
        _maxSamples = _outRate * MaxBufferMs / 1000 * _outCh;
        _ring = new FloatRingBuffer(_outRate * 2 * _outCh); // 2 s capacity
        _provider = new RingWaveProvider(_ring, outFormat, _targetSamples);
        _proc = new PitchProcessor(_inFormat.SampleRate, _inFormat.Channels, _outRate, _outCh);
        _smoothedLevel = _targetSamples;

        FormatInfo = $"{_inFormat.SampleRate / 1000.0:0.#} kHz {_inFormat.Channels}ch → {_outRate / 1000.0:0.#} kHz {_outCh}ch";

        _player = new WasapiOut(output, AudioClientShareMode.Shared, true, 30);
        _player.Init(_provider);
        _player.PlaybackStopped += OnStopped;

        _capture.DataAvailable += OnData;
        _capture.RecordingStopped += OnStopped;

        _player.Play();
        _capture.StartRecording();
    }

    public void Stop()
    {
        _stopping = true;
        var cap = _capture; var player = _player;
        _capture = null; _player = null;
        if (cap != null)
        {
            cap.DataAvailable -= OnData;
            cap.RecordingStopped -= OnStopped;
            try { cap.StopRecording(); } catch { }
            cap.Dispose();
        }
        if (player != null)
        {
            player.PlaybackStopped -= OnStopped;
            try { player.Stop(); } catch { }
            player.Dispose();
        }
        _proc = null; _ring = null; _provider = null; _inFormat = null;
    }

    private void OnStopped(object? sender, StoppedEventArgs e)
    {
        if (_stopping) return;
        Faulted?.Invoke(e.Exception?.Message ?? "The audio device stopped (unplugged or changed format).");
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        var proc = _proc; var ring = _ring; var fmt = _inFormat; var provider = _provider;
        if (proc == null || ring == null || fmt == null || provider == null || e.BytesRecorded == 0) return;

        try
        {
            int frames = ConvertToFloat(e.Buffer, e.BytesRecorded, fmt);

            proc.Pitch = _bypass ? 1.0 : Volatile.Read(ref _ratio);

            // Clock-drift control: keep the playback buffer near its target by
            // speeding up / slowing down imperceptibly (max ±0.2 %).
            _smoothedLevel = _smoothedLevel * 0.98 + ring.Count * 0.02;
            if (provider.Primed)
            {
                double err = (_smoothedLevel - _targetSamples) / _targetSamples;
                proc.Tempo = 1.0 + Math.Clamp(err * MaxTempoTrim, -MaxTempoTrim, MaxTempoTrim);
            }

            var output = proc.Process(_inFloats.AsSpan(0, frames * fmt.Channels), frames);
            ring.Write(output);

            if (ring.Count > _maxSamples) { ring.TrimTo(_targetSamples); _smoothedLevel = _targetSamples; }
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(ex.Message);
        }
    }

    private int ConvertToFloat(byte[] buffer, int bytes, WaveFormat fmt)
    {
        int bytesPerSample = fmt.BitsPerSample / 8;
        int samples = bytes / bytesPerSample;
        if (_inFloats.Length < samples) _inFloats = new float[samples * 2];
        var dst = _inFloats.AsSpan(0, samples);
        var src = buffer.AsSpan(0, samples * bytesPerSample);

        if (IsFloat(fmt))
        {
            MemoryMarshal.Cast<byte, float>(src).CopyTo(dst);
        }
        else switch (fmt.BitsPerSample)
        {
            case 16:
                var s16 = MemoryMarshal.Cast<byte, short>(src);
                for (int i = 0; i < samples; i++) dst[i] = s16[i] / 32768f;
                break;
            case 24:
                for (int i = 0; i < samples; i++)
                {
                    int v = (src[i * 3] << 8) | (src[i * 3 + 1] << 16) | (src[i * 3 + 2] << 24);
                    dst[i] = v / 2147483648f;
                }
                break;
            case 32:
                var s32 = MemoryMarshal.Cast<byte, int>(src);
                for (int i = 0; i < samples; i++) dst[i] = s32[i] / 2147483648f;
                break;
            default:
                throw new NotSupportedException($"{fmt.BitsPerSample}-bit audio is not supported");
        }
        return samples / fmt.Channels;
    }

    private static bool IsFloat(WaveFormat f) =>
        (f.Encoding == WaveFormatEncoding.IeeeFloat ||
         (f is WaveFormatExtensible x && x.SubFormat == FloatSubType)) && f.BitsPerSample == 32;

    private static bool IsPcm(WaveFormat f) =>
        f.Encoding == WaveFormatEncoding.Pcm || (f is WaveFormatExtensible x && x.SubFormat == PcmSubType);

    public void Dispose() => Stop();

    /// <summary>Feeds the WASAPI player from the ring buffer; outputs silence until enough audio is buffered.</summary>
    private sealed class RingWaveProvider : IWaveProvider
    {
        private readonly FloatRingBuffer _ring;
        private readonly int _primeSamples;
        private volatile bool _primed;

        public RingWaveProvider(FloatRingBuffer ring, WaveFormat format, int primeSamples)
        {
            _ring = ring; WaveFormat = format; _primeSamples = primeSamples;
        }

        public WaveFormat WaveFormat { get; }
        public bool Primed => _primed;
        public int Underruns { get; private set; }

        public int Read(byte[] buffer, int offset, int count)
        {
            var floats = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, count));
            if (!_primed && _ring.Count >= _primeSamples) _primed = true;

            int got = _primed ? _ring.Read(floats) : 0;
            if (got < floats.Length)
            {
                floats[got..].Clear();
                if (_primed) { _primed = false; Underruns++; }
            }
            return count;
        }
    }
}
