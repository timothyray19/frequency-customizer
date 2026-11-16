namespace FrequencyCustomizer.Tuning;

/// <summary>
/// Pure helpers for converting a target frequency into a pitch-shift ratio
/// relative to standard A4 = 440 Hz equal temperament.
/// </summary>
public static class TuningMath
{
    public const double StandardA4 = 440.0;
    public const int A4Midi = 69;

    private static readonly string[] Names =
        { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    /// <summary>Frequency of a MIDI note in standard 440 Hz tuning.</summary>
    public static double NoteFrequency(int midi) =>
        StandardA4 * Math.Pow(2.0, (midi - A4Midi) / 12.0);

    /// <summary>Name like "A4" or "D#5" for a MIDI note.</summary>
    public static string NoteName(int midi) =>
        Names[((midi % 12) + 12) % 12] + (midi / 12 - 1);

    /// <summary>MIDI note closest (in cents) to the given frequency.</summary>
    public static int NearestNote(double hz)
    {
        if (hz <= 0) throw new ArgumentOutOfRangeException(nameof(hz));
        return (int)Math.Round(A4Midi + 12.0 * Math.Log2(hz / StandardA4));
    }

    /// <summary>Pitch ratio that moves the reference note onto the target frequency.</summary>
    public static double Ratio(double targetHz, int referenceMidi) =>
        targetHz / NoteFrequency(referenceMidi);

    public static double Cents(double ratio) => 1200.0 * Math.Log2(ratio);

    public static double RatioFromCents(double cents) => Math.Pow(2.0, cents / 1200.0);
}

public enum TuningPreset { Off440, Hz432, Hz528, Hz639, Custom }

public sealed record TuningTarget(double TargetHz, int ReferenceMidi)
{
    public double Ratio => TuningMath.Ratio(TargetHz, ReferenceMidi);
    public double Cents => TuningMath.Cents(Ratio);
    public string ReferenceName => TuningMath.NoteName(ReferenceMidi);
    public double ReferenceHz => TuningMath.NoteFrequency(ReferenceMidi);

    public string Describe() => Math.Abs(Cents) < 0.005
        ? "Standard tuning (A4 = 440 Hz), no pitch change"
        : $"{ReferenceName} {ReferenceHz:0.##} Hz → {TargetHz:0.##} Hz   ·   {Cents:+0.0;-0.0} cents   ·   {Ratio * 100:0.00}%";

    public static TuningTarget ForPreset(TuningPreset preset, double customHz = 432, int? customRef = null) => preset switch
    {
        TuningPreset.Off440 => new(440, 69),                       // A4, no shift
        TuningPreset.Hz432 => new(432, 69),                        // A4 440 -> 432
        TuningPreset.Hz528 => new(528, 72),                        // C5 523.25 -> 528 (A = 444)
        TuningPreset.Hz639 => new(639, TuningMath.NearestNote(639)), // D#5 622.25 -> 639
        _ => new(customHz, customRef ?? TuningMath.NearestNote(customHz)),
    };
}
