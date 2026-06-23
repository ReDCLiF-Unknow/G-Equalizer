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
        var iconName = eqOn ? "tray-icon-on.ico" : "tray-icon-off.ico";
        var iconPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", iconName);

        if (File.Exists(iconPath))
            _notifyIcon.Icon = new System.Drawing.Icon(iconPath);
        else
            _notifyIcon.Icon = SystemIcons.Application;
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
        quitItem.Click += (_, _) => Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());

        menu.Items.Add(openItem);
        menu.Items.Add(toggleItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(quitItem);

        _notifyIcon.ContextMenuStrip = menu;
    }

    public void SetEqState(bool eqOn) => LoadIcon(eqOn);

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
