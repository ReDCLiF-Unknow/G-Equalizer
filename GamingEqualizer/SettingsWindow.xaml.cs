using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using GamingEqualizer.Models;
using Newtonsoft.Json;

namespace GamingEqualizer;

internal sealed class ProcessMappingRow
{
    public string Exe    { get; set; } = "";
    public string Preset { get; set; } = "";
}

public partial class SettingsWindow : Window
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GamingEqualizer";

    private readonly AppSettings _settings;
    private readonly PresetManager _presetManager;
    private readonly Action? _onBoostChanged;
    private bool _suppress = false;
    private readonly ObservableCollection<ProcessMappingRow> _mappingRows = new();

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
        AutoPresetCheck.IsChecked = _settings.AutoPresetEnabled;
        PopulateMappingUI();
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

    private void CopyShareCode_Click(object sender, RoutedEventArgs e)
    {
        string code = PresetShareCode.Encode(_settings.BandGains);
        System.Windows.Clipboard.SetText(code);
        MessageBox.Show($"Share code copied to clipboard:\n\n{code}", "Share Code Copied",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void PasteShareCode_Click(object sender, RoutedEventArgs e)
    {
        string text = System.Windows.Clipboard.GetText().Trim();
        if (string.IsNullOrEmpty(text))
        {
            MessageBox.Show("Clipboard is empty.", "Paste Share Code",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        float[]? bands = PresetShareCode.Decode(text);
        if (bands == null)
        {
            MessageBox.Show("Clipboard does not contain a valid share code.", "Paste Share Code",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var existingNames = _presetManager.Presets.Select(p => p.Name);
        var dlg = new SavePresetDialog(existingNames) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.PresetName == null) return;

        try
        {
            Directory.CreateDirectory(PresetsDir);
            var preset = new Models.Preset { Name = dlg.PresetName, Bands = bands };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(preset, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(Path.Combine(PresetsDir, $"{dlg.PresetName}.json"), json);
            ImportedPreset = preset;
            MessageBox.Show($"Preset '{dlg.PresetName}' added. Close Settings to apply.", "Preset Added",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save preset:\n{ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ImportAutoEQ_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title  = "Import AutoEQ Parametric EQ File",
            Filter = "AutoEQ parametric EQ|*.txt|All files|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        float[]? bands = AutoEQImporter.Import(dlg.FileName);
        if (bands == null)
        {
            MessageBox.Show(
                "Could not parse the file. Make sure it is an AutoEQ parametric EQ .txt file " +
                "containing lines like:\n  Filter 1: ON PK Fc 105 Hz Gain 6.6 dB Q 0.69",
                "Import AutoEQ", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string suggestedName = Path.GetFileNameWithoutExtension(dlg.FileName);

        // Exclude the suggested name from the "already exists" check so re-importing
        // the same headphone profile silently overwrites rather than forcing a rename.
        var existingNames = _presetManager.Presets
            .Select(p => p.Name)
            .Where(n => !n.Equals(suggestedName, StringComparison.OrdinalIgnoreCase));

        var saveDlg = new SavePresetDialog(existingNames, suggestedName) { Owner = this };
        if (saveDlg.ShowDialog() != true || saveDlg.PresetName == null) return;

        try
        {
            Directory.CreateDirectory(PresetsDir);
            var preset = new Models.Preset { Name = saveDlg.PresetName, Bands = bands };
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(preset, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(Path.Combine(PresetsDir, $"{saveDlg.PresetName}.json"), json);
            ImportedPreset = preset;
            MessageBox.Show($"AutoEQ profile '{saveDlg.PresetName}' imported. Close Settings to apply.",
                "AutoEQ Imported", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save preset:\n{ex.Message}", "Error",
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

    // ── Auto-preset switching ─────────────────────────────────────────────────

    private void PopulateMappingUI()
    {
        _mappingRows.Clear();
        foreach (var kv in _settings.ProcessPresetMap)
            _mappingRows.Add(new ProcessMappingRow { Exe = kv.Key, Preset = kv.Value });
        MappingList.ItemsSource = _mappingRows;

        NewPresetCombo.Items.Clear();
        foreach (var p in _presetManager.Presets)
            NewPresetCombo.Items.Add(p.Name);
        if (NewPresetCombo.Items.Count > 0)
            NewPresetCombo.SelectedIndex = 0;
    }

    private void AutoPreset_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress) return;
        _settings.AutoPresetEnabled = AutoPresetCheck.IsChecked == true;
        _settings.Save();
    }

    private const string ExePlaceholder = "process.exe";

    private void NewExeBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (NewExeBox.Text == ExePlaceholder)
        {
            NewExeBox.Text = "";
            NewExeBox.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"];
        }
    }

    private void NewExeBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewExeBox.Text))
        {
            NewExeBox.Text = ExePlaceholder;
            NewExeBox.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextDimBrush"];
        }
    }

    private void AddMapping_Click(object sender, RoutedEventArgs e)
    {
        string exe    = NewExeBox.Text.Trim();
        string preset = NewPresetCombo.SelectedItem as string ?? "";
        if (string.IsNullOrEmpty(exe) || exe == ExePlaceholder || string.IsNullOrEmpty(preset)) return;
        if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe += ".exe";

        _settings.ProcessPresetMap[exe] = preset;
        _settings.Save();

        var existing = _mappingRows.FirstOrDefault(r => r.Exe.Equals(exe, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
            existing.Preset = preset;
        else
            _mappingRows.Add(new ProcessMappingRow { Exe = exe, Preset = preset });

        NewExeBox.Text = ExePlaceholder;
        NewExeBox.Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextDimBrush"];
    }

    private void RemoveMapping_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button btn || btn.Tag is not string exe) return;
        _settings.ProcessPresetMap.Remove(exe);
        _settings.Save();
        var row = _mappingRows.FirstOrDefault(r => r.Exe == exe);
        if (row != null) _mappingRows.Remove(row);
    }
}
