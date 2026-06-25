using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Newtonsoft.Json;
using WpfRect    = System.Windows.Shapes.Rectangle;
using WpfEllipse = System.Windows.Shapes.Ellipse;
using WpfColor   = System.Windows.Media.Color;
using GamingEqualizer.Models;

namespace GamingEqualizer;

public partial class MainWindow : Window
{
    private static readonly string[] BandLabels = { "32", "64", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };

    private static readonly string[] BandTooltips =
    {
        "32 Hz — Sub-bass: deep rumble, explosions, engine vibration",
        "64 Hz — Bass: kick drum punch, low-end weight",
        "125 Hz — Upper bass: body of voices and instruments",
        "250 Hz — Low-mids: warmth; too much causes muddiness",
        "500 Hz — Mids: presence of voices, melee impact sounds",
        "1 kHz — Upper-mids: clarity and attack of most game sounds",
        "2 kHz — Presence: sharpness of dialogue and UI sounds",
        "4 kHz — High-mids: footsteps, reload clicks, detail cues",
        "8 kHz — Highs: air and crispness; spatial cues in headphones",
        "16 kHz — Brilliance: extreme top-end sparkle and hiss"
    };

    // Per-band slider + visual elements
    private readonly Slider[]     _sliders      = new Slider[10];
    private readonly TextBlock[]  _gainLabels   = new TextBlock[10];
    private readonly WpfRect[]    _sliderFills  = new WpfRect[10];
    private readonly WpfEllipse[] _sliderThumbs = new WpfEllipse[10];

    private const double SliderH  = 180;
    private const double CenterY  = SliderH / 2.0;
    private const double MaxFillH = CenterY - 6;

    // Preset chips
    private readonly List<(string Name, ToggleButton Chip)> _chips = new();

    private readonly AppSettings    _settings;
    private readonly EQConfigWriter _eqWriter      = new();
    private readonly PresetManager  _presetManager = new();

    private HwndSource?    _hwndSource;
    private MiniWindow?    _miniWindow;
    private TrayController? _tray;

    public void SetTray(TrayController tray) => _tray = tray;

    private bool _suppressPresetChange = false;
    private bool _suppressSliderChange = false;

    // ── Visualizer ──────────────────────────────────────────────────────────
    private const int VizBars = 80;
    private readonly WpfRect[]          _vizBars    = new WpfRect[VizBars];
    private readonly SolidColorBrush[]  _vizBrushes = new SolidColorBrush[VizBars];
    private readonly double[]           _vizCurrent = new double[VizBars];
    private readonly double[]           _vizTarget  = new double[VizBars];
    private WpfRect?                    _vizCenter;
    private double                      _ripplePhase;
    private DispatcherTimer?            _vizTimer;

    // Live audio
    private bool                   _liveMode;
    private AudioSpectrumAnalyzer? _spectrum;

    // Status dot pulse
    private DispatcherTimer? _pulseTimer;
    private bool             _pulseHigh = true;

    // Preset transition animation
    private readonly float[] _transitionTarget  = new float[10];
    private DispatcherTimer?  _transitionTimer;

    // Auto-preset switching
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    private DispatcherTimer? _autoPresetTimer;
    private string?          _lastAutoExe;

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            var uri = new Uri("pack://application:,,,/Assets/app-icon.ico", UriKind.Absolute);
            Icon = BitmapFrame.Create(uri);
        }
        catch { }
        _settings = AppSettings.Load();
        _presetManager.Load();

        BuildSliders();
        BuildPresetChips();
        RestoreState();
        BuildVisualizer();
        StartPulse();
        RefreshAutoPresetTimer();

        if (!EQConfigWriter.IsEqualizerApoInstalled())
            ShowEqApoMissingBanner();
    }

    // ── Band color ──────────────────────────────────────────────────────────

    private static WpfColor BandColor(double t)
    {
        byte r = (byte)(124 + (244 - 124) * t);
        byte g = (byte)(58  + (114 - 58)  * t);
        byte b = (byte)(237 + (182 - 237) * t);
        return WpfColor.FromRgb(r, g, b);
    }

    private WpfColor VizBarColor(int barIndex, double intensity, double t)
    {
        return _settings.VizColorMode switch
        {
            1 => WpfColor.FromRgb(0x7c, 0x3a, 0xed),  // Solid accent
            2 => PeakGlowColor(intensity, t),           // Peak Glow
            _ => BandColor(t)                           // Gradient (default)
        };
    }

    private static WpfColor PeakGlowColor(double intensity, double t)
    {
        intensity = Math.Clamp(intensity, 0, 1);
        var mid   = BandColor(t);

        if (intensity < 0.5)
        {
            // Dim background → gradient color
            double a = intensity * 2;
            byte r = (byte)(0x16 + (mid.R - 0x16) * a);
            byte g = (byte)(0x05 + (mid.G - 0x05) * a);
            byte bv = (byte)(0x2e + (mid.B - 0x2e) * a);
            return WpfColor.FromRgb(r, g, bv);
        }
        else
        {
            // Gradient color → white
            double a = (intensity - 0.5) * 2;
            byte r = (byte)(mid.R + (255 - mid.R) * a);
            byte g = (byte)(mid.G + (255 - mid.G) * a);
            byte bv = (byte)(mid.B + (255 - mid.B) * a);
            return WpfColor.FromRgb(r, g, bv);
        }
    }

    private static readonly string[] VizColorModeLabels = { "◈ GRADIENT", "◈ SOLID", "◈ PEAK GLOW" };

    private void ApplyVizColorMode()
    {
        if (VizColorModeButton != null)
        {
            VizColorModeButton.Content    = VizColorModeLabels[_settings.VizColorMode];
            VizColorModeButton.Foreground = _settings.VizColorMode == 0
                ? (System.Windows.Media.Brush)FindResource("TextDimBrush")
                : (System.Windows.Media.Brush)FindResource("AccentBrush");
        }

        if (_vizBrushes[0] == null) return; // not yet built

        if (_settings.VizColorMode != 2) // gradient and solid are static — set once
        {
            for (int j = 0; j < VizBars; j++)
            {
                double t = j / (double)(VizBars - 1);
                _vizBrushes[j].Color = VizBarColor(j, 0.5, t);
            }
        }
    }

    private void VizColorModeButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.VizColorMode = (_settings.VizColorMode + 1) % 3;
        _settings.Save();
        ApplyVizColorMode();
    }

    // ── Sliders ─────────────────────────────────────────────────────────────

    private void BuildSliders()
    {
        var chipStyle = (Style)Application.Current.Resources["TransparentSliderStyle"];

        for (int i = 0; i < 10; i++)
        {
            int idx   = i;
            double t  = i / 9.0;
            var color = BandColor(t);
            var brush = new SolidColorBrush(color);

            // Gain label
            var gainLabel = new TextBlock
            {
                Text                = "0",
                Foreground          = brush,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize            = 11,
                FontWeight          = FontWeights.SemiBold,
                Margin              = new Thickness(0, 0, 0, 4)
            };
            _gainLabels[idx] = gainLabel;

            // Visual canvas: track + fill + thumb
            var canvas = new Canvas { Width = 32, Height = SliderH };

            // Track background
            var track = new WpfRect
            {
                Width   = 4,
                Height  = SliderH,
                Fill    = new SolidColorBrush(WpfColor.FromRgb(20, 20, 40)),
                RadiusX = 2, RadiusY = 2
            };
            Canvas.SetLeft(track, 14);
            Canvas.SetTop(track, 0);
            canvas.Children.Add(track);

            // Zero tick
            var tick = new WpfRect
            {
                Width  = 12,
                Height = 1,
                Fill   = new SolidColorBrush(WpfColor.FromRgb(42, 42, 74))
            };
            Canvas.SetLeft(tick, 10);
            Canvas.SetTop(tick, CenterY);
            canvas.Children.Add(tick);

            // Fill (from center)
            var fill = new WpfRect
            {
                Width   = 4,
                Height  = 0,
                Fill    = brush,
                RadiusX = 2, RadiusY = 2,
                Opacity = 0.9
            };
            Canvas.SetLeft(fill, 14);
            Canvas.SetTop(fill, CenterY);
            canvas.Children.Add(fill);
            _sliderFills[idx] = fill;

            // Thumb ellipse
            var thumb = new WpfEllipse
            {
                Width           = 13,
                Height          = 13,
                Fill            = new SolidColorBrush(WpfColor.FromRgb(11, 11, 22)),
                Stroke          = brush,
                StrokeThickness = 2,
                Effect          = new DropShadowEffect
                {
                    Color       = color,
                    BlurRadius  = 10,
                    ShadowDepth = 0,
                    Opacity     = 0.85
                }
            };
            Canvas.SetLeft(thumb, 9.5);
            Canvas.SetTop(thumb, CenterY - 6.5);
            canvas.Children.Add(thumb);
            _sliderThumbs[idx] = thumb;

            // Transparent slider overlaid for mouse interaction
            var slider = new Slider
            {
                Orientation        = Orientation.Vertical,
                Minimum            = -12,
                Maximum            = 12,
                TickFrequency      = 1,
                IsSnapToTickEnabled = true,
                Width              = 32,
                Height             = SliderH,
                Style              = chipStyle
            };
            slider.ValueChanged += (_, _) => OnSliderChanged(idx);
            slider.MouseDoubleClick += (_, _) => { slider.Value = 0; };
            _sliders[idx] = slider;

            // Overlay grid
            var overlay = new Grid { Width = 32, Height = SliderH };
            overlay.Children.Add(canvas);
            overlay.Children.Add(slider);

            // Freq label
            var freqLabel = new TextBlock
            {
                Text                = BandLabels[i] + "Hz",
                Foreground          = new SolidColorBrush(WpfColor.FromRgb(42, 42, 74)),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize            = 10,
                Margin              = new Thickness(0, 4, 0, 0)
            };

            var col = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                ToolTip             = BandTooltips[i]
            };
            col.Children.Add(gainLabel);
            col.Children.Add(overlay);
            col.Children.Add(freqLabel);

            SliderGrid.Children.Add(col);
        }
    }

    private void UpdateSliderVisual(int idx)
    {
        float gain   = _settings.BandGains[idx];
        double fillH = Math.Abs(gain) / 12.0 * MaxFillH;
        fillH = Math.Max(2, fillH);

        _sliderFills[idx].Height = fillH;
        Canvas.SetTop(_sliderFills[idx], gain >= 0 ? CenterY - fillH : CenterY);

        double thumbTop = CenterY - (gain / 12.0 * MaxFillH) - 6.5;
        Canvas.SetTop(_sliderThumbs[idx], thumbTop);
    }

    // ── Preset chips ─────────────────────────────────────────────────────────

    private void BuildPresetChips()
    {
        _presetManager.Presets.ToList().ForEach(p => AddChip(p.Name, onClick: () => OnChipClick(p.Name)));
        AddChip("Custom", onClick: null);
    }

    private void AddChip(string name, Action? onClick)
    {
        var chip = new ToggleButton
        {
            Content = name,
            Style   = (Style)Application.Current.Resources["ChipStyle"]
        };

        if (onClick != null)
        {
            chip.Click += (_, _) =>
            {
                if (_suppressPresetChange) return;
                onClick();
            };
        }
        else
        {
            // Custom chip: visually selectable but no preset load
            chip.Click += (_, _) => { chip.IsChecked = true; };
        }

        _chips.Add((name, chip));
        ChipPanel.Children.Add(chip);
    }

    private void OnChipClick(string presetName)
    {
        var preset = _presetManager.Get(presetName);
        if (preset == null) return;

        for (int i = 0; i < 10; i++)
            _transitionTarget[i] = preset.Bands[i];

        _settings.ActivePreset = presetName;
        _settings.Save();
        SetActiveChip(presetName);

        StartPresetTransition();
    }

    private void StartPresetTransition()
    {
        _transitionTimer?.Stop();
        _transitionTimer          = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _transitionTimer.Tick    += TransitionTick;
        _transitionTimer.Start();
    }

    private void TransitionTick(object? sender, EventArgs e)
    {
        bool done = true;
        for (int i = 0; i < 10; i++)
        {
            float diff = _transitionTarget[i] - _settings.BandGains[i];
            if (Math.Abs(diff) > 0.05f)
            {
                _settings.BandGains[i] += diff * 0.18f;
                done = false;
            }
            else
            {
                _settings.BandGains[i] = _transitionTarget[i];
            }

            _gainLabels[i].Text = FormatGain(_settings.BandGains[i]);
            UpdateSliderVisual(i);
        }

        SetVizTargets();

        if (_settings.EqEnabled)
            ApplyCurrentGains();

        if (done)
        {
            _transitionTimer?.Stop();
            _suppressSliderChange = true;
            for (int i = 0; i < 10; i++)
                _sliders[i].Value = _settings.BandGains[i];
            _suppressSliderChange = false;
            _settings.Save();
        }
    }

    private void SetActiveChip(string? name)
    {
        _suppressPresetChange = true;
        foreach (var (n, chip) in _chips)
            chip.IsChecked = n == name;
        _suppressPresetChange = false;
    }

    // ── State / events ───────────────────────────────────────────────────────

    private void RestoreState()
    {
        _suppressSliderChange = true;
        for (int i = 0; i < 10; i++)
        {
            _sliders[i].Value   = _settings.BandGains[i];
            _gainLabels[i].Text = FormatGain(_settings.BandGains[i]);
        }
        SetVizTargets();
        Array.Copy(_vizTarget, _vizCurrent, VizBars);
        _suppressSliderChange = false;

        SetEqState(_settings.EqEnabled, writeConfig: false);
        SetActiveChip(_settings.ActivePreset);
        RefreshBoostButton();

        // Update all slider visuals after layout is ready
        Dispatcher.InvokeAsync(() =>
        {
            for (int i = 0; i < 10; i++) UpdateSliderVisual(i);
        }, System.Windows.Threading.DispatcherPriority.Loaded);

        if (_settings.EqEnabled)
            ApplyCurrentGains();
    }

    private void OnSliderChanged(int idx)
    {
        if (_suppressSliderChange) return;
        _transitionTimer?.Stop();

        float val = (float)_sliders[idx].Value;
        _gainLabels[idx].Text    = FormatGain(val);
        _settings.BandGains[idx] = val;

        UpdateSliderVisual(idx);

        // Moving a slider switches to Custom
        SetActiveChip("Custom");
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

    private void BoostButton_Click(object sender, RoutedEventArgs e)
    {
        _settings.BoostEnabled = !_settings.BoostEnabled;
        _settings.Save();
        RefreshBoostButton();
        if (_settings.EqEnabled) ApplyCurrentGains();
    }

    private void RefreshBoostButton()
    {
        if (_settings.BoostEnabled)
        {
            BoostButton.Content = $"⚡ +{_settings.BoostDb:0}dB ON";
            BoostButton.Style   = (Style)Application.Current.Resources["PrimaryButtonStyle"];
        }
        else
        {
            BoostButton.Content = "⚡ BOOST";
            BoostButton.Style   = null;
        }
    }

    private void SetEqState(bool enabled, bool writeConfig)
    {
        _settings.EqEnabled = enabled;

        if (enabled)
        {
            ToggleButton.Content = "■ DISABLE";
            ToggleButton.Style   = (Style)Application.Current.Resources["DangerButtonStyle"];
            StatusLabel.Text     = "EQ ACTIVE";
            StatusLabel.Foreground = new SolidColorBrush(WpfColor.FromRgb(167, 139, 250));
            StatusDot.Fill       = new SolidColorBrush(WpfColor.FromRgb(124, 58, 237));
            StatusPill.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(0x55, 0x7c, 0x3a, 0xed));
            _pulseTimer?.Start();
            if (writeConfig) ApplyCurrentGains();
        }
        else
        {
            ToggleButton.Content = "▶ ENABLE";
            ToggleButton.Style   = (Style)Application.Current.Resources["PrimaryButtonStyle"];
            StatusLabel.Text     = "EQ OFF";
            StatusLabel.Foreground = new SolidColorBrush(WpfColor.FromRgb(68, 68, 90));
            StatusDot.Fill       = new SolidColorBrush(WpfColor.FromRgb(42, 42, 68));
            StatusDot.Opacity    = 1;
            StatusPill.BorderBrush = new SolidColorBrush(WpfColor.FromArgb(0x33, 0x44, 0x44, 0x5a));
            _pulseTimer?.Stop();
            if (writeConfig) SafeBypass();
        }

        RefreshTrayTooltip();
    }

    private void RefreshTrayTooltip()
    {
        string preset = string.IsNullOrEmpty(_settings.ActivePreset) ? "Custom" : _settings.ActivePreset;
        _tray?.UpdateTooltip(preset, _settings.EqEnabled, _settings.BoostEnabled, _settings.BoostDb);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var win = new SettingsWindow(_settings, _presetManager, onBoostChanged: () =>
        {
            RefreshBoostButton();
            if (_settings.EqEnabled) ApplyCurrentGains();
        }) { Owner = this };
        win.ShowDialog();
        if (win.NewCalibrationGains != null) ApplyCalibrationGains(win.NewCalibrationGains);
        if (win.ImportedPreset != null)      HandleImportedPreset(win.ImportedPreset);
        RefreshBoostButton();
        RefreshAutoPresetTimer();
        if (_settings.EqEnabled) ApplyCurrentGains();
    }

    private void LiveModeButton_Click(object sender, RoutedEventArgs e)
        => ToggleLiveMode((WpfButton)sender);

    private void CalibrationButton_Click(object sender, RoutedEventArgs e)
        => OpenCalibrationWizard();

    public void OpenCalibrationWizard()
    {
        var wizard = new CalibrationWizard(_settings) { Owner = this };
        if (wizard.ShowDialog() == true && wizard.ResultGains != null)
        {
            _settings.LastCalibration      = wizard.ResultGains;
            _settings.LastCalibrationLeft  = wizard.ResultGainsLeft;
            _settings.LastCalibrationRight = wizard.ResultGainsRight;
            _settings.Save();
            ApplyCalibrationGains(wizard.ResultGains);
        }
    }

    private void ApplyCalibrationGains(float[] gains)
    {
        _suppressSliderChange = true;
        for (int i = 0; i < 10 && i < gains.Length; i++)
        {
            _sliders[i].Value        = gains[i];
            _gainLabels[i].Text      = FormatGain(gains[i]);
            _settings.BandGains[i]   = gains[i];
            UpdateSliderVisual(i);
        }
        _suppressSliderChange = false;

        _settings.ActivePreset = "";
        SetActiveChip("Custom");
        _settings.Save();
        SetVizTargets();

        if (_settings.EqEnabled)
            ApplyCurrentGains();
    }

    private void ApplyCurrentGains()
    {
        try
        {
            if (_settings.LastCalibrationLeft != null && _settings.LastCalibrationRight != null)
            {
                // Blend preset/slider gains with per-ear calibration offsets
                float[] avg = _settings.LastCalibration ?? _settings.BandGains;
                float[] left  = BlendWithPreset(_settings.BandGains, avg, _settings.LastCalibrationLeft);
                float[] right = BlendWithPreset(_settings.BandGains, avg, _settings.LastCalibrationRight);
                _eqWriter.ApplyPerEar(left, right, _settings.BoostEnabled ? _settings.BoostDb : 0f);
            }
            else
            {
                _eqWriter.Apply(_settings.BandGains, _settings.BoostEnabled ? _settings.BoostDb : 0f);
            }
            HideErrorBanner();
        }
        catch (Exception ex) { ShowErrorBanner($"Failed to apply EQ: {ex.Message}"); }
        RefreshTrayTooltip();
    }

    // Adds the per-ear deviation (calSide - calAvg) on top of the current preset/slider gains.
    private static float[] BlendWithPreset(float[] preset, float[] calAvg, float[] calSide)
    {
        float[] result = new float[10];
        for (int i = 0; i < 10; i++)
            result[i] = Math.Clamp(preset[i] + (calSide[i] - calAvg[i]), -12f, 12f);
        return result;
    }

    private void SafeBypass()
    {
        try   { _eqWriter.Bypass(); HideErrorBanner(); }
        catch (Exception ex) { ShowErrorBanner($"Failed to bypass EQ: {ex.Message}"); }
    }

    private void ShowErrorBanner(string message)
    {
        ErrorText.Text          = message;
        ErrorBanner.Visibility  = Visibility.Visible;
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
        e.Cancel = true;
        Hide();
    }

    private static string FormatGain(float v)
        => v >= 0 ? $"+{v:F0}" : $"{v:F0}";

    // ── Status dot pulse ─────────────────────────────────────────────────────

    private void StartPulse()
    {
        _pulseTimer          = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _pulseTimer.Tick    += (_, _) =>
        {
            _pulseHigh           = !_pulseHigh;
            StatusDot.Opacity    = _pulseHigh ? 1.0 : 0.35;
        };

        if (_settings.EqEnabled)
            _pulseTimer.Start();
    }

    // ── Visualizer ───────────────────────────────────────────────────────────

    private void BuildVisualizer()
    {
        // Center line
        _vizCenter = new WpfRect
        {
            Height = 1,
            Fill   = new SolidColorBrush(WpfColor.FromArgb(60, 30, 30, 58))
        };
        VisualizerCanvas.Children.Add(_vizCenter);

        // 80 bars — store brush refs so color mode can update them per-frame
        for (int j = 0; j < VizBars; j++)
        {
            double t      = j / (double)(VizBars - 1);
            var    brush  = new SolidColorBrush(VizBarColor(j, 0.5, t));
            var    bar    = new WpfRect { Fill = brush, RadiusX = 1, RadiusY = 1 };
            _vizBrushes[j] = brush;
            VisualizerCanvas.Children.Add(bar);
            _vizBars[j] = bar;
        }
        ApplyVizColorMode();

        _vizTimer          = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _vizTimer.Tick    += VizTick;
        _vizTimer.Start();
    }

    private void VisualizerCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => PositionVizBars();

    private void VizTick(object? sender, EventArgs e)
    {
        double lerp = _liveMode ? 0.30 : 0.15;
        double snap = _liveMode ? 0.05 : 0.02;

        for (int i = 0; i < VizBars; i++)
        {
            double diff = _vizTarget[i] - _vizCurrent[i];
            if (Math.Abs(diff) > snap)
                _vizCurrent[i] += diff * lerp;
            else
                _vizCurrent[i] = _vizTarget[i];
        }

        if (!_liveMode) _ripplePhase += 0.06;
        PositionVizBars();
    }

    private void PositionVizBars()
    {
        double w = VisualizerCanvas.ActualWidth;
        double h = VisualizerCanvas.ActualHeight;
        if (w <= 0 || h <= 0) return;

        double midY  = h / 2.0;
        double maxH  = midY - 3;
        double step  = w / VizBars;
        double barW  = Math.Max(1, step - 1.2);

        for (int j = 0; j < VizBars; j++)
        {
            double gain = _vizCurrent[j];

            if (!_liveMode)
            {
                double pos = j / (double)(VizBars - 1) * 9.0;
                gain += Math.Sin(pos * 1.4 + _ripplePhase) * 0.35;
            }

            double barH = Math.Abs(gain) / 12.0 * maxH;
            barH = Math.Max(2, barH);

            double x = j * step + (step - barW) / 2.0;
            double y = gain >= 0 ? midY - barH : midY;

            _vizBars[j].Width   = barW;
            _vizBars[j].Height  = barH;
            _vizBars[j].Opacity = gain >= 0 ? 1.0 : 0.4;
            Canvas.SetLeft(_vizBars[j], x);
            Canvas.SetTop(_vizBars[j], y);

            if (_settings.VizColorMode == 2) // Peak Glow — update color per-frame
            {
                double t = j / (double)(VizBars - 1);
                double intensity = Math.Abs(_vizCurrent[j]) / 12.0;
                _vizBrushes[j].Color = VizBarColor(j, intensity, t);
            }
        }

        if (_vizCenter != null)
        {
            _vizCenter.Width = w;
            Canvas.SetTop(_vizCenter, midY);
        }
    }

    private void SetVizTargets()
    {
        if (_liveMode) return; // live mode writes _vizTarget directly from FFT

        for (int j = 0; j < VizBars; j++)
        {
            double pos  = j / (double)(VizBars - 1) * 9.0;
            int    b0   = (int)pos;
            int    b1   = Math.Min(9, b0 + 1);
            double frac = pos - b0;
            _vizTarget[j] = _settings.BandGains[b0] * (1 - frac) + _settings.BandGains[b1] * frac;
        }
    }

    // ── Mini mode ────────────────────────────────────────────────────────────

    private void MiniModeButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
        _miniWindow = new MiniWindow(
            _settings, _presetManager,
            onToggle:      MiniToggleEq,
            onExpand:      ExpandFromMini,
            onPresetClick: MiniPresetClick);
        _miniWindow.Closed += (_, _) => _miniWindow = null;
        _miniWindow.Show();
    }

    private void MiniToggleEq()
    {
        SetEqState(!_settings.EqEnabled, writeConfig: true);
        _settings.Save();
    }

    private void MiniPresetClick(string presetName) => OnChipClick(presetName);

    private void ExpandFromMini()
    {
        _miniWindow?.Close();
        Show();
        Activate();
    }

    // Sync mini window after any state change triggered from MainWindow hotkeys
    private void SyncMiniWindow() => _miniWindow?.RefreshUI();

    // ── Save preset ──────────────────────────────────────────────────────────

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        var existingNames = _chips
            .Where(c => c.Name != "Custom")
            .Select(c => c.Name);

        var dlg = new SavePresetDialog(existingNames) { Owner = this };
        if (dlg.ShowDialog() != true || dlg.PresetName == null) return;

        string name = dlg.PresetName;
        try
        {
            var preset  = new Models.Preset { Name = name, Bands = (float[])_settings.BandGains.Clone() };
            var dir     = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Presets");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"{name}.json"),
                JsonConvert.SerializeObject(preset, Formatting.Indented));

            _presetManager.Reload();
            InsertPresetChip(name);
            OnChipClick(name);
        }
        catch (Exception ex)
        {
            ShowErrorBanner($"Failed to save preset: {ex.Message}");
        }
    }

    private void InsertPresetChip(string name)
    {
        var chip = new ToggleButton
        {
            Content = name,
            Style   = (Style)Application.Current.Resources["ChipStyle"]
        };
        chip.Click += (_, _) =>
        {
            if (_suppressPresetChange) return;
            OnChipClick(name);
        };

        // Insert before the Custom chip (always last)
        int idx = _chips.Count - 1;
        _chips.Insert(idx, (name, chip));
        ChipPanel.Children.Insert(idx, chip);

        _miniWindow?.AddChip(name);
    }

    private void HandleImportedPreset(Models.Preset preset)
    {
        _presetManager.Reload();
        if (_chips.Any(c => c.Name == preset.Name)) return;
        InsertPresetChip(preset.Name);
    }

    // ── Global hotkeys ───────────────────────────────────────────────────────

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hwndSource = HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(this).Handle);
        _hwndSource?.AddHook(WndProc);
        if (_hwndSource != null) HotkeyManager.Register(_hwndSource);
        DwmHelper.ApplyDarkTitlebar(this);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwndSource != null) HotkeyManager.Unregister(_hwndSource);
        _autoPresetTimer?.Stop();
        _spectrum?.Dispose();
        base.OnClosed(e);
    }

    internal void ToggleLiveMode(WpfButton liveButton)
    {
        _liveMode = !_liveMode;

        if (_liveMode)
        {
            try
            {
                _spectrum = new AudioSpectrumAnalyzer();
                _spectrum.OnSpectrum = bars => Dispatcher.InvokeAsync(() =>
                {
                    for (int j = 0; j < VizBars; j++)
                        _vizTarget[j] = bars[j];
                });
                _spectrum.Start();
                liveButton.Content = "◉ LIVE";
                liveButton.Style   = (Style)Application.Current.Resources["PrimaryButtonStyle"];
            }
            catch
            {
                _liveMode = false;
                _spectrum = null;
                ShowErrorBanner("WASAPI audio capture failed. Is a playback device available?");
            }
        }
        else
        {
            _spectrum?.Dispose();
            _spectrum          = null;
            liveButton.Content = "○ LIVE";
            liveButton.Style   = (Style)Application.Current.Resources[typeof(WpfButton)];
            SetVizTargets();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotkeyManager.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyManager.HK_TOGGLE)
            {
                SetEqState(!_settings.EqEnabled, writeConfig: true);
                _settings.Save();
                SyncMiniWindow();
                handled = true;
            }
            else if (id == HotkeyManager.HK_CYCLE)
            {
                CyclePreset();
                SyncMiniWindow();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void CyclePreset()
    {
        var presetChips = _chips.Where(c => c.Name != "Custom").ToList();
        if (presetChips.Count == 0) return;

        int current = presetChips.FindIndex(c => c.Name == _settings.ActivePreset);
        int next    = (current + 1) % presetChips.Count;
        OnChipClick(presetChips[next].Name);
    }

    // ── Auto-preset switching ────────────────────────────────────────────────

    internal void RefreshAutoPresetTimer()
    {
        if (_settings.AutoPresetEnabled)
        {
            if (_autoPresetTimer == null)
            {
                _autoPresetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                _autoPresetTimer.Tick += AutoPresetTick;
            }
            _autoPresetTimer.Start();
        }
        else
        {
            _autoPresetTimer?.Stop();
            _lastAutoExe = null;
        }
    }

    private void AutoPresetTick(object? sender, EventArgs e)
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0) return;

            string exe;
            try { exe = Process.GetProcessById((int)pid).ProcessName + ".exe"; }
            catch { return; }

            if (exe.Equals(_lastAutoExe, StringComparison.OrdinalIgnoreCase)) return;
            _lastAutoExe = exe;

            if (_settings.ProcessPresetMap.TryGetValue(exe, out string? presetName) &&
                presetName != _settings.ActivePreset &&
                _presetManager.Get(presetName) != null)
            {
                OnChipClick(presetName);
                RefreshTrayTooltip();
            }
        }
        catch { }
    }
}
