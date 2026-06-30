using System.Runtime.InteropServices;

namespace GamingEqualizer;

public static class DwmHelper
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    private const int DWMWA_CAPTION_COLOR = 35;
    private const int AppCaptionColor     = 0x00330519; // #1a0533 as COLORREF

    public static void ApplyDarkTitlebar(IntPtr hwnd)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            int color = AppCaptionColor;
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(int));
        }
        catch { }
    }
}
