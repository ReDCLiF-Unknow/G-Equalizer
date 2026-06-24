using Microsoft.Win32;
using System.IO;
using System.Windows;
using GamingEqualizer.Models;
using Newtonsoft.Json;

namespace GamingEqualizer;

public partial class SettingsWindow : Window
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GamingEqualizer";

    private readonly AppSettings _settings;
    private readonly PresetManager _presetManager;
    private readonly Action? _onBoostChanged;
    private bool _suppress = false;

    public float[]? NewCalibrationGains { get; private set; }
    public Preset?  ImportedPreset      { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DwmHelper.ApplyDarkTitlebar(this);
    }

    public SettingsWindow(AppSettings settings, PresetManager presetManager, Action? onBoostChanged = null)
    {
        InitializeComponent();
        _settings = settings;
        _presetManager = presetManager;
        _onBoostChanged = onBoostChanged;

        _suppress = true;
        LaunchWithWindowsCheck.IsChecked = IsStartupRegistered();
        PopulateDefaultPresetCombo();
        BoostEnabledCheck.IsChecked = _settings.BoostEnabled;
        BoostSlider.Value = _settings.BoostDb;
        BoostLabel.Text = $"+{_settings.BoostDb:0} dB";
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

    private void Boost_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _settings.BoostEnabled = BoostEnabledCheck.IsChecked == true;
        _settings.Save();
        _onBoostChanged?.Invoke();
    }

    private void BoostSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress) return;
        _settings.BoostDb = (float)BoostSlider.Value;
        BoostLabel.Text = $"+{_settings.BoostDb:0} dB";
        _settings.Save();
        _onBoostChanged?.Invoke();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static readonly string PresetsDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Presets");

    private void ExportPreset_Click(object sender, RoutedEventArgs e)
    {
        string presetName = string.IsNullOrEmpty(_settings.ActivePreset) ? "Custom" : _settings.ActivePreset;
        var dlg = new SaveFileDialog
        {
            Title      = "Export Preset",
            FileName   = $"{presetName}.json",
            Filter     = "JSON preset|*.json",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var preset = new Preset { Name = presetName, Bands = (float[])_settings.BandGains.Clone() };
            File.WriteAllText(dlg.FileName, JsonConvert.SerializeObject(preset, Formatting.Indented));
            MessageBox.Show($"Preset exported to:\n{dlg.FileName}", "Exported",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportPreset_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Import Preset", Filter = "JSON preset|*.json" };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var json   = File.ReadAllText(dlg.FileName);
            var preset = JsonConvert.DeserializeObject<Preset>(json);
            if (preset?.Bands == null || preset.Bands.Length != 10)
            {
                MessageBox.Show("Invalid preset file — must have 10 band values.", "Import failed",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(preset.Name))
                preset.Name = Path.GetFileNameWithoutExtension(dlg.FileName);

            // Sanitize name: strip path chars
            foreach (char c in Path.GetInvalidFileNameChars())
                preset.Name = preset.Name.Replace(c, '_');

            var destPath = Path.Combine(PresetsDir, $"{preset.Name}.json");
            if (File.Exists(destPath))
            {
                var confirm = MessageBox.Show(
                    $"A preset named '{preset.Name}' already exists. Overwrite?",
                    "Conflict", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;
            }

            Directory.CreateDirectory(PresetsDir);
            File.WriteAllText(destPath, json);
            ImportedPreset = preset;
            MessageBox.Show($"Preset '{preset.Name}' imported.", "Imported",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Import failed:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

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
