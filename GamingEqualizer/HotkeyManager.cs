using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace GamingEqualizer;

public static class HotkeyManager
{
    public const int HK_TOGGLE = 1;
    public const int HK_CYCLE  = 2;

    // MOD_CONTROL | MOD_ALT
    private const uint MOD_ALT     = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_CTRL_ALT = MOD_CONTROL | MOD_ALT;

    private const uint VK_E = 0x45;
    private const uint VK_P = 0x50;

    public const int WM_HOTKEY = 0x0312;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public static void Register(HwndSource source)
    {
        RegisterHotKey(source.Handle, HK_TOGGLE, MOD_CTRL_ALT, VK_E);
        RegisterHotKey(source.Handle, HK_CYCLE,  MOD_CTRL_ALT, VK_P);
    }

    public static void Unregister(HwndSource source)
    {
        UnregisterHotKey(source.Handle, HK_TOGGLE);
        UnregisterHotKey(source.Handle, HK_CYCLE);
    }
}
