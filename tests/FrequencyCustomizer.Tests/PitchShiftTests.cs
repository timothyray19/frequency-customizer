using FrequencyCustomizer.Audio;
using FrequencyCustomizer.Tuning;

namespace FrequencyCustomizer.Tests;

/// <summary>Runs synthetic tones through the same DSP the live engine uses.</summary>
public class PitchShiftTests
{
    private const int Rate = 48000;

    private static float[] Sine(double hz, int seconds, int channels)
    {
        var buf = new float[Rate * seconds * channels];
        for (int i = 0; i < Rate * seconds; i++)
        {
            float v = (float)(0.5 * Math.Sin(2 * Math.PI * hz * i / Rate));
            for (int c = 0; c < channels; c++) buf[i * channels + c] = v;
        }
        return buf;
    }

    /// <summary>Feeds the signal in 10 ms blocks like WASAPI would, returns channel 0 of the output.</summary>
    private static List<float> Run(PitchProcessor p, float[] input, int inCh, int outCh)
    {
        var outCh0 = new List<float>();
        int block = Rate / 100;
        for (int f = 0; f + block <= input.Length / inCh; f += block)
        {
            var o = p.Process(input.AsSpan(f * inCh, block * inCh), block);
            for (int i = 0; i < o.Length; i += outCh) outCh0.Add(o[i]);
        }
        return outCh0;
    }

    /// <summary>Finds the dominant frequency in [lo, hi] by correlating against test sinusoids (0.05 Hz steps).</summary>
    private static double PeakHz(IReadOnlyList<float> x, int rate, double lo, double hi)
    {
        int start = x.Count / 4, len = rate; // 1 s analysis window after the start-up transient
        double best = 0, bestHz = 0;
        for (double hz = lo; hz <= hi; hz += 0.05)
        {
            double re = 0, im = 0, w = 2 * Math.PI * hz / rate;
            for (int n = 0; n < len; n++)
            {
                double win = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / (len - 1)); // Hann
                re += x[start + n] * win * Math.Cos(w * n);
                im += x[start + n] * win * Math.Sin(w * n);
            }
            double mag = re * re + im * im;
            if (mag > best) { best = mag; bestHz = hz; }
        }
        return bestHz;
    }

    [Theory]
    [InlineData(TuningPreset.Hz432, 440.0, 432.0)]
    [InlineData(TuningPreset.Hz528, 523.2511, 528.0)]
    [InlineData(TuningPreset.Hz639, 622.254, 639.0)]
    public void Shifts_reference_tone_onto_target(TuningPreset preset, double inHz, double expectedHz)
    {
        var p = new PitchProcessor(Rate, 2, Rate, 2) { Pitch = TuningTarget.ForPreset(preset).Ratio };
        var output = Run(p, Sine(inHz, 4, 2), 2, 2);
        double peak = PeakHz(output, Rate, expectedHz - 15, expectedHz + 15);
        Assert.InRange(peak, expectedHz - 1, expectedHz + 1);
    }

    [Fact]
    public void Keeps_duration_unchanged()
    {
        var p = new PitchProcessor(Rate, 2, Rate, 2) { Pitch = 432.0 / 440.0 };
        var output = Run(p, Sine(440, 4, 2), 2, 2);
        int expected = Rate * 4;
        // Only the start-up latency (<0.2 s) may be missing; no speed-up or slow-down.
        Assert.InRange(output.Count, expected - Rate / 5, expected + Rate / 100);
    }

    [Fact]
    public void Converts_44k1_mono_to_48k_stereo_without_changing_pitch()
    {
        const int inRate = 44100;
        var p = new PitchProcessor(inRate, 1, Rate, 2) { Pitch = 1.0 };
        var input = new float[inRate * 4];
        for (int i = 0; i < input.Length; i++) input[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 440 * i / inRate));

        var outCh0 = new List<float>();
        int block = inRate / 100;
        for (int f = 0; f + block <= input.Length; f += block)
        {
            var o = p.Process(input.AsSpan(f, block), block);
            for (int i = 0; i < o.Length; i += 2) outCh0.Add(o[i]);
        }
        Assert.InRange(PeakHz(outCh0, Rate, 425, 455), 439, 441);
    }

    [Fact]
    public void Ring_buffer_wraps_and_drops_oldest()
    {
        var r = new FloatRingBuffer(4);
        r.Write(new float[] { 1, 2, 3 });
        var tmp = new float[2];
        Assert.Equal(2, r.Read(tmp));
        r.Write(new float[] { 4, 5, 6, 7 }); // overflow: [3,4,5,6,7] -> keep last 4
        var all = new float[4];
        Assert.Equal(4, r.Read(all));
        Assert.Equal(new float[] { 4, 5, 6, 7 }, all);
    }
}
