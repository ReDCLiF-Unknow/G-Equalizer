using System.Windows;
using System.Windows.Forms;

namespace GamingEqualizer;

public class TrayController : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainWindow _mainWindow;
    private bool _disposed;

    public TrayController(MainWindow mainWindow)
    {
        _mainWindow = mainWindow;

        _notifyIcon = new NotifyIcon
        {
            Text = "G Equalizer",
            Visible = true
        };

        LoadIcon(true);
        BuildContextMenu();

        _notifyIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private void LoadIcon(bool eqOn)
    {
        var resourceName = eqOn ? "tray-icon-on.ico" : "tray-icon-off.ico";
        var uri = new Uri($"pack://application:,,,/Assets/{resourceName}", UriKind.Absolute);
        try
        {
            var stream = System.Windows.Application.GetResourceStream(uri)?.Stream;
            if (stream != null)
                _notifyIcon.Icon = new System.Drawing.Icon(stream);
            else
                _notifyIcon.Icon = SystemIcons.Application;
        }
        catch
        {
            _notifyIcon.Icon = SystemIcons.Application;
        }
    }

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        var toggleItem = new ToolStripMenuItem("Toggle EQ");
        toggleItem.Click += (_, _) =>
        {
            // Invoke on UI thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                _mainWindow.ToggleButton.RaiseEvent(
                    new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            });
        };

        var openItem = new ToolStripMenuItem("Open");
        openItem.Click += (_, _) => Application.Current.Dispatcher.Invoke(ShowWindow);

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => Application.Current.Dispatcher.Invoke(() =>
        {
            _mainWindow.BypassAndQuit();
        });

        menu.Items.Add(openItem);
        menu.Items.Add(toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);

        _notifyIcon.ContextMenuStrip = menu;
    }

    public void SetEqState(bool eqOn) => LoadIcon(eqOn);

    public void UpdateTooltip(string preset, bool eqOn, bool boostEnabled, float boostDb)
    {
        string status = eqOn ? "ON" : "OFF";
        string boost  = (boostEnabled && boostDb > 0) ? $" · Boost +{boostDb:F0}dB" : "";
        // NotifyIcon.Text is capped at 63 chars by Windows
        string text = $"G Equalizer [{status}] — {preset}{boost}";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
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
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
