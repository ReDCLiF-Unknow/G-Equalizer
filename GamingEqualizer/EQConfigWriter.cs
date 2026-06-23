namespace GamingEqualizer;

public class EQConfigWriter
{
    private static readonly string EqApoDir = @"C:\Program Files\EqualizerAPO\config";
    private static readonly string ConfigPath = Path.Combine(EqApoDir, "config.txt");

    // Fallback path if Program Files write fails despite UAC
    private static readonly string FallbackDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GamingEqualizer");
    private static readonly string FallbackConfigPath = Path.Combine(FallbackDir, "eq_config.txt");
    private static readonly string FallbackIncludePath = Path.Combine(EqApoDir, "geq_include.txt");

    private static readonly int[] BandFrequencies = { 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    public static bool IsEqualizerApoInstalled() => Directory.Exists(@"C:\Program Files\EqualizerAPO\");

    public void Apply(float[] bands)
    {
        var lines = BuildConfig(bands);
        WriteWithFallback(lines);
    }

    public void Bypass()
    {
        var lines = new[] { "Preamp: 0 dB" };
        WriteWithFallback(lines);
    }

    private string[] BuildConfig(float[] bands)
    {
        var lines = new List<string> { "Preamp: -6 dB" };
        for (int i = 0; i < bands.Length && i < BandFrequencies.Length; i++)
        {
            float gain = Math.Clamp(bands[i], -12f, 12f);
            lines.Add($"Filter {i + 1}: ON PK Fc {BandFrequencies[i]} Hz Gain {gain:F1} dB Q 1.41");
        }
        return lines.ToArray();
    }

    private void WriteWithFallback(string[] lines)
    {
        if (TryWrite(ConfigPath, lines))
            return;

        // Retry once after 200ms
        Thread.Sleep(200);
        if (TryWrite(ConfigPath, lines))
            return;

        // Fallback: write to user-writable path, chain via Include
        TryWriteFallback(lines);
    }

    private bool TryWrite(string path, string[] lines)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllLines(path, lines);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Config write failed ({path}): {ex.Message}");
            return false;
        }
    }

    private void TryWriteFallback(string[] lines)
    {
        try
        {
            Directory.CreateDirectory(FallbackDir);
            File.WriteAllLines(FallbackConfigPath, lines);

            // Write an Include directive into the EqualizerAPO config dir
            File.WriteAllText(FallbackIncludePath, $"Include: {FallbackConfigPath}");
            Logger.Log("Used fallback Include directive path.");
        }
        catch (Exception ex)
        {
            Logger.Log($"Fallback config write also failed: {ex.Message}");
            throw new InvalidOperationException("Cannot write EQ config. Check EqualizerAPO installation and permissions.", ex);
        }
    }
}
