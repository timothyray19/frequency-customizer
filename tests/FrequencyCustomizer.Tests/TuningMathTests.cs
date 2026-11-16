using FrequencyCustomizer.Tuning;

namespace FrequencyCustomizer.Tests;

public class TuningMathTests
{
    [Fact]
    public void A4_is_440() => Assert.Equal(440.0, TuningMath.NoteFrequency(69), 6);

    [Theory]
    [InlineData(69, "A4")]
    [InlineData(72, "C5")]
    [InlineData(75, "D#5")]
    [InlineData(60, "C4")]
    public void Note_names(int midi, string name) => Assert.Equal(name, TuningMath.NoteName(midi));

    [Fact]
    public void Preset_432_shifts_A4_down_31_77_cents()
    {
        var t = TuningTarget.ForPreset(TuningPreset.Hz432);
        Assert.Equal(69, t.ReferenceMidi);
        Assert.Equal(0.981818, t.Ratio, 5);
        Assert.Equal(-31.77, t.Cents, 2);
    }

    [Fact]
    public void Preset_528_maps_C5_and_equals_A444()
    {
        var t = TuningTarget.ForPreset(TuningPreset.Hz528);
        Assert.Equal(72, t.ReferenceMidi);
        Assert.Equal(15.64, t.Cents, 2);
        Assert.Equal(444.0, 440 * t.Ratio, 1);
    }

    [Fact]
    public void Preset_639_uses_nearest_note_DSharp5()
    {
        var t = TuningTarget.ForPreset(TuningPreset.Hz639);
        Assert.Equal(75, t.ReferenceMidi);
        Assert.Equal(46.0, t.Cents, 1);
    }

    [Fact]
    public void Off_is_unity() => Assert.Equal(1.0, TuningTarget.ForPreset(TuningPreset.Off440).Ratio, 12);

    [Fact]
    public void Cents_roundtrip() => Assert.Equal(1.05, TuningMath.RatioFromCents(TuningMath.Cents(1.05)), 12);
}
