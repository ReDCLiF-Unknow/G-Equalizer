#pragma warning disable CA1416 // Windows-specific APIs guarded by OperatingSystem.IsWindows()

using System.Runtime.Versioning;
using Avalonia.Input.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

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
    private readonly Slider[]    _sliders      = new Slider[10];
    private readonly TextBlock[] _gainLabels   = new TextBlock[10];
    private readonly Rectangle[] _sliderFills  = new Rectangle[10];
    private readonly Ellipse[]   _sliderThumbs = new Ellipse[10];

    private const double SliderH  = 180;
    private const double CenterY  = SliderH / 2.0;
    private const double MaxFillH = CenterY - 6;

    // Preset chips — store (Name, ToggleButton, Container) so deletion removes the right element
    private readonly List<(string Name, ToggleButton Chip, Control Container)> _chips = new();

    private readonly AppSettings   _settings;
    private readonly IEQBackend    _backend       = PlatformServices.CreateEQBackend();
    private readonly PresetManager _presetManager = new();

    private MiniWindow?     _miniWindow;
    private TrayController? _tray;

    public void SetTray(TrayController tray) => _tray = tray;
    public AppSettings Settings => _settings;

    private bool _suppressPresetChange = false;
    private bool _suppressSliderChange = false;

    // ── Visualizer ──────────────────────────────────────────────────────────
    private const int VizBars = 80;
    private readonly Rectangle[]         _vizBars    = new Rectangle[VizBars];
    private readonly SolidColorBrush[]   _vizBrushes = new SolidColorBrush[VizBars];
    private readonly double[]            _vizCurrent = new double[VizBars];
    private readonly double[]            _vizTarget  = new double[VizBars];
    private Rectangle?                   _vizCenter;
    private double                       _ripplePhase;
    private DispatcherTimer?             _vizTimer;
    private bool                         _positioningVizBars;

    // Live audio
    private bool                   _liveMode;
    private AudioSpectrumAnalyzer? _spectrum;

    // Status dot pulse
    private DispatcherTimer? _pulseTimer;
    private bool             _pulseHigh = true;

    // Preset transition animation
    private readonly float[] _transitionTarget = new float[10];
    private DispatcherTimer?  _transitionTimer;

    // Auto-preset switching (Windows-only P/Invokes)
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    private DispatcherTimer? _autoPresetTimer;
    private string?          _lastAutoExe;

    // Settings panel
    private bool _settingsPanelOpen = false;
    private bool _suppressSettings  = false;
    private readonly ObservableCollection<ProcessMappingRow> _mappingRows = new();

    // Win32 hotkey subclassing (Windows-only)
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, WndProcDelegate newProc);
    [DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    private WndProcDelegate? _wndProcDelegate;
    private IntPtr           _originalWndProc;
    private IntPtr           _hwnd;

    // ── Resource helpers ─────────────────────────────────────────────────────

    private ControlTheme PrimaryButtonTheme => (ControlTheme)this.FindResource("PrimaryButtonTheme")!;
    private ControlTheme DangerButtonTheme  => (ControlTheme)this.FindResource("DangerButtonTheme")!;
    private ControlTheme ChipTheme          => (ControlTheme)this.FindResource("ChipTheme")!;
    private ControlTheme IconButtonTheme    => (ControlTheme)this.FindResource("IconButtonTheme")!;

    private IBrush TextBrush    => (IBrush)this.FindResource("TextBrush")!;
    private IBrush TextDimBrush => (IBrush)this.FindResource("TextDimBrush")!;
    private IBrush AccentBrush  => (IBrush)this.FindResource("AccentBrush")!;

    // ── Constructor ──────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        _settings = AppSettings.Load();
        _presetManager.Load();

        Width  = Math.Max(MinWidth,  _settings.WindowWidth);
        Height = Math.Max(MinHeight, _settings.WindowHeight);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        BuildSliders();
        BuildPresetChips();
        RestoreState();
        BuildVisualizer();
        StartPulse();
        RefreshAutoPresetTimer();

        if (!_backend.IsAvailable)
            ShowEqApoMissingBanner();

        if (OperatingSystem.IsWindows())
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero)
            {
                HotkeyManager.Register(hwnd);

                // OnOpened can fire more than once for the same window (e.g. hide-to-tray
                // then restore). Only subclass the WndProc once per hwnd — re-subclassing
                // would make SetWindowLongPtr return our own WndProc thunk as the "previous"
                // proc, so CallWindowProc would call back into WndProc forever (stack overflow).
                if (_hwnd != hwnd)
                {
                    _hwnd = hwnd;
                    _wndProcDelegate = WndProc;
                    _originalWndProc = SetWindowLongPtr(_hwnd, -4, _wndProcDelegate);
                }
            }
            DwmHelper.ApplyDarkTitlebar(_hwnd);
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_hwnd != IntPtr.Zero) HotkeyManager.Unregister(_hwnd);
        _autoPresetTimer?.Stop();
        _spectrum?.Dispose();

        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth  = Width;
            _settings.WindowHeight = Height;
            _settings.Save();
        }

        base.OnClosed(e);
    }

    // ── Band color ──────────────────────────────────────────────────────────

    private static Color BandColor(double t)
    {
        byte r = (byte)(124 + (244 - 124) * t);
        byte g = (byte)(58  + (114 - 58)  * t);
        byte b = (byte)(237 + (182 - 237) * t);
        return Color.FromRgb(r, g, b);
    }

    private Color VizBarColor(int barIndex, double intensity, double t)
    {
        return _settings.VizColorMode switch
        {
            1 => Color.FromRgb(0x7c, 0x3a, 0xed),
            2 => PeakGlowColor(intensity, t),
            _ => BandColor(t)
        };
    }

    private static Color PeakGlowColor(double intensity, double t)
    {
        intensity = Math.Clamp(intensity, 0, 1);
        var mid = BandColor(t);

        if (intensity < 0.5)
        {
            double a = intensity * 2;
            byte r = (byte)(0x16 + (mid.R - 0x16) * a);
            byte g = (byte)(0x05 + (mid.G - 0x05) * a);
            byte bv = (byte)(0x2e + (mid.B - 0x2e) * a);
            return Color.FromRgb(r, g, bv);
        }
        else
        {
            double a = (intensity - 0.5) * 2;
            byte r = (byte)(mid.R + (255 - mid.R) * a);
            byte g = (byte)(mid.G + (255 - mid.G) * a);
            byte bv = (byte)(mid.B + (255 - mid.B) * a);
            return Color.FromRgb(r, g, bv);
        }
    }

    private static readonly string[] VizColorModeLabels = { "◈ GRADIENT", "◈ SOLID", "◈ PEAK GLOW" };

    private void ApplyVizColorMode()
    {
        if (VizColorModeButton != null)
        {
            VizColorModeButton.Content  = VizColorModeLabels[_settings.VizColorMode];
            VizColorModeButton.Foreground = _settings.VizColorMode == 0
                ? TextDimBrush : AccentBrush;
        }

        if (_vizBrushes[0] == null) return;

        if (_settings.VizColorMode != 2)
        {
            for (int j = 0; j < VizBars; j++)
            {
                double t = j / (double)(VizBars - 1);
                _vizBrushes[j].Color = VizBarColor(j, 0.5, t);
            }
        }
    }

    private void VizColorModeButton_Click(object? sender, RoutedEventArgs e)
    {
        _settings.VizColorMode = (_settings.VizColorMode + 1) % 3;
        _settings.Save();
        ApplyVizColorMode();
    }

    // ── Sliders ─────────────────────────────────────────────────────────────

    private void BuildSliders()
    {
        SliderGrid.Children.Clear();
        for (int i = 0; i < 10; i++)
        {
            int    idx   = i;
            double t     = i / 9.0;
            var    color = BandColor(t);
            var    brush = new SolidColorBrush(color);

            var gainLabel = new TextBlock
            {
                Text                = "0",
                Foreground          = brush,
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize            = 11,
                FontWeight          = FontWeight.SemiBold,
                Margin              = new Thickness(0, 0, 0, 4)
            };
            _gainLabels[idx] = gainLabel;

            var canvas = new Canvas { Width = 32, Height = SliderH };

            var track = new Rectangle
            {
                Width   = 4,
                Height  = SliderH,
                Fill    = new SolidColorBrush(Color.FromRgb(20, 20, 40)),
                RadiusX = 2, RadiusY = 2
            };
            Canvas.SetLeft(track, 14);
            Canvas.SetTop(track, 0);
            canvas.Children.Add(track);

            var tick = new Rectangle
            {
                Width  = 12,
                Height = 1,
                Fill   = new SolidColorBrush(Color.FromRgb(42, 42, 74))
            };
            Canvas.SetLeft(tick, 10);
            Canvas.SetTop(tick, CenterY);
            canvas.Children.Add(tick);

            var fill = new Rectangle
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

            var thumb = new Ellipse
            {
                Width           = 13,
                Height          = 13,
                Fill            = new SolidColorBrush(Color.FromRgb(11, 11, 22)),
                Stroke          = brush,
                StrokeThickness = 2
            };
            Canvas.SetLeft(thumb, 9.5);
            Canvas.SetTop(thumb, CenterY - 6.5);
            canvas.Children.Add(thumb);
            _sliderThumbs[idx] = thumb;

            // Transparent overlay slider — Opacity=0 keeps it invisible but still captures input
            var slider = new Slider
            {
                Orientation   = Orientation.Vertical,
                Minimum       = -12,
                Maximum       = 12,
                TickFrequency = 1,
                Width         = 32,
                Height        = SliderH,
                Opacity       = 0
            };
            slider.ValueChanged += (_, _) => OnSliderChanged(idx);
            slider.DoubleTapped += (_, _) => { slider.Value = 0; };
            _sliders[idx] = slider;

            var overlay = new Grid { Width = 32, Height = SliderH };
            overlay.Children.Add(canvas);
            overlay.Children.Add(slider);

            var freqLabel = new TextBlock
            {
                Text                = BandLabels[i] + "Hz",
                Foreground          = new SolidColorBrush(Color.FromRgb(42, 42, 74)),
                HorizontalAlignment = HorizontalAlignment.Center,
                FontSize            = 10,
                Margin              = new Thickness(0, 4, 0, 0)
            };

            var col = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            ToolTip.SetTip(col, BandTooltips[i]);
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
        ChipPanel.Children.Clear();
        _chips.Clear();
        _presetManager.Presets.ToList().ForEach(p => AddChip(p.Name, onClick: () => OnChipClick(p.Name)));
        AddChip("Custom", onClick: null);
    }

    private void AddChip(string name, Action? onClick)
    {
        var chip = new ToggleButton
        {
            Content = name,
            Theme   = ChipTheme
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
            chip.Click += (_, _) => { chip.IsChecked = true; };
        }

        _chips.Add((name, chip, chip));
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
        _transitionTimer       = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _transitionTimer.Tick += TransitionTick;
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
        foreach (var (n, chip, _) in _chips)
            chip.IsChecked = n == name;
        _suppressPresetChange = false;
    }

    // ── State / events ───────────────────────────────────────────────────────

    private void RestoreState()
    {
        if (_settings.BandGains.All(g => g == 0f) && !string.IsNullOrEmpty(_settings.DefaultPreset))
        {
            var defaultPreset = _presetManager.Get(_settings.DefaultPreset);
            if (defaultPreset != null)
            {
                Array.Copy(defaultPreset.Bands, _settings.BandGains, 10);
                _settings.ActivePreset = _settings.DefaultPreset;
            }
        }

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

        Dispatcher.UIThread.InvokeAsync(() =>
        {
            for (int i = 0; i < 10; i++) UpdateSliderVisual(i);
        }, DispatcherPriority.Loaded);

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
        SetActiveChip("Custom");
        _settings.ActivePreset = "";
        _settings.Save();
        SetVizTargets();

        if (_settings.EqEnabled)
            ApplyCurrentGains();
    }

    private void ResetAllBands_Click(object? sender, RoutedEventArgs e)
    {
        for (int i = 0; i < 10; i++)
            _transitionTarget[i] = 0f;
        _settings.ActivePreset = "";
        SetActiveChip("Custom");
        _settings.Save();
        StartPresetTransition();
    }

    private void ToggleButton_Click(object? sender, RoutedEventArgs e)
    {
        SetEqState(!_settings.EqEnabled, writeConfig: true);
        _settings.Save();
    }

    private void BoostButton_Click(object? sender, RoutedEventArgs e)
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
            BoostButton.Theme   = PrimaryButtonTheme;
        }
        else
        {
            BoostButton.Content = "⚡ BOOST";
            BoostButton.Theme   = null;
        }
    }

    private void SetEqState(bool enabled, bool writeConfig)
    {
        _settings.EqEnabled = enabled;

        if (enabled)
        {
            ToggleButton.Content   = "■ DISABLE";
            ToggleButton.Theme     = DangerButtonTheme;
            StatusLabel.Text       = "EQ ACTIVE";
            StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(167, 139, 250));
            StatusDot.Fill         = new SolidColorBrush(Color.FromRgb(124, 58, 237));
            StatusPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x7c, 0x3a, 0xed));
            _pulseTimer?.Start();
            if (writeConfig) ApplyCurrentGains();
        }
        else
        {
            ToggleButton.Content   = "▶ ENABLE";
            ToggleButton.Theme     = PrimaryButtonTheme;
            StatusLabel.Text       = "EQ OFF";
            StatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(68, 68, 90));
            StatusDot.Fill         = new SolidColorBrush(Color.FromRgb(42, 42, 68));
            StatusDot.Opacity      = 1;
            StatusPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x44, 0x44, 0x5a));
            _pulseTimer?.Stop();
            if (writeConfig) SafeBypass();
        }

        _tray?.SetEqState(enabled);
        RefreshTrayTooltip();
    }

    public void ToggleEqFromTray()
    {
        SetEqState(!_settings.EqEnabled, writeConfig: true);
        _settings.Save();
        SyncMiniWindow();
    }

    private void RefreshTrayTooltip()
    {
        string preset = string.IsNullOrEmpty(_settings.ActivePreset) ? "Custom" : _settings.ActivePreset;
        _tray?.UpdateTooltip(preset, _settings.EqEnabled, _settings.BoostEnabled, _settings.BoostDb);
    }

    private const double SettingsScrollStep = 60;

    private void ScrollUpButton_Click(object? sender, RoutedEventArgs e)
        => SettingsScrollViewer.Offset = new Vector(SettingsScrollViewer.Offset.X, Math.Max(0, SettingsScrollViewer.Offset.Y - SettingsScrollStep));

    private void ScrollDownButton_Click(object? sender, RoutedEventArgs e)
        => SettingsScrollViewer.Offset = new Vector(SettingsScrollViewer.Offset.X, Math.Min(SettingsScrollViewer.Extent.Height - SettingsScrollViewer.Viewport.Height, SettingsScrollViewer.Offset.Y + SettingsScrollStep));

    private void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        _settingsPanelOpen       = !_settingsPanelOpen;
        SettingsPanel.IsVisible  = _settingsPanelOpen;
        SettingsNavButton.Content = _settingsPanelOpen ? "← Back" : "⚙ Settings";

        if (_settingsPanelOpen)
            PopulateSettingsPanel();
        else
        {
            RefreshBoostButton();
            RefreshAutoPresetTimer();
            if (_settings.EqEnabled) ApplyCurrentGains();
        }
    }

    // ── Settings panel ───────────────────────────────────────────────────────

    private const string RunKey  = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "GamingEqualizer";

    private void PopulateSettingsPanel()
    {
        // Hide launch-with-windows on non-Windows
        if (LaunchWithWindowsPanel != null)
            LaunchWithWindowsPanel.IsVisible = OperatingSystem.IsWindows();

        _suppressSettings = true;
        LaunchWithWindowsCheck.IsChecked = IsStartupRegistered();
        DefaultPresetCombo.Items.Clear();
        foreach (var preset in _presetManager.Presets)
            DefaultPresetCombo.Items.Add(preset.Name);
        DefaultPresetCombo.SelectedItem = string.IsNullOrEmpty(_settings.DefaultPreset)
            ? "Flat" : _settings.DefaultPreset;
        BoostEnabledCheck.IsChecked = _settings.BoostEnabled;
        BoostSlider.Value           = _settings.BoostDb;
        BoostLabel.Text             = $"+{_settings.BoostDb:0} dB";
        AutoPresetCheck.IsChecked   = _settings.AutoPresetEnabled;

        _mappingRows.Clear();
        foreach (var kv in _settings.ProcessPresetMap)
            _mappingRows.Add(new ProcessMappingRow { Exe = kv.Key, Preset = kv.Value });
        MappingList.ItemsSource = _mappingRows;

        NewPresetCombo.Items.Clear();
        foreach (var p in _presetManager.Presets)
            NewPresetCombo.Items.Add(p.Name);
        if (NewPresetCombo.Items.Count > 0)
            NewPresetCombo.SelectedIndex = 0;
        _suppressSettings = false;
    }

    private static bool IsStartupRegistered()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(AppName) != null;
        }
        catch { return false; }
    }

    private void LaunchWithWindows_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressSettings || !OperatingSystem.IsWindows()) return;
        bool enable = LaunchWithWindowsCheck.IsChecked == true;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (enable)
            {
                var exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule!.FileName;
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
            _suppressSettings = true;
            LaunchWithWindowsCheck.IsChecked = !enable;
            _suppressSettings = false;
        }
    }

    private void DefaultPresetCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettings || DefaultPresetCombo.SelectedItem is not string presetName) return;
        _settings.DefaultPreset = presetName;
        _settings.Save();
    }

    private async void RerunCalibration_Click(object? sender, RoutedEventArgs e)
    {
        var wizard = new CalibrationWizard(_settings);
        bool result = await wizard.ShowDialog<bool>(this);
        if (result && wizard.ResultGains != null)
        {
            _settings.LastCalibration      = wizard.ResultGains;
            _settings.LastCalibrationLeft  = wizard.ResultGainsLeft;
            _settings.LastCalibrationRight = wizard.ResultGainsRight;
            _settings.Save();
            ApplyCalibrationGains(wizard.ResultGains);
        }
    }

    private void Boost_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressSettings) return;
        _settings.BoostEnabled = BoostEnabledCheck.IsChecked == true;
        _settings.Save();
        RefreshBoostButton();
        if (_settings.EqEnabled) ApplyCurrentGains();
    }

    private void BoostSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSettings) return;
        _settings.BoostDb = (float)BoostSlider.Value;
        BoostLabel.Text   = $"+{_settings.BoostDb:0} dB";
        _settings.Save();
        RefreshBoostButton();
        if (_settings.EqEnabled) ApplyCurrentGains();
    }

    private static readonly string PresetsDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "Presets");

    private async void ExportPreset_Click(object? sender, RoutedEventArgs e)
    {
        string presetName = string.IsNullOrEmpty(_settings.ActivePreset) ? "Custom" : _settings.ActivePreset;
        var result = await this.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title           = "Export Preset",
            SuggestedFileName = $"{presetName}.json",
            FileTypeChoices = new[] { new Avalonia.Platform.Storage.FilePickerFileType("JSON preset") { Patterns = new[] { "*.json" } } }
        });
        if (result == null) return;
        try
        {
            var preset = new Preset { Name = presetName, Bands = (float[])_settings.BandGains.Clone() };
            var path   = result.Path.LocalPath;
            File.WriteAllText(path, JsonConvert.SerializeObject(preset, Formatting.Indented));
            await MsgBox.Info($"Preset exported to:\n{path}", "Exported", this);
        }
        catch (Exception ex)
        {
            await MsgBox.Info($"Export failed:\n{ex.Message}", "Error", this);
        }
    }

    private async void ImportPreset_Click(object? sender, RoutedEventArgs e)
    {
        var results = await this.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title           = "Import Preset",
            AllowMultiple   = false,
            FileTypeFilter  = new[] { new Avalonia.Platform.Storage.FilePickerFileType("JSON preset") { Patterns = new[] { "*.json" } } }
        });
        if (results.Count == 0) return;
        try
        {
            var path   = results[0].Path.LocalPath;
            var json   = File.ReadAllText(path);
            var preset = JsonConvert.DeserializeObject<Preset>(json);
            if (preset?.Bands == null || preset.Bands.Length != 10)
            {
                await MsgBox.Info("Invalid preset file — must have 10 band values.", "Import failed", this);
                return;
            }
            if (string.IsNullOrWhiteSpace(preset.Name))
                preset.Name = Path.GetFileNameWithoutExtension(path);
            foreach (char c in Path.GetInvalidFileNameChars())
                preset.Name = preset.Name.Replace(c, '_');
            var destPath = Path.Combine(PresetsDir, $"{preset.Name}.json");
            if (File.Exists(destPath))
            {
                bool overwrite = await MsgBox.Confirm($"A preset named '{preset.Name}' already exists. Overwrite?", "Conflict", this);
                if (!overwrite) return;
            }
            Directory.CreateDirectory(PresetsDir);
            File.WriteAllText(destPath, json);
            HandleImportedPreset(preset);
            await MsgBox.Info($"Preset '{preset.Name}' imported.", "Imported", this);
        }
        catch (Exception ex)
        {
            await MsgBox.Info($"Import failed:\n{ex.Message}", "Error", this);
        }
    }

    private async void CopyShareCode_Click(object? sender, RoutedEventArgs e)
    {
        string code = PresetShareCode.Encode(_settings.BandGains);
        await ((IClipboard)this.Clipboard!).SetTextAsync(code);
        await MsgBox.Info($"Share code copied to clipboard:\n\n{code}", "Share Code Copied", this);
    }

    private async void PasteShareCode_Click(object? sender, RoutedEventArgs e)
    {
        string? text = (await ((IClipboard)this.Clipboard!).TryGetTextAsync())?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            await MsgBox.Info("Clipboard is empty.", "Paste Share Code", this);
            return;
        }
        float[]? bands = PresetShareCode.Decode(text);
        if (bands == null)
        {
            await MsgBox.Info("Clipboard does not contain a valid share code.", "Paste Share Code", this);
            return;
        }
        var existingNames = _presetManager.Presets.Select(p => p.Name);
        var saveDlg = new SavePresetDialog(existingNames);
        bool saved = await saveDlg.ShowDialog<bool>(this);
        if (!saved || saveDlg.PresetName == null) return;
        try
        {
            Directory.CreateDirectory(PresetsDir);
            var preset = new Preset { Name = saveDlg.PresetName, Bands = bands };
            File.WriteAllText(Path.Combine(PresetsDir, $"{saveDlg.PresetName}.json"),
                JsonConvert.SerializeObject(preset, Formatting.Indented));
            HandleImportedPreset(preset);
            await MsgBox.Info($"Preset '{saveDlg.PresetName}' added.", "Preset Added", this);
        }
        catch (Exception ex)
        {
            await MsgBox.Info($"Failed to save preset:\n{ex.Message}", "Error", this);
        }
    }

    private async void ImportAutoEQ_Click(object? sender, RoutedEventArgs e)
    {
        var results = await this.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title          = "Import AutoEQ Parametric EQ File",
            AllowMultiple  = false,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("AutoEQ parametric EQ") { Patterns = new[] { "*.txt" } },
                new Avalonia.Platform.Storage.FilePickerFileType("All files")            { Patterns = new[] { "*.*" } }
            }
        });
        if (results.Count == 0) return;
        var path    = results[0].Path.LocalPath;
        float[]? bands = AutoEQImporter.Import(path);
        if (bands == null)
        {
            await MsgBox.Info(
                "Could not parse the file. Make sure it is an AutoEQ parametric EQ .txt file " +
                "containing lines like:\n  Filter 1: ON PK Fc 105 Hz Gain 6.6 dB Q 0.69",
                "Import AutoEQ", this);
            return;
        }
        string suggestedName = Path.GetFileNameWithoutExtension(path);
        var existingNames = _presetManager.Presets
            .Select(p => p.Name)
            .Where(n => !n.Equals(suggestedName, StringComparison.OrdinalIgnoreCase));
        var saveDlg = new SavePresetDialog(existingNames, suggestedName);
        bool saved  = await saveDlg.ShowDialog<bool>(this);
        if (!saved || saveDlg.PresetName == null) return;
        try
        {
            Directory.CreateDirectory(PresetsDir);
            var preset = new Preset { Name = saveDlg.PresetName, Bands = bands };
            File.WriteAllText(Path.Combine(PresetsDir, $"{saveDlg.PresetName}.json"),
                JsonConvert.SerializeObject(preset, Formatting.Indented));
            HandleImportedPreset(preset);
            await MsgBox.Info($"AutoEQ profile '{saveDlg.PresetName}' imported.", "AutoEQ Imported", this);
        }
        catch (Exception ex)
        {
            await MsgBox.Info($"Failed to save preset:\n{ex.Message}", "Error", this);
        }
    }

    private void AutoPreset_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressSettings) return;
        _settings.AutoPresetEnabled = AutoPresetCheck.IsChecked == true;
        _settings.Save();
        RefreshAutoPresetTimer();
    }

    private const string ExePlaceholder = "process.exe";

    private void NewExeBox_GotFocus(object? sender, RoutedEventArgs e)
    {
        if (NewExeBox.Text == ExePlaceholder)
        {
            NewExeBox.Text       = "";
            NewExeBox.Foreground = TextBrush;
        }
    }

    private void NewExeBox_LostFocus(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NewExeBox.Text))
        {
            NewExeBox.Text       = ExePlaceholder;
            NewExeBox.Foreground = TextDimBrush;
        }
    }

    private void AddMapping_Click(object? sender, RoutedEventArgs e)
    {
        string exe    = NewExeBox.Text?.Trim() ?? "";
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

        NewExeBox.Text       = ExePlaceholder;
        NewExeBox.Foreground = TextDimBrush;
    }

    private void RemoveMapping_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string exe) return;
        _settings.ProcessPresetMap.Remove(exe);
        _settings.Save();
        var row = _mappingRows.FirstOrDefault(r => r.Exe == exe);
        if (row != null) _mappingRows.Remove(row);
    }

    private void LiveModeButton_Click(object? sender, RoutedEventArgs e)
        => ToggleLiveMode((Button)sender!);

    private void CalibrationButton_Click(object? sender, RoutedEventArgs e)
        => OpenCalibrationWizard();

    public async void OpenCalibrationWizard()
    {
        var wizard = new CalibrationWizard(_settings);
        bool result = await wizard.ShowDialog<bool>(this);
        if (result && wizard.ResultGains != null)
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
            _sliders[i].Value       = gains[i];
            _gainLabels[i].Text     = FormatGain(gains[i]);
            _settings.BandGains[i] = gains[i];
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
                float[] avg   = _settings.LastCalibration ?? _settings.BandGains;
                float[] left  = BlendWithPreset(_settings.BandGains, avg, _settings.LastCalibrationLeft);
                float[] right = BlendWithPreset(_settings.BandGains, avg, _settings.LastCalibrationRight);
                _backend.ApplyPerEar(left, right, _settings.BoostEnabled ? _settings.BoostDb : 0f);
            }
            else
            {
                _backend.Apply(_settings.BandGains, _settings.BoostEnabled ? _settings.BoostDb : 0f);
            }
            HideErrorBanner();
        }
        catch (Exception ex) { ShowErrorBanner($"Failed to apply EQ: {ex.Message}"); }
        RefreshTrayTooltip();
    }

    private static float[] BlendWithPreset(float[] preset, float[] calAvg, float[] calSide)
    {
        float[] result = new float[10];
        for (int i = 0; i < 10; i++)
            result[i] = Math.Clamp(preset[i] + (calSide[i] - calAvg[i]), -12f, 12f);
        return result;
    }

    private void SafeBypass()
    {
        try   { _backend.Bypass(); HideErrorBanner(); }
        catch (Exception ex) { ShowErrorBanner($"Failed to bypass EQ: {ex.Message}"); }
    }

    public void BypassAndQuit()
    {
        try { _backend.Bypass(); } catch { }
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lt)
            lt.Shutdown();
    }

    private void ShowErrorBanner(string message)
    {
        ErrorText.Text     = message;
        ErrorBanner.IsVisible = true;
    }

    private void HideErrorBanner() => ErrorBanner.IsVisible = false;

    private void ShowEqApoMissingBanner()
    {
        ShowErrorBanner("EqualizerAPO is not installed at C:\\Program Files\\EqualizerAPO\\. " +
                        "EQ controls are disabled. Install EqualizerAPO and restart the app.");
        foreach (var s in _sliders) s.IsEnabled = false;
        ToggleButton.IsEnabled = false;
    }

    // Close to tray instead of quitting
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private static string FormatGain(float v)
        => v >= 0 ? $"+{v:F0}" : $"{v:F0}";

    // ── Status dot pulse ─────────────────────────────────────────────────────

    private void StartPulse()
    {
        _pulseTimer?.Stop();
        _pulseTimer       = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _pulseTimer.Tick += (_, _) =>
        {
            _pulseHigh         = !_pulseHigh;
            StatusDot.Opacity  = _pulseHigh ? 1.0 : 0.35;
        };
        if (_settings.EqEnabled)
            _pulseTimer.Start();
    }

    // ── Visualizer ───────────────────────────────────────────────────────────

    private void BuildVisualizer()
    {
        VisualizerCanvas.Children.Clear();
        _vizTimer?.Stop();

        _vizCenter = new Rectangle
        {
            Height = 1,
            Fill   = new SolidColorBrush(Color.FromArgb(60, 30, 30, 58))
        };
        VisualizerCanvas.Children.Add(_vizCenter);

        for (int j = 0; j < VizBars; j++)
        {
            double t     = j / (double)(VizBars - 1);
            var    brush = new SolidColorBrush(VizBarColor(j, 0.5, t));
            var    bar   = new Rectangle { Fill = brush, RadiusX = 1, RadiusY = 1 };
            _vizBrushes[j] = brush;
            VisualizerCanvas.Children.Add(bar);
            _vizBars[j] = bar;
        }
        ApplyVizColorMode();

        _vizTimer       = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _vizTimer.Tick += VizTick;
        _vizTimer.Start();
    }

    private void VisualizerCanvas_SizeChanged(object? sender, SizeChangedEventArgs e)
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
        if (_vizBars[0] == null) return; // not yet built
        if (_positioningVizBars) return;
        _positioningVizBars = true;
        try
        {
            double w = VisualizerCanvas.Bounds.Width;
            double h = VisualizerCanvas.Bounds.Height;
            if (w <= 0 || h <= 0) return;

            double midY = h / 2.0;
            double maxH = midY - 3;
            double step = w / VizBars;
            double barW = Math.Max(1, step - 1.2);

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

                if (_settings.VizColorMode == 2)
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
        finally { _positioningVizBars = false; }
    }

    private void SetVizTargets()
    {
        if (_liveMode) return;

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

    private void MiniModeButton_Click(object? sender, RoutedEventArgs e)
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

    private void SyncMiniWindow() => _miniWindow?.RefreshUI();

    // ── Save preset ──────────────────────────────────────────────────────────

    private async void SavePresetButton_Click(object? sender, RoutedEventArgs e)
    {
        var existingNames = _chips
            .Where(c => c.Name != "Custom")
            .Select(c => c.Name);

        var dlg    = new SavePresetDialog(existingNames);
        bool saved = await dlg.ShowDialog<bool>(this);
        if (!saved || dlg.PresetName == null) return;

        string name = dlg.PresetName;
        try
        {
            var preset = new Preset { Name = name, Bands = (float[])_settings.BandGains.Clone() };
            var dir    = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Presets");
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
            Theme   = ChipTheme
        };
        chip.Click += (_, _) =>
        {
            if (_suppressPresetChange) return;
            OnChipClick(name);
        };

        Control container;
        if (!BuiltInPresets.Contains(name))
        {
            var deleteBtn = new Button
            {
                Content         = "✕",
                FontSize        = 9,
                Width           = 16,
                Height          = 16,
                Padding         = new Thickness(0),
                Margin          = new Thickness(-6, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Theme           = IconButtonTheme
            };
            ToolTip.SetTip(deleteBtn, $"Delete preset '{name}'");
            deleteBtn.Click += (_, _) => DeletePresetChip(name);

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(chip);
            panel.Children.Add(deleteBtn);
            container = panel;
        }
        else
        {
            container = chip;
        }

        int idx = _chips.Count - 1; // insert before Custom
        _chips.Insert(idx, (name, chip, container));
        ChipPanel.Children.Insert(idx, container);

        _miniWindow?.AddChip(name);
    }

    private void HandleImportedPreset(Preset preset)
    {
        _presetManager.Reload();
        if (_chips.Any(c => c.Name == preset.Name)) return;
        InsertPresetChip(preset.Name);
    }

    private static readonly HashSet<string> BuiltInPresets =
        new(StringComparer.OrdinalIgnoreCase) { "Flat", "FPS", "RPG", "Cinematic", "Music", "PUBG" };

    private void DeletePresetChip(string name)
    {
        var destPath = Path.Combine(PresetsDir, $"{name}.json");
        try { File.Delete(destPath); } catch { }

        _presetManager.Reload();
        var entry = _chips.FirstOrDefault(c => c.Name == name);
        if (entry != default)
        {
            _chips.Remove(entry);
            ChipPanel.Children.Remove(entry.Container);
        }

        if (_settings.ActivePreset == name)
        {
            _settings.ActivePreset = "Flat";
            OnChipClick("Flat");
        }
    }

    // ── Win32 hotkey WndProc ─────────────────────────────────────────────────

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == (uint)HotkeyManager.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyManager.HK_TOGGLE)
                Dispatcher.UIThread.InvokeAsync(() => { SetEqState(!_settings.EqEnabled, true); _settings.Save(); SyncMiniWindow(); });
            else if (id == HotkeyManager.HK_CYCLE)
                Dispatcher.UIThread.InvokeAsync(() => { CyclePreset(); SyncMiniWindow(); });
            return IntPtr.Zero;
        }
        return CallWindowProc(_originalWndProc, hwnd, msg, wParam, lParam);
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
                _autoPresetTimer       = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
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
        if (!OperatingSystem.IsWindows()) return;
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

    // ── Live mode ────────────────────────────────────────────────────────────

    internal void ToggleLiveMode(Button liveButton)
    {
        _liveMode = !_liveMode;

        if (_liveMode)
        {
            try
            {
                _spectrum = new AudioSpectrumAnalyzer();
                _spectrum.OnSpectrum = bars => Dispatcher.UIThread.InvokeAsync(() =>
                {
                    for (int j = 0; j < VizBars; j++)
                        _vizTarget[j] = bars[j];
                });
                _spectrum.Start();
                liveButton.Content = "◉ LIVE";
                liveButton.Theme   = PrimaryButtonTheme;
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
            liveButton.Theme   = null; // revert to implicit button theme
            SetVizTargets();
        }
    }
}
