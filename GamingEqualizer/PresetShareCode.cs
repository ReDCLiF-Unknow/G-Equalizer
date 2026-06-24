namespace GamingEqualizer;

// Encodes/decodes a 10-band float[] as a compact base64 string for copy-paste sharing.
// Format: 10 × little-endian float32 (40 bytes) → URL-safe base64 (56 chars, no padding).
public static class PresetShareCode
{
    public static string Encode(float[] bands)
    {
        var bytes = new byte[bands.Length * 4];
        for (int i = 0; i < bands.Length; i++)
            BitConverter.GetBytes(bands[i]).CopyTo(bytes, i * 4);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static float[]? Decode(string code)
    {
        try
        {
            string b64 = code.Trim().Replace('-', '+').Replace('_', '/');
            int pad = (4 - b64.Length % 4) % 4;
            b64 += new string('=', pad);
            var bytes = Convert.FromBase64String(b64);
            if (bytes.Length != 40) return null;
            var bands = new float[10];
            for (int i = 0; i < 10; i++)
                bands[i] = Math.Clamp(BitConverter.ToSingle(bytes, i * 4), -12f, 12f);
            return bands;
        }
        catch { return null; }
    }
}
