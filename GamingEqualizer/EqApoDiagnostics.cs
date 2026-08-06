#pragma warning disable CA1416 // Windows-only; entry point is guarded by OperatingSystem.IsWindows()

using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace GamingEqualizer;

/// <summary>Why the EQ might be inaudible even though EqualizerAPO is installed.</summary>
public enum EqApoStatus
{
    /// <summary>Installed and hooked into the current playback device.</summary>
    Ok,
    /// <summary>No EqualizerAPO installation found.</summary>
    NotInstalled,
    /// <summary>Installed, but not attached to the current default playback device at all.</summary>
    NotAttached
}

/// <summary>
/// Checks whether EqualizerAPO will actually affect audio — not merely whether it is
/// installed. Windows 10/11 loads APOs from the modern SFX/MFX/EFX chain; a device
/// registered only in the legacy pre/post-mix slots is skipped silently, which presents
/// as "the EQ does nothing" while every write to config.txt appears to succeed.
/// </summary>
public static class EqApoDiagnostics
{
    // Property-set GUIDs as they appear in FxProperties value names ("{guid},pid").
    private const string ModernFxSet = "{d3993a3f-99c2-4402-b5ec-a92a0367664b}";
    private const string LegacyFxSet = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}";

    public static EqApoStatus GetStatus()
    {
        // A diagnostic must never take the app down or block EQ use — on any doubt,
        // report Ok and stay quiet rather than nagging about a healthy setup.
        try
        {
            if (!EQConfigWriter.IsEqualizerApoInstalled()) return EqApoStatus.NotInstalled;
            if (!OperatingSystem.IsWindows())              return EqApoStatus.Ok;

            string? endpoint = DefaultRenderEndpointGuid();
            if (endpoint is null) return EqApoStatus.Ok;

            // Either slot counts as attached. EqualizerAPO's default install mode uses the
            // legacy pre/post-mix slots and works on most drivers, so legacy-only is not
            // reportable on its own. Confirming it is *actually* live would mean inspecting
            // audiodg.exe's loaded modules, and that is a protected process — its module
            // list is not enumerable, so absence of the DLL there proves nothing.
            var (modern, legacy) = FindEqApoSlots(endpoint);
            return modern || legacy ? EqApoStatus.Ok : EqApoStatus.NotAttached;
        }
        catch { return EqApoStatus.Ok; }
    }

    /// <summary>Endpoint GUID of the default playback device, or null if it can't be read.</summary>
    private static string? DefaultRenderEndpointGuid()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device     = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);

            // ID looks like "{0.0.0.00000000}.{042e9d92-0811-4da5-9835-447603e156a0}"
            int brace = device.ID.LastIndexOf('{');
            return brace < 0 ? null : device.ID[brace..];
        }
        catch { return null; }
    }

    private static (bool modern, bool legacy) FindEqApoSlots(string endpointGuid)
    {
        bool modern = false, legacy = false;

        using var fx = Registry.LocalMachine.OpenSubKey(
            $@"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\{endpointGuid}\FxProperties");
        if (fx is null) return (false, false);

        foreach (string name in fx.GetValueNames())
        {
            bool isModern = name.StartsWith(ModernFxSet, StringComparison.OrdinalIgnoreCase);
            bool isLegacy = name.StartsWith(LegacyFxSet, StringComparison.OrdinalIgnoreCase);
            if (!isModern && !isLegacy) continue;

            foreach (string clsid in AsStrings(fx.GetValue(name)))
            {
                if (!IsEqualizerApoClsid(clsid)) continue;
                if (isModern) modern = true;
                else          legacy = true;
            }
        }

        return (modern, legacy);
    }

    // FX slots hold either a single CLSID or a list of them, depending on the property.
    private static IEnumerable<string> AsStrings(object? value) => value switch
    {
        string   s => new[] { s },
        string[] a => a,
        _          => Array.Empty<string>()
    };

    // Resolved through the COM registration rather than hard-coded, so this keeps working
    // if a future EqualizerAPO release ships different CLSIDs.
    private static bool IsEqualizerApoClsid(string clsid)
    {
        if (string.IsNullOrWhiteSpace(clsid) || clsid[0] != '{') return false;

        using var key = Registry.LocalMachine.OpenSubKey($@"SOFTWARE\Classes\CLSID\{clsid}\InprocServer32");
        return key?.GetValue(null) is string dll &&
               dll.Contains("EqualizerAPO", StringComparison.OrdinalIgnoreCase);
    }
}
