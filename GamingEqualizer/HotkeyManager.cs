using System.Runtime.InteropServices;

namespace GamingEqualizer;

public static class HotkeyManager
{
    public const int WM_HOTKEY   = 0x0312;
    public const int HK_TOGGLE   = 1;
    public const int HK_CYCLE    = 2;

    private const uint MOD_ALT  = 0x0001;
    private const uint MOD_CTRL = 0x0002;
    private const uint VK_E     = 0x45;
    private const uint VK_P     = 0x50;

    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static void Register(IntPtr hwnd)
    {
        RegisterHotKey(hwnd, HK_TOGGLE, MOD_CTRL | MOD_ALT, VK_E);
        RegisterHotKey(hwnd, HK_CYCLE,  MOD_CTRL | MOD_ALT, VK_P);
    }

    public static void Unregister(IntPtr hwnd)
    {
        UnregisterHotKey(hwnd, HK_TOGGLE);
        UnregisterHotKey(hwnd, HK_CYCLE);
    }
}
