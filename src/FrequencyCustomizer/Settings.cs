using System.Text.Json;
using FrequencyCustomizer.Tuning;

namespace FrequencyCustomizer;

public sealed class Settings
{
    public TuningPreset Preset { get; set; } = TuningPreset.Hz432;
    public double CustomHz { get; set; } = 432;
    public int CustomReferenceMidi { get; set; } = 69;
    public string? InputDeviceId { get; set; }
    public string? OutputDeviceId { get; set; }
    public bool AutoSwitchDefault { get; set; } = true;
    public bool StartOnLaunch { get; set; }

    /// <summary>Speakers to restore if the app closed while CABLE was the default device.</summary>
    public string? RestoreDeviceId { get; set; }

    private static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FrequencyCustomizer");
    private static string FilePath => Path.Combine(Dir, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* corrupt file: start fresh */ }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* non-fatal */ }
    }
}
