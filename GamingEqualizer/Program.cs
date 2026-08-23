using Avalonia;

namespace GamingEqualizer;

class Program
{
    // Per-session (Local\), not Global\: two different users signed in at once should each get
    // their own G-EQ, since the hotkeys and the APO config are per-desktop.
    private const string MutexName = @"Local\GamingEqualizer.SingleInstance";

    private static Mutex? _instanceMutex;

    /// <summary>
    /// Broadcast by a second instance to ask the running one to surface. Zero on non-Windows,
    /// and until <see cref="Main"/> has registered it.
    /// </summary>
    internal static uint ShowWindowMessage { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        if (OperatingSystem.IsWindows() && !ClaimSingleInstance())
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

        // The mutex must outlive the app; without this the GC is free to collect it early and
        // release the instance claim while we are still running.
        GC.KeepAlive(_instanceMutex);
    }

    /// <summary>
    /// True if this process is the first instance. If one is already running, asks it to show
    /// its window and returns false so this copy can exit.
    ///
    /// A second copy is not harmless: global hotkeys are exclusive, so whichever instance
    /// started first keeps them and the newer one silently gets none, while both write
    /// EqualizerAPO's config.txt. That is easy to hit now that G-EQ starts with Windows —
    /// it is already in the tray when the user clicks the desktop shortcut.
    /// </summary>
    private static bool ClaimSingleInstance()
    {
        ShowWindowMessage = RegisterWindowMessage("GamingEqualizer.ShowWindow");

        _instanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (isFirstInstance) return true;

        _instanceMutex.Dispose();
        _instanceMutex = null;

        // Best effort: if the running instance is mid-startup and has not subclassed its window
        // yet, nothing receives this and the user simply sees no new window. Exiting is still
        // the right call — a second instance is worse than an unresponsive click.
        if (ShowWindowMessage != 0)
            PostMessage(HwndBroadcast, ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);

        return false;
    }

    private static readonly IntPtr HwndBroadcast = new(0xffff);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string lpString);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
