using System.Globalization;
using System.Text.RegularExpressions;

namespace GamingEqualizer;

public static class AutoEQImporter
{
    private static readonly int[] BandFrequencies = { 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };

    private record PeakingFilter(float Fc, float Gain, float Q);

    /// <summary>
    /// Parse an AutoEQ parametric EQ .txt file and return 10-band gains blended to our fixed frequencies.
    /// Returns null if the file cannot be parsed.
    /// </summary>
    public static float[]? Import(string filePath)
    {
        string[] lines;
        try { lines = File.ReadAllLines(filePath); }
        catch { return null; }

        var filters = new List<PeakingFilter>();

        // Filter line: "Filter N: ON PK Fc 105 Hz Gain 6.6 dB Q 0.69"
        // Also handles "ON LSC" / "ON HSC" (shelf) — we skip those (rare, mostly handled by Preamp).
        var filterRx = new Regex(
            @"Filter\s+\d+\s*:\s*ON\s+PK\s+Fc\s+([\d.]+)\s*Hz\s+Gain\s+([+-]?[\d.]+)\s*dB\s+Q\s+([\d.]+)",
            RegexOptions.IgnoreCase);

        foreach (var line in lines)
        {
            var m = filterRx.Match(line);
            if (!m.Success) continue;

            if (!float.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float fc)) continue;
            if (!float.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float gain)) continue;
            if (!float.TryParse(m.Groups[3].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out float q)) continue;
            if (fc <= 0 || q <= 0) continue;

            filters.Add(new PeakingFilter(fc, gain, q));
        }

        if (filters.Count == 0) return null;

        var bands = new float[10];
        for (int i = 0; i < BandFrequencies.Length; i++)
        {
            float f = BandFrequencies[i];
            float total = 0f;
            foreach (var filt in filters)
            {
                float ratio = f / filt.Fc - filt.Fc / f;
                float denom = 1f + (filt.Q * ratio) * (filt.Q * ratio);
                total += filt.Gain / denom;
            }
            bands[i] = Math.Clamp(total, -12f, 12f);
        }

        return bands;
    }
}
