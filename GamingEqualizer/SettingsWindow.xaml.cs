using Microsoft.Win32;
using System.Windows;
using GamingEqualizer.Models;

namespace GamingEqualizer;

public partial class SettingsWindow : Window
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GamingEqualizer";

    private readonly AppSettings _settings;
    private readonly PresetManager _presetManager;
    private bool _suppress = false;

    public float[]? NewCalibrationGains { get; private set; }

    public SettingsWindow(AppSettings settings, PresetManager presetManager)
    {
        InitializeComponent();
        _settings = settings;
        _presetManager = presetManager;

        _suppress = true;
        LaunchWithWindowsCheck.IsChecked = IsStartupRegistered();
        PopulateDefaultPresetCombo();
        _suppress = false;
    }

    private void PopulateDefaultPresetCombo()
    {
        DefaultPresetCombo.Items.Clear();
        foreach (var preset in _presetManager.Presets)
            DefaultPresetCombo.Items.Add(preset.Name);

        DefaultPresetCombo.SelectedItem = string.IsNullOrEmpty(_settings.DefaultPreset)
            ? "Flat"
            : _settings.DefaultPreset;
    }

    private void LaunchWithWindows_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;

        bool enable = LaunchWithWindowsCheck.IsChecked == true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;

            if (enable)
            {
                var exePath = Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
                key.SetValue(AppName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(AppName, throwOnMissingValue: false);
            }

            _settings.LaunchWithWindows = enable;
            _settings.Save();
        }
        catch (Exception ex)
        {
            Logger.Log($"Failed to update startup registry: {ex.Message}");
            MessageBox.Show($"Could not update startup registry:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            _suppress = true;
            LaunchWithWindowsCheck.IsChecked = !enable;
            _suppress = false;
        }
    }

    private void DefaultPresetCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppress || DefaultPresetCombo.SelectedItem is not string presetName) return;

        _settings.DefaultPreset = presetName;
        _settings.Save();
    }

    private void RerunCalibration_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new CalibrationWizard(_settings) { Owner = this };
        if (wizard.ShowDialog() == true && wizard.ResultGains != null)
        {
            NewCalibrationGains = wizard.ResultGains;
            _settings.LastCalibration = wizard.ResultGains;
            _settings.Save();
            DialogResult = true;
            Close();
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static bool IsStartupRegistered()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) != null;
        }
        catch
        {
            return false;
        }
    }
}
