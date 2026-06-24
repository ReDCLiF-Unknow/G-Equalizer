using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GamingEqualizer;

public static class DwmHelper
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_CAPTION_COLOR = 35;

    // #1a0533 as COLORREF (0x00BBGGRR)
    private const int AppCaptionColor = 0x00330519;

    public static void ApplyDarkTitlebar(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        int color = AppCaptionColor;
        DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref color, sizeof(int));
    }
}
