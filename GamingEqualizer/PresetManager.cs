using GamingEqualizer.Models;
using Newtonsoft.Json;

namespace GamingEqualizer;

public class PresetManager
{
    public List<Preset> Presets { get; } = new();

    private static readonly string PresetsDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Presets");

    public void Load()
    {
        Presets.Clear();
        if (!Directory.Exists(PresetsDir))
            return;

        foreach (var file in Directory.GetFiles(PresetsDir, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var preset = JsonConvert.DeserializeObject<Preset>(json);
                if (preset != null)
                    Presets.Add(preset);
            }
            catch (Exception ex)
            {
                Logger.Log($"Failed to load preset '{file}': {ex.Message}");
            }
        }

        // Ensure Flat exists as fallback
        if (!Presets.Any(p => p.Name == "Flat"))
            Presets.Add(new Preset { Name = "Flat", Bands = new float[10] });
    }

    public Preset? Get(string name) =>
        Presets.FirstOrDefault(p => p.Name == name) ?? Presets.FirstOrDefault(p => p.Name == "Flat");
}
