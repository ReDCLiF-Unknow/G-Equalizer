namespace GamingEqualizer;

public partial class App : System.Windows.Application
{
    private TrayController? _tray;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var mainWindow = new MainWindow();
        _tray = new TrayController(mainWindow);

        bool startMinimized = e.Args.Contains("--minimized");
        if (!startMinimized)
            mainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
