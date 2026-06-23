using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WpfRect = System.Windows.Shapes.Rectangle;
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

    // Visualizer
    private readonly WpfRect[] _vizBars = new WpfRect[10];
    private readonly double[] _vizCurrent = new double[10];
    private readonly double[] _vizTarget = new double[10];
    private DispatcherTimer? _vizTimer;

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        _presetManager.Load();

        BuildSliders();
        BuildFreqLabels();
        PopulatePresetCombo();
        RestoreState();
        BuildVisualizer();

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
        SetVizTargets();
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
        SetVizTargets();

        if (_settings.EqEnabled)
            ApplyCurrentGains();
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings, _presetManager) { Owner = this };
        if (win.ShowDialog() == true && win.NewCalibrationGains != null)
        {
            ApplyCalibrationGains(win.NewCalibrationGains);
        }
    }

    private void CalibrationButton_Click(object sender, RoutedEventArgs e)
    {
        var wizard = new CalibrationWizard(_settings) { Owner = this };
        if (wizard.ShowDialog() == true && wizard.ResultGains != null)
        {
            _settings.LastCalibration = wizard.ResultGains;
            _settings.Save();
            ApplyCalibrationGains(wizard.ResultGains);
        }
    }

    private void ApplyCalibrationGains(float[] gains)
    {
        _suppressSliderChange = true;
        for (int i = 0; i < 10 && i < gains.Length; i++)
        {
            _sliders[i].Value = gains[i];
            _gainLabels[i].Text = gains[i].ToString("F1");
            _settings.BandGains[i] = gains[i];
        }
        _suppressSliderChange = false;
        _settings.ActivePreset = "";

        _suppressPresetChange = true;
        PresetCombo.SelectedIndex = -1;
        _suppressPresetChange = false;

        _settings.Save();
        SetVizTargets();
        if (_settings.EqEnabled)
            ApplyCurrentGains();
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

    // ── Visualizer ────────────────────────────────────────────────────────────

    private void BuildVisualizer()
    {
        var accent = (SolidColorBrush)Application.Current.Resources["AccentBrush"];
        var dimAccent = new SolidColorBrush(System.Windows.Media.Color.FromArgb(80, accent.Color.R, accent.Color.G, accent.Color.B));

        for (int i = 0; i < 10; i++)
        {
            _vizBars[i] = new WpfRect
            {
                Fill = dimAccent,
                RadiusX = 2,
                RadiusY = 2
            };
            VisualizerCanvas.Children.Add(_vizBars[i]);
        }

        // Center line
        var centerLine = new WpfRect
        {
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 255, 255, 255)),
            Height = 1
        };
        VisualizerCanvas.Children.Add(centerLine);
        Canvas.SetTop(centerLine, 0);
        Canvas.SetLeft(centerLine, 0);
        centerLine.Width = double.NaN; // will be set in SizeChanged

        // Store center line for SizeChanged
        _vizCenterLine = centerLine;

        // Seed current values from settings so first frame isn't a snap from 0
        for (int i = 0; i < 10; i++)
        {
            _vizCurrent[i] = _settings.BandGains[i];
            _vizTarget[i] = _settings.BandGains[i];
        }

        _vizTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _vizTimer.Tick += VizTick;
        _vizTimer.Start();
    }

    private WpfRect? _vizCenterLine;

    private void VisualizerCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => PositionAllVizBars();

    private void VizTick(object? sender, EventArgs e)
    {
        bool dirty = false;
        for (int i = 0; i < 10; i++)
        {
            double diff = _vizTarget[i] - _vizCurrent[i];
            if (Math.Abs(diff) > 0.01)
            {
                _vizCurrent[i] += diff * 0.18;
                dirty = true;
            }
            else
            {
                _vizCurrent[i] = _vizTarget[i];
            }
        }
        if (dirty) PositionAllVizBars();
    }

    private void PositionAllVizBars()
    {
        double w = VisualizerCanvas.ActualWidth;
        double h = VisualizerCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double midY = h / 2.0;
        double maxBarH = midY - 4;
        double barW = Math.Max(4, w / 10.0 - 4);

        for (int i = 0; i < 10; i++)
        {
            double gain = _vizCurrent[i];
            double barH = Math.Abs(gain) / 12.0 * maxBarH;
            barH = Math.Max(1, barH);

            double x = i * (w / 10.0) + (w / 10.0 - barW) / 2.0;
            double y = gain >= 0 ? midY - barH : midY;

            _vizBars[i].Width = barW;
            _vizBars[i].Height = barH;
            Canvas.SetLeft(_vizBars[i], x);
            Canvas.SetTop(_vizBars[i], y);
        }

        if (_vizCenterLine != null)
        {
            _vizCenterLine.Width = w;
            Canvas.SetTop(_vizCenterLine, midY);
        }
    }

    private void SetVizTargets()
    {
        for (int i = 0; i < 10; i++)
            _vizTarget[i] = _settings.BandGains[i];
    }
}
