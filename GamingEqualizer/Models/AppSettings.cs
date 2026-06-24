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

    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GamingEqualizer");
    private static readonly string SettingsPath = Path.Combine(SettingsDir, "AppSettings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                return JsonConvert.DeserializeObject<AppSettings>(json) ?? new AppSettings();
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
