using System.Diagnostics;
using System.Net.Http;
using System.Text;

namespace GamingEqualizer.Platform;

// Controls eqMac (https://eqmac.app) via its local HTTP API.
// eqMac must be installed and running; port is read from its app-support port file.
public class MacEQBackend : IEQBackend
{
    private static readonly float[] BandFreqs = { 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(2) };

    public bool IsAvailable => IsEqMacRunning() && TryGetPort(out _);

    public void Apply(float[] bands, float boostDb)
    {
        if (!TryGetPort(out int port)) return;
        try
        {
            Post(port, "/enabled", """{"enabled":true}""");

            double preamp = -6.0 + boostDb;
            var bandsJson = string.Join(",", bands.Select((g, i) =>
                $"{{\"id\":{i},\"frequency\":{BandFreqs[i]},\"gain\":{g:F4}}}"));
            Post(port, "/eq/bands", $"{{\"preamp\":{preamp:F2},\"bands\":[{bandsJson}]}}");
        }
        catch (Exception ex) { Logger.Log($"MacEQBackend.Apply: {ex.Message}"); }
    }

    public void ApplyPerEar(float[] left, float[] right, float boostDb)
    {
        // eqMac doesn't expose per-channel routing; apply averaged bands
        var avg = left.Zip(right, (l, r) => (l + r) / 2f).ToArray();
        Apply(avg, boostDb);
    }

    public void Bypass()
    {
        if (!TryGetPort(out int port)) return;
        try { Post(port, "/enabled", """{"enabled":false}"""); }
        catch (Exception ex) { Logger.Log($"MacEQBackend.Bypass: {ex.Message}"); }
    }

    private static bool IsEqMacRunning() =>
        Process.GetProcessesByName("eqMac").Length > 0;

    private static bool TryGetPort(out int port)
    {
        port = 0;
        var portFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Application Support", "eqMac.app", "port");
        if (File.Exists(portFile) && int.TryParse(File.ReadAllText(portFile).Trim(), out port))
            return true;
        // Fallback: assume default port if eqMac process is detected
        port = 59669;
        return IsEqMacRunning();
    }

    private static void Post(int port, string path, string json)
    {
        var content = new StringContent(json, Encoding.UTF8, "application/json");
        _http.PostAsync($"http://127.0.0.1:{port}{path}", content).GetAwaiter().GetResult();
    }
}
