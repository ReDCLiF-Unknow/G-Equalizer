using Newtonsoft.Json;

namespace GamingEqualizer.Models;

public class HearingProfile
{
    public float[] Frequencies { get; set; } = Array.Empty<float>();
    public float[] Thresholds { get; set; } = Array.Empty<float>();
    public string Headphone { get; set; } = "";
    public string Date { get; set; } = "";

    private static readonly string ProfilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GamingEqualizer", "HearingProfile.json");

    public static HearingProfile? Load()
    {
        try
        {
            if (File.Exists(ProfilePath))
            {
                var json = File.ReadAllText(ProfilePath);
                return JsonConvert.DeserializeObject<HearingProfile>(json);
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to load HearingProfile: {ex.Message}");
        }
        return null;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(ProfilePath)!;
            Directory.CreateDirectory(dir);
            var json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(ProfilePath, json);
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to save HearingProfile: {ex.Message}");
        }
    }
}
