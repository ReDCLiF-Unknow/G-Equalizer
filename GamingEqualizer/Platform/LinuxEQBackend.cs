using System.Diagnostics;
using System.Text;

namespace GamingEqualizer.Platform;

// Controls system-wide EQ on Linux via EasyEffects (or legacy PulseEffects).
// Writes a preset JSON to ~/.config/easyeffects/output/ and loads it via the CLI.
public class LinuxEQBackend : IEQBackend
{
    private static readonly float[] BandFreqs = { 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    private const string PresetName = "GEqualizer";
    private const string BypassPreset = "GEqualizer-Bypass";

    public bool IsAvailable => FindEasyEffects() is not null;

    public void Apply(float[] bands, float boostDb)
    {
        try
        {
            WritePreset(PresetName, bands, boostDb);
            Run($"--load-preset {PresetName}");
        }
        catch (Exception ex) { Logger.Log($"LinuxEQBackend.Apply: {ex.Message}"); }
    }

    public void ApplyPerEar(float[] left, float[] right, float boostDb)
    {
        // EasyEffects doesn't expose per-channel routing in preset mode; apply averaged bands
        var avg = left.Zip(right, (l, r) => (l + r) / 2f).ToArray();
        Apply(avg, boostDb);
    }

    public void Bypass()
    {
        try
        {
            WritePreset(BypassPreset, new float[10], 0f);
            Run($"--load-preset {BypassPreset}");
        }
        catch (Exception ex) { Logger.Log($"LinuxEQBackend.Bypass: {ex.Message}"); }
    }

    private static void WritePreset(string name, float[] bands, float boostDb)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".config", "easyeffects", "output");
        Directory.CreateDirectory(dir);

        double outputGain = -6.0 + boostDb;
        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  \"output\": {");
        sb.AppendLine("    \"blocklist\": [],");
        sb.AppendLine("    \"plugins_order\": [\"equalizer\"],");
        sb.AppendLine("    \"equalizer\": {");
        sb.AppendLine($"      \"input-gain\": 0.0,");
        sb.AppendLine($"      \"output-gain\": {outputGain:F2},");
        sb.AppendLine("      \"mode\": \"IIR\",");
        sb.AppendLine("      \"num-bands\": 10,");
        sb.AppendLine("      \"split-channels\": false,");
        for (int i = 0; i < 10; i++)
        {
            bool last = i == 9;
            sb.AppendLine($"      \"band{i}\": {{");
            sb.AppendLine($"        \"frequency\": {BandFreqs[i]}.0,");
            sb.AppendLine($"        \"gain\": {bands[i]:F4},");
            sb.AppendLine("        \"mode\": \"Bell\",");
            sb.AppendLine("        \"mute\": false,");
            sb.AppendLine("        \"q\": 1.41,");
            sb.AppendLine("        \"slope\": \"x1\",");
            sb.AppendLine("        \"solo\": false,");
            sb.AppendLine("        \"type\": \"Bell\"");
            sb.AppendLine(last ? "      }" : "      },");
        }
        sb.AppendLine("    }");
        sb.AppendLine("  }");
        sb.AppendLine("}");

        File.WriteAllText(Path.Combine(dir, $"{name}.json"), sb.ToString(), Encoding.UTF8);
    }

    private static void Run(string args)
    {
        var exe = FindEasyEffects();
        if (exe is null) return;
        var psi = new ProcessStartInfo(exe, args)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        using var p = Process.Start(psi);
        p?.WaitForExit(3000);
    }

    private static string? FindEasyEffects()
    {
        foreach (var candidate in new[] { "easyeffects", "pulseeffects" })
        {
            try
            {
                var psi = new ProcessStartInfo("which", candidate)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false
                };
                using var p = Process.Start(psi)!;
                var path = p.StandardOutput.ReadLine()?.Trim();
                p.WaitForExit();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            catch { }
        }
        return null;
    }
}
