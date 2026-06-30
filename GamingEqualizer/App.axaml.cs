using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace GamingEqualizer;

public partial class App : Application
{
    private TrayController? _tray;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.Exit += (_, _) => _tray?.Dispose();

            var mainWindow = new MainWindow();
            _tray = new TrayController(mainWindow);
            mainWindow.SetTray(_tray);

            bool startMinimized = desktop.Args?.Contains("--minimized") == true;
            if (!startMinimized)
            {
                mainWindow.Show();

                mainWindow.Opened += async (_, _) =>
                {
                    var settings = mainWindow.Settings;
                    if (!settings.HasCompletedOnboarding)
                    {
                        var wizard   = new OnboardingWizard();
                        bool accepted = await wizard.ShowDialog<bool>(mainWindow);
                        settings.HasCompletedOnboarding = true;
                        settings.Save();
                        if (accepted && wizard.ShouldRunCalibration)
                            mainWindow.OpenCalibrationWizard();
                    }
                };
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Tray is disposed via the Exit event registered in OnFrameworkInitializationCompleted
}
