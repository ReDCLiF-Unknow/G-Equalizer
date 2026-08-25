#pragma warning disable CA1416 // Windows-only; device enumeration is guarded by OperatingSystem.IsWindows()

using NAudio.CoreAudioApi;

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

    public void Apply(float[] bands, float boostDb = 0f)
    {
        var lines = BuildConfig(bands, boostDb);
        WriteWithFallback(lines);
    }

    public void ApplyPerEar(float[] leftBands, float[] rightBands, float boostDb = 0f)
    {
        var lines = BuildPerEarConfig(leftBands, rightBands, boostDb);
        WriteWithFallback(lines);
    }

    public void Bypass()
    {
        var lines = new[] { "Preamp: 0 dB" };
        WriteWithFallback(lines);
    }

    private string[] BuildPerEarConfig(float[] left, float[] right, float boostDb = 0f)
    {
        float preamp = -6f + Math.Clamp(boostDb, 0f, 20f);
        var lines = new List<string>();

        AddRenderDeviceScope(lines);
        lines.Add($"Preamp: {preamp:+0.#;-0.#;0} dB");

        lines.Add("Channel: L");
        for (int i = 0; i < left.Length && i < BandFrequencies.Length; i++)
        {
            float gain = Math.Clamp(left[i], -12f, 12f);
            lines.Add($"Filter {i + 1}: ON PK Fc {BandFrequencies[i]} Hz Gain {gain:F1} dB Q 1.41");
        }

        lines.Add("Channel: R");
        for (int i = 0; i < right.Length && i < BandFrequencies.Length; i++)
        {
            float gain = Math.Clamp(right[i], -12f, 12f);
            lines.Add($"Filter {i + 1}: ON PK Fc {BandFrequencies[i]} Hz Gain {gain:F1} dB Q 1.41");
        }

        lines.Add("Channel: ALL");
        ResetDeviceScope(lines);
        return lines.ToArray();
    }

    private string[] BuildConfig(float[] bands, float boostDb = 0f)
    {
        float preamp = -6f + Math.Clamp(boostDb, 0f, 20f);
        var lines = new List<string>();

        AddRenderDeviceScope(lines);
        lines.Add($"Preamp: {preamp:+0.#;-0.#;0} dB");

        for (int i = 0; i < bands.Length && i < BandFrequencies.Length; i++)
        {
            float gain = Math.Clamp(bands[i], -12f, 12f);
            lines.Add($"Filter {i + 1}: ON PK Fc {BandFrequencies[i]} Hz Gain {gain:F1} dB Q 1.41");
        }

        ResetDeviceScope(lines);
        return lines.ToArray();
    }

    /// <summary>
    /// Scopes everything that follows to playback devices only.
    ///
    /// Without it EqualizerAPO applies config.txt to *every* device it is installed on, capture
    /// devices included — so a user who ticks a microphone in the Configurator gets the playback
    /// EQ applied to their own voice (a preset with a +7 dB 4 kHz footstep boost puts that boost
    /// on everything they say).
    ///
    /// Syntax per EqualizerAPO's configuration reference: patterns are separated by ';', and
    /// within a pattern every space-separated word must appear in that device's
    /// "DeviceName ConnectionName GUID" string. Every *active* render device is listed rather
    /// than just the current default, so switching headphones does not silently kill the EQ.
    /// </summary>
    private static void AddRenderDeviceScope(List<string> lines)
    {
        string? scope = RenderDeviceScopeLine();
        if (scope != null) lines.Add(scope);
    }

    /// <summary>Puts the scope back to everything, so anything appended or included after this
    /// config is not silently constrained to our device list.</summary>
    private static void ResetDeviceScope(List<string> lines)
    {
        if (lines.Any(l => l.StartsWith("Device:", StringComparison.Ordinal)))
            lines.Add("Device: all");
    }

    private static string? RenderDeviceScopeLine()
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var patterns = new List<string>();

            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (device)
                {
                    string pattern = DevicePattern(device);
                    if (pattern.Length > 0 && !patterns.Contains(pattern)) patterns.Add(pattern);
                }
            }

            return patterns.Count == 0 ? null : "Device: " + string.Join("; ", patterns);
        }
        catch (Exception ex)
        {
            // Deliberately fall back to an unscoped config: that is the old behaviour, which is
            // wrong for microphones but still works for playback. Writing a Device: line built
            // from a failed enumeration would be worse — a pattern matching nothing disables the
            // EQ on every device with no visible error.
            Logger.Log($"Could not enumerate render devices for Device: scoping: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Builds a match pattern from a device's name and connection name. FriendlyName is
    /// "ConnectionName (DeviceName)", and the brackets must not survive into the pattern —
    /// EqualizerAPO matches against "DeviceName ConnectionName GUID", which contains no
    /// brackets, so a word like "(Arctis" would never match.
    /// </summary>
    private static string DevicePattern(MMDevice device)
    {
        string deviceName = (device.DeviceFriendlyName ?? string.Empty).Trim();
        string friendly   = (device.FriendlyName ?? string.Empty).Trim();

        string suffix     = $" ({deviceName})";
        string connection = deviceName.Length > 0 && friendly.EndsWith(suffix, StringComparison.Ordinal)
            ? friendly[..^suffix.Length]
            : string.Empty;   // unexpected format — device name alone still matches, just less narrowly

        string pattern = connection.Length > 0 ? $"{deviceName} {connection}" : deviceName;

        // ';' would read as a pattern separator, and stray brackets cannot match (see above).
        pattern = pattern.Replace(';', ' ').Replace("(", "").Replace(")", "");
        return string.Join(' ', pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries));
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
