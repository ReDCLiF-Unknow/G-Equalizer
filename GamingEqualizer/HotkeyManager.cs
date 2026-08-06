using System.Runtime.InteropServices;

namespace GamingEqualizer;

public static class HotkeyManager
{
    public const int WM_HOTKEY = 0x0312;
    public const int HK_TOGGLE = 1;
    public const int HK_CYCLE  = 2;

    /// <summary>
    /// Preset-selection hotkeys (modifier + 1..9) use ids HK_PRESET_BASE + 0..PresetCount-1,
    /// kept clear of the ids above.
    /// </summary>
    public const int HK_PRESET_BASE = 10;
    public const int PresetCount    = 9;

    private const uint VK_1 = 0x31;   // VK_1..VK_9 are contiguous

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>
    /// Registers every configured hotkey. Returns the human-readable names of any that
    /// could not be claimed — RegisterHotKey fails when another application already owns
    /// the combination, and staying silent about that makes the app look broken.
    /// </summary>
    public static List<string> Register(IntPtr hwnd, AppSettings settings)
    {
        var failed = new List<string>();

        if (Hotkey.TryParse(settings.HotkeyToggle, out var toggle))
        {
            if (!RegisterHotKey(hwnd, HK_TOGGLE, toggle.Modifiers, toggle.VirtualKey))
                failed.Add($"Toggle EQ ({toggle})");
        }
        else failed.Add($"Toggle EQ (\"{settings.HotkeyToggle}\" is not a valid combination)");

        if (Hotkey.TryParse(settings.HotkeyCycle, out var cycle))
        {
            if (!RegisterHotKey(hwnd, HK_CYCLE, cycle.Modifiers, cycle.VirtualKey))
                failed.Add($"Cycle preset ({cycle})");
        }
        else failed.Add($"Cycle preset (\"{settings.HotkeyCycle}\" is not a valid combination)");

        uint presetMods = Hotkey.ParseModifiers(settings.HotkeyPresetModifiers);
        if (presetMods == 0)
        {
            failed.Add("Preset 1–9 (no modifier set)");
        }
        else
        {
            int lost = 0;
            for (int i = 0; i < PresetCount; i++)
                if (!RegisterHotKey(hwnd, HK_PRESET_BASE + i, presetMods, VK_1 + (uint)i))
                    lost++;

            if (lost > 0)
                failed.Add($"Preset selection ({settings.HotkeyPresetModifiers}+1…9 — {lost} of {PresetCount} unavailable)");
        }

        return failed;
    }

    public static void Unregister(IntPtr hwnd)
    {
        UnregisterHotKey(hwnd, HK_TOGGLE);
        UnregisterHotKey(hwnd, HK_CYCLE);

        for (int i = 0; i < PresetCount; i++)
            UnregisterHotKey(hwnd, HK_PRESET_BASE + i);
    }
}
