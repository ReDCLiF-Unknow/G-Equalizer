#pragma warning disable CA1416 // Windows-only; device enumeration is guarded by OperatingSystem.IsWindows()

using System.Threading.Tasks;
using NAudio.CoreAudioApi;

namespace GamingEqualizer;

public class EQConfigWriter
{
    /// <summary>
    /// Formats a config line with '.' as the decimal separator regardless of the machine's
    /// locale. EqualizerAPO's configuration reference is explicit that floats are parsed
    /// "using point (.) as the decimal separator", locale-independently — so on a machine set
    /// to a comma locale, current-culture formatting wrote "Gain -3.0 dB" as "Gain -3,0 dB"
    /// and EqualizerAPO could not read it. The Q value on the same line was a hardcoded
    /// "1.41", which is what made the inconsistency visible.
    ///
    /// Every numeric line written to config.txt must go through this.
    /// </summary>
    private static string Inv(FormattableString line) => FormattableString.Invariant(line);

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
        lines.Add(Inv($"Preamp: {preamp:+0.#;-0.#;0} dB"));

        lines.Add("Channel: L");
        for (int i = 0; i < left.Length && i < BandFrequencies.Length; i++)
        {
            float gain = Math.Clamp(left[i], -12f, 12f);
            lines.Add(Inv($"Filter {i + 1}: ON PK Fc {BandFrequencies[i]} Hz Gain {gain:F1} dB Q 1.41"));
        }

        lines.Add("Channel: R");
        for (int i = 0; i < right.Length && i < BandFrequencies.Length; i++)
        {
            float gain = Math.Clamp(right[i], -12f, 12f);
            lines.Add(Inv($"Filter {i + 1}: ON PK Fc {BandFrequencies[i]} Hz Gain {gain:F1} dB Q 1.41"));
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
        lines.Add(Inv($"Preamp: {preamp:+0.#;-0.#;0} dB"));

        for (int i = 0; i < bands.Length && i < BandFrequencies.Length; i++)
        {
            float gain = Math.Clamp(bands[i], -12f, 12f);
            lines.Add(Inv($"Filter {i + 1}: ON PK Fc {BandFrequencies[i]} Hz Gain {gain:F1} dB Q 1.41"));
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

    // Enumerating endpoints costs ~400ms on real hardware, and this runs on the UI thread for
    // every EQ apply — every preset click and every slider move. Uncached, that froze the app
    // for half a second per change. The device list almost never changes, so it is computed
    // once, reused, and refreshed off-thread when stale or when something signals a change.
    private static readonly TimeSpan ScopeCacheTtl = TimeSpan.FromSeconds(30);
    private static string?  _cachedScopeLine;
    private static DateTime _scopeCachedAt = DateTime.MinValue;
    private static int      _scopeRefreshing;   // 0/1, guards against piling up refreshes

    /// <summary>Drops the cached device list so the next write re-reads it. Called when
    /// something has already observed a device change, so the 30s TTL is not the only path.</summary>
    public static void InvalidateDeviceScopeCache() => _scopeCachedAt = DateTime.MinValue;

    private static string? RenderDeviceScopeLine()
    {
        if (!OperatingSystem.IsWindows()) return null;

        // First use has to be correct before anything is written, so pay the cost here rather
        // than writing one unscoped config (which is exactly the microphone bug) while a
        // background read completes.
        if (_scopeCachedAt == DateTime.MinValue)
        {
            _cachedScopeLine = ComputeRenderDeviceScopeLine();
            _scopeCachedAt   = DateTime.UtcNow;
            return _cachedScopeLine;
        }

        if (DateTime.UtcNow - _scopeCachedAt > ScopeCacheTtl &&
            Interlocked.CompareExchange(ref _scopeRefreshing, 1, 0) == 0)
        {
            // Serve the cached value now; fold the new one in whenever it arrives.
            Task.Run(() =>
            {
                try
                {
                    _cachedScopeLine = ComputeRenderDeviceScopeLine();
                    _scopeCachedAt   = DateTime.UtcNow;
                }
                finally { Volatile.Write(ref _scopeRefreshing, 0); }
            });
        }

        return _cachedScopeLine;
    }

    private static string? ComputeRenderDeviceScopeLine()
    {
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
