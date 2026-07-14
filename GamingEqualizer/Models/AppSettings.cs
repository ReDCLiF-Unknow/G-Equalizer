using Newtonsoft.Json;

namespace GamingEqualizer.Models;

public class AppSettings
{
    public float[] BandGains { get; set; } = new float[10];
    public bool EqEnabled { get; set; } = true;
    public string ActivePreset { get; set; } = "Flat";
    public bool LaunchWithWindows { get; set; } = false;
    public string DefaultPreset { get; set; } = "Flat";
    public float[]? LastCalibration { get; set; }        // kept for backward compat (average)
    public float[]? LastCalibrationLeft  { get; set; }
    public float[]? LastCalibrationRight { get; set; }
    public bool HasCompletedOnboarding { get; set; } = false;
    public float BoostDb { get; set; } = 0f;
    public bool BoostEnabled { get; set; } = false;
    // 0 = Gradient, 1 = Solid, 2 = Peak Glow
    public int VizColorMode { get; set; } = 0;
    public bool AutoPresetEnabled { get; set; } = false;
    public double WindowWidth { get; set; } = 820;
    public double WindowHeight { get; set; } = 610;
    public Dictionary<string, string> ProcessPresetMap { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs2.exe"]                        = "FPS",
        ["r5apex.exe"]                     = "FPS",
        ["VALORANT-Win64-Shipping.exe"]    = "FPS",
        ["RainbowSix.exe"]                 = "FPS",
        ["Spotify.exe"]                    = "Music",
    };

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingEqualizer");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "AppSettings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json     = File.ReadAllText(SettingsPath);
                var settings = JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
                // Re-apply OrdinalIgnoreCase comparer lost during JSON deserialization
                settings.ProcessPresetMap = new Dictionary<string, string>(
                    settings.ProcessPresetMap, StringComparer.OrdinalIgnoreCase);
                return settings;
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load AppSettings: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(SettingsPath, json);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save AppSettings: {ex.Message}");
        }
    }
}
