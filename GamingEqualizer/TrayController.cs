using Avalonia.Controls;
using Avalonia.Platform;

namespace GamingEqualizer;

public class TrayController : IDisposable
{
    private readonly TrayIcon    _trayIcon;
    private readonly MainWindow  _mainWindow;
    private bool                 _disposed;

    public TrayController(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;

        _trayIcon = new TrayIcon
        {
            ToolTipText = "G-EQ",
            IsVisible   = true
        };

        LoadIcon(true);

        var openItem = new NativeMenuItem { Header = "Open" };
        openItem.Click += (_, _) => Dispatcher.UIThread.InvokeAsync(ShowWindow);

        var toggleItem = new NativeMenuItem { Header = "Toggle EQ" };
        toggleItem.Click += (_, _) => Dispatcher.UIThread.InvokeAsync(_mainWindow.ToggleEqFromTray);

        var quitItem = new NativeMenuItem { Header = "Quit" };
        quitItem.Click += (_, _) => Dispatcher.UIThread.InvokeAsync(_mainWindow.BypassAndQuit);

        var menu = new NativeMenu();
        menu.Add(openItem);
        menu.Add(toggleItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quitItem);

        _trayIcon.Menu    = menu;
        _trayIcon.Clicked += (_, _) => Dispatcher.UIThread.InvokeAsync(ShowWindow);
    }

    private void LoadIcon(bool eqOn)
    {
        try
        {
            var name   = eqOn ? "tray-icon-on.ico" : "tray-icon-off.ico";
            var uri    = new Uri($"avares://GamingEqualizer/Assets/{name}");
            using var stream = AssetLoader.Open(uri);
            _trayIcon.Icon = new WindowIcon(stream);
        }
        catch { }
    }

    public void SetEqState(bool eqOn) => LoadIcon(eqOn);

    public void UpdateTooltip(string preset, bool eqOn, bool boostEnabled, float boostDb)
    {
        string status = eqOn ? "ON" : "OFF";
        string boost  = (boostEnabled && boostDb > 0) ? $" · Boost +{boostDb:F0}dB" : "";
        string text   = $"G-EQ [{status}] — {preset}{boost}";
        _trayIcon.ToolTipText = text.Length > 63 ? text[..63] : text;
    }

    private void ShowWindow()
    {
        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed           = true;
        _trayIcon.IsVisible = false;
        _trayIcon.Dispose();
    }
}
