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

            // The window has to be shown even for a --minimized start (the "Launch with
            // Windows" run entry). MainWindow.OnOpened is where the sliders, saved state,
            // global hotkeys, auto-preset timer and the EqualizerAPO health check are all
            // wired up, and it only fires on the first Show() — skipping it left the app
            // sitting in the tray with none of that running. Showing minimized and hiding
            // again runs the init without the window ever appearing on screen.
            if (startMinimized)
            {
                mainWindow.WindowState = WindowState.Minimized;
                mainWindow.Show();
                mainWindow.Hide();
                // Reset so a restore from the tray comes back as a normal window.
                mainWindow.WindowState = WindowState.Normal;
            }
            else
            {
                // Opened fires during Show(), so this has to be attached before it — it used
                // to be registered afterwards, which meant the first-run wizard never ran on
                // any install. Opened also re-fires on a hide-to-tray/restore cycle, so the
                // HasCompletedOnboarding check is what keeps the wizard one-shot. Deferring
                // through the dispatcher lets Show() finish before a modal opens over it.
                mainWindow.Opened += (_, _) => Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    var settings = mainWindow.Settings;
                    if (settings.HasCompletedOnboarding) return;

                    var wizard   = new OnboardingWizard();
                    bool accepted = await wizard.ShowDialog<bool>(mainWindow);
                    settings.HasCompletedOnboarding = true;
                    settings.Save();
                    if (accepted && wizard.ShouldRunCalibration)
                        mainWindow.OpenCalibrationWizard();
                });

                mainWindow.Show();
            }
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Tray is disposed via the Exit event registered in OnFrameworkInitializationCompleted
}
