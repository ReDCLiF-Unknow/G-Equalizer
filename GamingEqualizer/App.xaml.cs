using GamingEqualizer.Models;

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

        var settings = AppSettings.Load();
        if (!settings.HasCompletedOnboarding)
        {
            var wizard = new OnboardingWizard { Owner = mainWindow };
            bool accepted = wizard.ShowDialog() == true;

            settings.HasCompletedOnboarding = true;
            settings.Save();

            if (accepted && wizard.ShouldRunCalibration)
                mainWindow.OpenCalibrationWizard();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
