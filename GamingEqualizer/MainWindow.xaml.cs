using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamingEqualizer.Models;

namespace GamingEqualizer;

public partial class MainWindow : Window
{
    private static readonly int[] BandFreqs = { 32, 64, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 };
    private static readonly string[] BandLabels = { "32", "64", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

    private readonly Slider[] _sliders = new Slider[10];
    private readonly TextBlock[] _gainLabels = new TextBlock[10];

    private readonly AppSettings _settings;
    private readonly EQConfigWriter _eqWriter = new();
    private readonly PresetManager _presetManager = new();

    private bool _suppressPresetChange = false;
    private bool _suppressSliderChange = false;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        _presetManager.Load();

        BuildSliders();
        BuildFreqLabels();
        PopulatePresetCombo();
        RestoreState();

        if (!EQConfigWriter.IsEqualizerApoInstalled())
            ShowEqApoMissingBanner();
    }

    private void BuildSliders()
    {
        for (int i = 0; i < 10; i++)
        {
            int idx = i;

            var container = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch
            };

            var slider = new Slider
            {
                Orientation = Orientation.Vertical,
                Minimum = -12,
                Maximum = 12,
                TickFrequency = 1,
                IsSnapToTickEnabled = true,
                Width = 40,
                Height = 160,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"]
            };

            slider.ValueChanged += (_, _) => OnSliderChanged(idx);
            _sliders[idx] = slider;

            var gainLabel = new TextBlock
            {
                Text = "0.0",
                Foreground = (SolidColorBrush)Application.Current.Resources["TextDimBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 11
            };
            _gainLabels[idx] = gainLabel;

            container.Children.Add(gainLabel);
            container.Children.Add(slider);
            SliderGrid.Children.Add(container);
        }
    }

    private void BuildFreqLabels()
    {
        for (int i = 0; i < 10; i++)
        {
            FreqLabels.Children.Add(new TextBlock
            {
                Text = BandLabels[i] + "Hz",
                Foreground = (SolidColorBrush)Application.Current.Resources["TextDimBrush"],
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize = 11
            });
        }
    }

    private void PopulatePresetCombo()
    {
        _suppressPresetChange = true;
        PresetCombo.Items.Clear();
        foreach (var preset in _presetManager.Presets)
            PresetCombo.Items.Add(preset.Name);
        _suppressPresetChange = false;
    }

    private void RestoreState()
    {
        _suppressSliderChange = true;
        for (int i = 0; i < 10; i++)
        {
            _sliders[i].Value = _settings.BandGains[i];
            _gainLabels[i].Text = _settings.BandGains[i].ToString("F1");
        }
        _suppressSliderChange = false;

        SetEqState(_settings.EqEnabled, writeConfig: false);

        _suppressPresetChange = true;
        PresetCombo.SelectedItem = _settings.ActivePreset;
        _suppressPresetChange = false;

        // Apply the saved gains on startup
        if (_settings.EqEnabled)
            ApplyCurrentGains();
    }

    private void OnSliderChanged(int idx)
    {
        if (_suppressSliderChange) return;

        float val = (float)_sliders[idx].Value;
        _gainLabels[idx].Text = val.ToString("F1");
        _settings.BandGains[idx] = val;

        // Moving a slider deselects named preset
        _suppressPresetChange = true;
        PresetCombo.SelectedIndex = -1;
        _suppressPresetChange = false;
        _settings.ActivePreset = "";

        _settings.Save();
        if (_settings.EqEnabled)
            ApplyCurrentGains();
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        SetEqState(!_settings.EqEnabled, writeConfig: true);
        _settings.Save();
    }

    private void SetEqState(bool enabled, bool writeConfig)
    {
        _settings.EqEnabled = enabled;

        if (enabled)
        {
            ToggleButton.Content = "DISABLE EQ";
            StatusLabel.Text = " · ON";
            StatusLabel.Foreground = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
            if (writeConfig) ApplyCurrentGains();
        }
        else
        {
            ToggleButton.Content = "ENABLE EQ";
            StatusLabel.Text = " · OFF";
            StatusLabel.Foreground = (SolidColorBrush)Application.Current.Resources["TextDimBrush"];
            if (writeConfig) SafeBypass();
        }
    }

    private void PresetCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetChange || PresetCombo.SelectedItem is not string presetName) return;

        var preset = _presetManager.Get(presetName);
        if (preset == null) return;

        _suppressSliderChange = true;
        for (int i = 0; i < 10; i++)
        {
            _sliders[i].Value = preset.Bands[i];
            _gainLabels[i].Text = preset.Bands[i].ToString("F1");
            _settings.BandGains[i] = preset.Bands[i];
        }
        _suppressSliderChange = false;

        _settings.ActivePreset = presetName;
        _settings.Save();

        if (_settings.EqEnabled)
            ApplyCurrentGains();
    }

    private void CalibrationButton_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new CalibrationWizard(_settings) { Owner = this };
        if (wizard.ShowDialog() == true && wizard.ResultGains != null)
        {
            _settings.LastCalibration = wizard.ResultGains;
            _suppressSliderChange = true;
            for (int i = 0; i < 10 && i < wizard.ResultGains.Length; i++)
            {
                _sliders[i].Value = wizard.ResultGains[i];
                _gainLabels[i].Text = wizard.ResultGains[i].ToString("F1");
                _settings.BandGains[i] = wizard.ResultGains[i];
            }
            _suppressSliderChange = false;
            _settings.ActivePreset = "";

            _suppressPresetChange = true;
            PresetCombo.SelectedIndex = -1;
            _suppressPresetChange = false;

            _settings.Save();
            if (_settings.EqEnabled)
                ApplyCurrentGains();
        }
    }

    private void ApplyCurrentGains()
    {
        try
        {
            _eqWriter.Apply(_settings.BandGains);
            HideErrorBanner();
        }
        catch (Exception ex)
        {
            ShowErrorBanner($"Failed to apply EQ: {ex.Message}");
        }
    }

    private void SafeBypass()
    {
        try
        {
            _eqWriter.Bypass();
            HideErrorBanner();
        }
        catch (Exception ex)
        {
            ShowErrorBanner($"Failed to bypass EQ: {ex.Message}");
        }
    }

    private void ShowErrorBanner(string message)
    {
        ErrorText.Text = message;
        ErrorBanner.Visibility = Visibility.Visible;
    }

    private void HideErrorBanner() => ErrorBanner.Visibility = Visibility.Collapsed;

    private void ShowEqApoMissingBanner()
    {
        ShowErrorBanner("EqualizerAPO is not installed at C:\\Program Files\\EqualizerAPO\\. " +
                        "EQ controls are disabled. Install EqualizerAPO and restart the app.");
        foreach (var s in _sliders) s.IsEnabled = false;
        ToggleButton.IsEnabled = false;
    }

    protected void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        // Hide to tray instead of closing
        e.Cancel = true;
        Hide();
    }
}
