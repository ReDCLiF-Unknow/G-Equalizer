#pragma warning disable CA1416 // Windows-specific APIs guarded by OperatingSystem.IsWindows()

using System.Runtime.Versioning;
using Avalonia.Input.Platform;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
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

    // Mirrored copies drawn below the centre line, only while audio-driven. They deliberately
    // share each bar's brush object rather than owning one, so every colour change follows
    // automatically without a second update pass.
    private readonly Rectangle[] _vizReflections = new Rectangle[VizBars];
    private const double         ReflectionOpacity = 0.22;

    // Horizontal background-coloured stripes laid over the whole canvas. One overlay segments
    // every bar and reflection at once, which is far cheaper than splitting 80 bars into
    // stacked blocks and repositioning all of them each frame.
    private readonly List<Rectangle> _vizStripes    = new();
    private const double             StripePitch    = 5.0;   // segment + gap, in px
    private const double             StripeGap      = 2.0;   // the punched-out part

    // Live audio
    private bool                   _liveMode;
    private AudioSpectrumAnalyzer? _spectrum;

    // Beat mode: pulse the bars in time with the music instead of tracing the spectrum.
    // Both modes read the same capture, so they share the analyzer and are mutually exclusive.
    private bool _beatMode;

    /// <summary>True when the bars are driven by audio rather than by the EQ curve.</summary>
    private bool AudioDriven => _liveMode || _beatMode;

    // The loopback capture dies on its own whenever the default playback device changes —
    // most commonly a wireless headset reconnecting — with no event to distinguish that from a
    // capture we stopped on purpose. This flag is that distinction; the counter/window below
    // bound how many silent recovery attempts get made before giving up and telling the user.
    private bool     _analyzerStoppingIntentionally;
    private int      _analyzerRecoveryAttempts;
    private DateTime _analyzerRecoveryWindowStart = DateTime.MinValue;

    // Onset detection runs per frequency band rather than on the kick band alone, so a snare,
    // a hi-hat or a synth stab each pulse their own slice of the row instead of everything
    // riding on the bass alone. Nine bands: the original five (kick/bass/mid/presence/treble)
    // with one extra band interleaved between each pair, roughly doubling frequency resolution.
    // Boundaries are bar indices into the analyzer's 80 log-spaced bars (20 Hz-20 kHz):
    // {0,13,21,29,37,45,53,61,69,80} sits close to 20/60/120/250/500/1000/2000/4000/8000/20000 Hz.
    // Bars are ~85 ms apart (4096 samples at 48 kHz), so a beat lands within about a frame of
    // the real one — fine for a visual pulse.
    private static readonly int[]    BeatBandBounds = { 0, 13, 21, 29, 37, 45, 53, 61, 69, VizBars };
    private static readonly int      BeatBandCount  = BeatBandBounds.Length - 1;
    // Bass sustains into a boomy decay; treble is a short tick. One rate per band reads more
    // like the actual instruments than a single shared decay would.
    private static readonly double[] BeatBandDecay  = { 0.94, 0.93, 0.92, 0.91, 0.90, 0.89, 0.88, 0.87, 0.85 };
    // The interleaved (even-index-gap) bands get a lighter tint and partial alpha in
    // PositionVizBars so they read as the "extra" bands sitting between the original five.
    private static readonly bool[]   BeatBandInterleaved = { false, true, false, true, false, true, false, true, false };

    private const int    BeatHistoryFrames  = 18;    // ~1.5 s of context
    private const double BeatThreshold      = 1.25;  // energy must exceed this × the local average
    private const double BeatFloor          = 0.6;   // ignore near-silence
    private const double BeatRefractoryMs   = 200;   // caps at ~300 BPM per band, stops double triggers
    private const double BeatRise           = 1.15;  // must be climbing, not merely loud
    // A hard, instantaneous stop (a track cut, a sample with a clipped release) makes an FFT
    // frame straddle real signal and true digital silence, and that discontinuity leaks energy
    // across every bin — every band spikes at once and looks exactly like a broadband onset,
    // then the very next frame reads a literal 0.000. A real sound, even decaying fast, never
    // produces an exact zero on the frame right after a hit. So a detected rise is held for one
    // extra frame (~85 ms, not perceptible) and only actually pulses if the following frame's
    // energy clears this — comfortably above true silence, comfortably below anything a real,
    // if quiet, decay would leave behind.
    private const double BeatClickRejectFloor = 0.03;

    private readonly double[][]  _beatHistory          = InitBandArrays(BeatBandCount, BeatHistoryFrames);
    private readonly int[]       _beatHistoryCount     = new int[BeatBandCount];
    private readonly int[]       _beatHistoryPos       = new int[BeatBandCount];
    private readonly double[]    _beatPrevEnergy       = new double[BeatBandCount];
    private readonly DateTime[]  _lastBeatAt           = InitDates(BeatBandCount);
    private readonly double[]    _beatPulse            = new double[BeatBandCount];   // 0-1 per band, decayed every tick
    private readonly bool[]      _beatCandidatePending = new bool[BeatBandCount];
    private readonly double[]    _beatCandidateStrength = new double[BeatBandCount];

    private static double[][] InitBandArrays(int bands, int frames)
    {
        var a = new double[bands][];
        for (int i = 0; i < bands; i++) a[i] = new double[frames];
        return a;
    }

    private static DateTime[] InitDates(int bands)
    {
        var a = new DateTime[bands];
        Array.Fill(a, DateTime.MinValue);
        return a;
    }

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

        // Before any control is built, so sliders and visualizer bars pick up the
        // saved accent on their first draw rather than flashing the default violet.
        ThemeColors.Apply(_settings);
        BannerHintBrush.Color = ThemeColors.Start;

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
        RestoreAudioVizMode();
        StartPulse();
        RefreshAutoPresetTimer();

        CheckEqBackendHealth();

        if (OperatingSystem.IsWindows())
        {
            var hwnd = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
            if (hwnd != IntPtr.Zero)
            {
                // Re-registering on every OnOpened is safe (Unregister first) and keeps the
                // hotkeys correct after a hide-to-tray/restore cycle.
                HotkeyManager.Unregister(hwnd);
                WarnAboutFailedHotkeys(HotkeyManager.Register(hwnd, _settings));

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

        // Same reasoning as StopAnalyzerIfIdle: Dispose() fires RecordingStopped synchronously,
        // and without this flag HandleAnalyzerStopped would see AudioDriven still true (this
        // does not touch _liveMode/_beatMode) and try to start a fresh capture on a window that
        // is being torn down.
        _analyzerStoppingIntentionally = true;
        _spectrum?.Dispose();
        _analyzerStoppingIntentionally = false;

        if (WindowState == WindowState.Normal)
        {
            _settings.WindowWidth  = Width;
            _settings.WindowHeight = Height;
            _settings.Save();
        }

        base.OnClosed(e);
    }

    // ── Band color ──────────────────────────────────────────────────────────

    private static Color BandColor(double t) => ThemeColors.Band(t);

    // Fixed neon palette, deliberately independent of the user's accent: this mode exists to
    // reproduce a specific pink-to-cyan look, so it does not follow ThemeColors the way the
    // gradient/solid modes do. Pick a different mode to get accent-coloured bars back.
    private static readonly Color NeonStart = Color.Parse("#ff2ea6");
    private static readonly Color NeonEnd   = Color.Parse("#38bdf8");

    private static Color NeonColor(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromRgb(
            (byte)(NeonStart.R + (NeonEnd.R - NeonStart.R) * t),
            (byte)(NeonStart.G + (NeonEnd.G - NeonStart.G) * t),
            (byte)(NeonStart.B + (NeonEnd.B - NeonStart.B) * t));
    }

    private Color VizBarColor(int barIndex, double intensity, double t)
    {
        return _settings.VizColorMode switch
        {
            1 => ThemeColors.Start,
            2 => PeakGlowColor(intensity, t),
            3 => NeonColor(t),
            _ => BandColor(t)
        };
    }

    /// <summary>Blends a colour toward white and lowers its alpha — used to mark the
    /// interleaved beat bands as "a lighter, dimmer version of the same colour" regardless
    /// of which of the three viz colour modes is active.</summary>
    private static Color LightenAndFade(Color c, double amount, double alpha)
    {
        byte r = (byte)(c.R + (255 - c.R) * amount);
        byte g = (byte)(c.G + (255 - c.G) * amount);
        byte b = (byte)(c.B + (255 - c.B) * amount);
        return Color.FromArgb((byte)(255 * alpha), r, g, b);
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

    private static readonly string[] VizColorModeLabels = { "◈ GRADIENT", "◈ SOLID", "◈ PEAK GLOW", "◈ NEON" };

    private void ApplyVizColorMode()
    {
        // Clamp rather than index blindly: a settings file written by a build with more modes
        // than this one would otherwise throw straight into the Settings panel.
        if (_settings.VizColorMode < 0 || _settings.VizColorMode >= VizColorModeLabels.Length)
            _settings.VizColorMode = 0;

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
        _settings.VizColorMode = (_settings.VizColorMode + 1) % VizColorModeLabels.Length;
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

        foreach (var preset in VisiblePresets())
            AddChip(preset.Name, onClick: () => OnChipClick(preset.Name));

        AddChip("Custom", onClick: null);
    }

    /// <summary>
    /// Presets the user has not hidden. Everything that walks presets — the chip row,
    /// Cycle, and the 1..9 hotkeys — goes through here, so hiding one renumbers the
    /// rest and the hotkey numbers keep matching the chips on screen.
    /// </summary>
    private IEnumerable<Preset> VisiblePresets() =>
        _presetManager.Presets.Where(p => !_settings.HiddenPresets.Contains(p.Name));

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

        ApplyIfEnabled();

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

        ApplyIfEnabled();
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
        ApplyIfEnabled();
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
            StatusLabel.Foreground = new SolidColorBrush(ThemeColors.AccentText);
            StatusDot.Fill         = new SolidColorBrush(ThemeColors.Start);
            StatusPill.BorderBrush = new SolidColorBrush(
                Color.FromArgb(0x55, ThemeColors.Start.R, ThemeColors.Start.G, ThemeColors.Start.B));
            _pulseTimer?.Start();
            HideErrorBanner();   // clears the "EQ is switched off" hint, if shown
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

    /// <summary>Bring the window back from the tray. Used by the tray icon and by a second
    /// instance asking us to surface.</summary>
    internal void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
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
        // Leaving the panel mid-capture would otherwise strand the hotkeys unregistered.
        CancelHotkeyCapture();

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

    private void PopulateSettingsPanel()
    {
        // Hide launch-with-windows on non-Windows
        if (LaunchWithWindowsPanel != null)
            LaunchWithWindowsPanel.IsVisible = OperatingSystem.IsWindows();

        // The SETTINGS heading is fixed while this list scrolls, so a retained offset makes a
        // scrolled panel look like the top of it — which is how "Launch with Windows" (the
        // first row) came to look like a dead control while a different row was being clicked.
        SettingsScrollViewer.Offset = new Vector(0, 0);

        // try/finally matters here: _suppressSettings gates every handler in this panel, so a
        // throw anywhere below would leave it stuck true and silently deaden the entire
        // Settings panel until restart, with nothing logged.
        _suppressSettings = true;
        try
        {
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
            RefreshHotkeyControls();
            RefreshPresetVisibilityPanel();
            RefreshAccentControls();

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
        finally
        {
            _suppressSettings = false;
        }
    }

    private static bool IsStartupRegistered() => StartupTask.IsRegistered();

    private void LaunchWithWindows_Changed(object? sender, RoutedEventArgs e)
    {
        if (_suppressSettings || !OperatingSystem.IsWindows()) return;
        bool enable = LaunchWithWindowsCheck.IsChecked == true;
        try
        {
            if (enable) StartupTask.Register();
            else        StartupTask.Unregister();

            _settings.LaunchWithWindows = enable;
            _settings.Save();
        }
        catch (Exception ex)
        {
            // This used to fail silently into the log, which is part of why the feature was
            // broken for so long without anyone noticing. Put it on screen.
            Logger.Log($"Failed to update the logon task: {ex.Message}");
            ShowErrorBanner($"Could not change \"Launch with Windows\": {ex.Message}");
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
        ApplyIfEnabled();
    }

    private void BoostSlider_ValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_suppressSettings) return;
        _settings.BoostDb = (float)BoostSlider.Value;
        BoostLabel.Text   = $"+{_settings.BoostDb:0} dB";
        _settings.Save();
        RefreshBoostButton();
        ApplyIfEnabled();
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
    {
        SetLiveMode(!_liveMode);
        _settings.VizLiveMode = _liveMode;
        _settings.VizBeatMode = _beatMode;   // turning LIVE on may have switched BEAT off
        _settings.Save();
    }

    private void BeatModeButton_Click(object? sender, RoutedEventArgs e)
    {
        SetBeatMode(!_beatMode);
        _settings.VizBeatMode = _beatMode;
        _settings.VizLiveMode = _liveMode;   // turning BEAT on may have switched LIVE off
        _settings.Save();
    }

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

    // Match the palette in App.axaml: ErrorBrush #db2777, AccentBrush #7c3aed.
    private static readonly SolidColorBrush BannerErrorBrush = new(Color.FromRgb(0xdb, 0x27, 0x77));
    // Mutated by ApplyAccentTheme — the hint banner follows the accent colour.
    private static readonly SolidColorBrush BannerHintBrush  = new(Color.FromRgb(0x7c, 0x3a, 0xed));

    private void ShowErrorBanner(string message)
    {
        ErrorText.Text         = message;
        ErrorBanner.Background = BannerErrorBrush;
        ErrorBanner.IsVisible  = true;
    }

    private void HideErrorBanner() => ErrorBanner.IsVisible = false;

    /// <summary>
    /// Same banner, accent-coloured: this is guidance, not a failure.
    /// </summary>
    private void ShowHintBanner(string message)
    {
        ErrorText.Text         = message;
        ErrorBanner.Background = BannerHintBrush;
        ErrorBanner.IsVisible  = true;
    }

    /// <summary>
    /// Apply the current gains, or explain why nothing happened when the EQ is off.
    /// Silently doing nothing here is exactly what makes a disabled EQ feel broken:
    /// the sliders move, the visualizer reacts, and the audio never changes.
    /// </summary>
    private void ApplyIfEnabled()
    {
        if (_settings.EqEnabled)
        {
            ApplyCurrentGains();
            return;
        }

        // Read the live binding rather than hardcoding it. This said "Ctrl+Alt+E" no matter
        // what the toggle was actually bound to, so after any rebind it pointed the user at a
        // combination that did nothing.
        string toggle = string.IsNullOrWhiteSpace(_settings.HotkeyToggle)
            ? string.Empty
            : $" (or press {_settings.HotkeyToggle})";

        ShowHintBanner("The EQ is switched off, so this won't change what you hear — "
                     + $"click ENABLE{toggle} to turn it on.");
    }

    private void ShowEqApoMissingBanner()
    {
        ShowErrorBanner("EqualizerAPO is not installed at C:\\Program Files\\EqualizerAPO\\. " +
                        "EQ controls are disabled. Install EqualizerAPO and restart the app.");
        foreach (var s in _sliders) s.IsEnabled = false;
        ToggleButton.IsEnabled = false;
    }

    /// <summary>
    /// "Installed" is not the same as "audible": EqualizerAPO can be present and accepting
    /// our config writes while Windows never loads it into the playback device's APO chain.
    /// Warn about that instead of silently doing nothing. Controls stay enabled — the config
    /// is still written correctly and starts working as soon as the user fixes the hookup.
    /// </summary>
    private void CheckEqBackendHealth()
    {
        if (!_backend.IsAvailable)
        {
            ShowEqApoMissingBanner();
            return;
        }

        if (!OperatingSystem.IsWindows()) return;

        switch (EqApoDiagnostics.GetStatus())
        {
            case EqApoStatus.NotAttached:
                ShowErrorBanner(
                    "EqualizerAPO is installed but is not active on your current playback device, " +
                    "so the EQ will not change what you hear. Open " +
                    "C:\\Program Files\\EqualizerAPO\\Configurator.exe, tick that device, then reboot. " +
                    "If it still has no effect, also enable \"Install as SFX/EFX\" under Troubleshooting.");
                break;
        }
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

            // Shares the bar's brush on purpose — see the field comment.
            var reflection = new Rectangle
            {
                Fill      = brush,
                RadiusX   = 1,
                RadiusY   = 1,
                Opacity   = ReflectionOpacity,
                IsVisible = false
            };
            VisualizerCanvas.Children.Add(reflection);
            _vizReflections[j] = reflection;
        }

        // Added last so they sit above every bar, punching the segment gaps.
        _vizStripes.Clear();
        var stripeBrush = new SolidColorBrush(Color.Parse("#07070f"));   // matches TitlebarBrush
        for (int i = 0; i < 64; i++)
        {
            var stripe = new Rectangle { Fill = stripeBrush, Height = StripeGap, IsVisible = false };
            VisualizerCanvas.Children.Add(stripe);
            _vizStripes.Add(stripe);
        }

        ApplyVizColorMode();

        _vizTimer       = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _vizTimer.Tick += VizTick;
        _vizTimer.Start();
    }

    private void VisualizerCanvas_SizeChanged(object? sender, SizeChangedEventArgs e)
        => PositionVizBars();

    // ── Headphone backdrop ──────────────────────────────────────────────────
    //
    // Randomly scattered, randomly rotated headphone outlines behind the EQ
    // sliders. Positions are rejection-sampled against a circular bound: the
    // 50x36 glyph fits inside a circle of radius HpRadius about its centre, so
    // keeping every pair of centres more than a diameter apart guarantees no
    // two icons overlap no matter how they are rotated.

    private const  double HpHalfW  = 19;   // glyph is 38 x 28 …
    private const  double HpHalfH  = 14;
    private static readonly double HpRadius = Math.Sqrt(HpHalfW * HpHalfW + HpHalfH * HpHalfH);

    private readonly Random       _hpRandom  = new();
    private readonly List<Point>  _hpCenters = new();
    private Size _hpLastSize;
    private bool _buildingHeadphones;

    private void HeadphoneLayer_SizeChanged(object? sender, SizeChangedEventArgs e)
    {
        // Only regenerate on a meaningful resize, so sub-pixel/transient size
        // changes can't churn the layout (or feed back into another pass).
        if (Math.Abs(e.NewSize.Width  - _hpLastSize.Width)  < 8 &&
            Math.Abs(e.NewSize.Height - _hpLastSize.Height) < 8)
            return;

        BuildHeadphoneBackdrop();
    }

    private void BuildHeadphoneBackdrop()
    {
        if (_buildingHeadphones) return;
        _buildingHeadphones = true;
        try
        {
            HeadphoneLayer.Children.Clear();
            _hpCenters.Clear();

            double w = HeadphoneLayer.Bounds.Width;
            double h = HeadphoneLayer.Bounds.Height;
            _hpLastSize = new Size(w, h);
            if (w < HpRadius * 2 || h < HpRadius * 2) return;

            // Sparse on purpose: this sits behind the sliders, so anything denser
            // reads as clutter around the thin slider tracks rather than texture.
            int    target  = (int)Math.Clamp(w * h / 46000d, 2, 7);
            double minDist = HpRadius * 2 + 10;

            for (int placed = 0, attempts = 0; placed < target && attempts < 400; attempts++)
            {
                double cx = HpRadius + _hpRandom.NextDouble() * (w - HpRadius * 2);
                double cy = HpRadius + _hpRandom.NextDouble() * (h - HpRadius * 2);

                bool clashes = false;
                foreach (var c in _hpCenters)
                {
                    double dx = c.X - cx, dy = c.Y - cy;
                    if (dx * dx + dy * dy < minDist * minDist) { clashes = true; break; }
                }
                if (clashes) continue;

                _hpCenters.Add(new Point(cx, cy));
                HeadphoneLayer.Children.Add(
                    MakeHeadphone(cx, cy, _hpRandom.NextDouble() * 360));
                placed++;
            }
        }
        finally { _buildingHeadphones = false; }
    }

    private static Avalonia.Controls.Shapes.Path MakeHeadphone(double cx, double cy, double angle)
    {
        // Drawn to fill the 38 x 28 box below; a Path does not scale its geometry to
        // Width/Height, it clips, so these must stay in step with HpHalfW/HpHalfH.
        var geo = new GeometryGroup();
        geo.Children.Add(Geometry.Parse("M 3.8,16 A 15.2,15.2 0 0 1 34.2,16"));  // headband
        geo.Children.Add(new EllipseGeometry(new Rect( 0.8, 14.4, 7.6, 12.2)));  // left cup
        geo.Children.Add(new EllipseGeometry(new Rect(29.6, 14.4, 7.6, 12.2)));  // right cup

        var path = new Avalonia.Controls.Shapes.Path
        {
            Data                  = geo,
            Stroke                = new SolidColorBrush(Color.Parse("#101020")),
            StrokeThickness       = 1.2,
            Width                 = HpHalfW * 2,
            Height                = HpHalfH * 2,
            RenderTransformOrigin = RelativePoint.Center,
            RenderTransform       = new RotateTransform(angle),
            IsHitTestVisible      = false
        };

        Canvas.SetLeft(path, cx - HpHalfW);
        Canvas.SetTop (path, cy - HpHalfH);
        return path;
    }

    private void VizTick(object? sender, EventArgs e)
    {
        double lerp = AudioDriven ? 0.30 : 0.15;
        double snap = AudioDriven ? 0.05 : 0.02;

        if (_beatMode)
        {
            for (int b = 0; b < BeatBandCount; b++)
                _beatPulse[b] *= BeatBandDecay[b];

            for (int i = 0; i < VizBars; i++)
            {
                int band = BandForBar(i);
                int lo   = BeatBandBounds[band];
                int hi   = BeatBandBounds[band + 1];

                // Each band gets its own arch, rising from zero at its own edges to a peak at
                // its own centre — tapering fully to zero (not a raised floor) is what makes a
                // quiet band actually sink out of view next to an active one, so five distinct
                // peaks stand apart instead of blurring into one continuous ridge across the
                // whole row.
                double localT = hi > lo + 1 ? (i - lo) / (double)(hi - lo - 1) : 0.5;
                double arch   = Math.Sin(Math.PI * localT);
                _vizTarget[i] = _beatPulse[band] * arch * 12.0;
            }
        }

        for (int i = 0; i < VizBars; i++)
        {
            double diff = _vizTarget[i] - _vizCurrent[i];
            if (Math.Abs(diff) > snap)
                _vizCurrent[i] += diff * lerp;
            else
                _vizCurrent[i] = _vizTarget[i];
        }

        if (!AudioDriven) _ripplePhase += 0.06;
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

                if (!AudioDriven)
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

                bool recolor = _settings.VizColorMode == 2 || _beatMode;
                if (recolor)
                {
                    double t = j / (double)(VizBars - 1);
                    double intensity = Math.Abs(_vizCurrent[j]) / 12.0;
                    Color color = VizBarColor(j, intensity, t);

                    // The four interleaved beat bands (between the original five) get a
                    // lighter, partly transparent version of the same colour rather than an
                    // unrelated hue, so they read as "the extra bands" without breaking the
                    // row's existing gradient/solid/peak-glow palette.
                    if (_beatMode && BeatBandInterleaved[BandForBar(j)])
                        color = LightenAndFade(color, amount: 0.35, alpha: 0.55);

                    _vizBrushes[j].Color = color;
                }

                // NEON only, and only while audio-driven. Restricting it to this mode keeps the
                // other three looking exactly as they always have, and the EQ-curve view already
                // uses the lower half for negative gains — a reflection there would collide with
                // real data rather than decorate empty space.
                var reflection = _vizReflections[j];
                if (_settings.VizColorMode == 3 && AudioDriven && gain > 0)
                {
                    reflection.IsVisible = true;
                    reflection.Width     = barW;
                    reflection.Height    = barH;
                    Canvas.SetLeft(reflection, x);
                    Canvas.SetTop(reflection, midY + 1);
                }
                else reflection.IsVisible = false;
            }

            PositionVizStripes(w, h);

            if (_vizCenter != null)
            {
                _vizCenter.Width = w;
                Canvas.SetTop(_vizCenter, midY);
            }
        }
        finally { _positioningVizBars = false; }
    }

    /// <summary>
    /// Lays the background-coloured stripes across the canvas, giving every bar and reflection
    /// the stacked-segment look in one pass. Only shown in NEON mode — the other three modes
    /// are meant to read as continuous bars.
    /// </summary>
    private void PositionVizStripes(double w, double h)
    {
        bool show = _settings.VizColorMode == 3;
        int needed = show ? (int)(h / StripePitch) : 0;

        for (int i = 0; i < _vizStripes.Count; i++)
        {
            var stripe = _vizStripes[i];
            if (i >= needed) { stripe.IsVisible = false; continue; }

            stripe.IsVisible = true;
            stripe.Width     = w;
            Canvas.SetLeft(stripe, 0);
            Canvas.SetTop(stripe, i * StripePitch);
        }
    }

    private void SetVizTargets()
    {
        if (AudioDriven) return;

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
        // A second copy of G-EQ broadcasts this instead of starting up, so clicking the
        // shortcut while we are already in the tray surfaces this window rather than leaving
        // a rival instance running that would silently hold none of the global hotkeys.
        if (msg != 0 && msg == Program.ShowWindowMessage)
        {
            Dispatcher.UIThread.InvokeAsync(RestoreFromTray);
            return IntPtr.Zero;
        }

        if (msg == (uint)HotkeyManager.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyManager.HK_TOGGLE)
                Dispatcher.UIThread.InvokeAsync(() => { SetEqState(!_settings.EqEnabled, true); _settings.Save(); SyncMiniWindow(); });
            else if (id == HotkeyManager.HK_CYCLE)
                Dispatcher.UIThread.InvokeAsync(() => { CyclePreset(); SyncMiniWindow(); });
            else if (id >= HotkeyManager.HK_PRESET_BASE &&
                     id <  HotkeyManager.HK_PRESET_BASE + HotkeyManager.PresetCount)
            {
                int index = id - HotkeyManager.HK_PRESET_BASE;
                Dispatcher.UIThread.InvokeAsync(() => { SelectPresetByIndex(index); SyncMiniWindow(); });
            }
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

    // ── Accent colour ────────────────────────────────────────────────────────

    private void RefreshAccentControls()
    {
        if (AccentToneBox.ItemsSource is null)
            AccentToneBox.ItemsSource = ThemeColors.Tones.Select(t => t.Name).ToList();
        AccentToneBox.SelectedIndex = Math.Clamp(_settings.AccentTone, 0, ThemeColors.Tones.Length - 1);

        BuildAccentSwatches();
        RefreshAccentPreview();
    }

    /// <summary>
    /// One button per named hue, filled with that hue at the currently selected tone —
    /// so the row previews exactly what picking it would give you.
    /// </summary>
    private void BuildAccentSwatches()
    {
        AccentSwatchPanel.Children.Clear();

        foreach (var (name, hue) in ThemeColors.Palette)
        {
            bool selected = string.Equals(name, _settings.AccentColor, StringComparison.OrdinalIgnoreCase);

            var swatch = new Border
            {
                Width           = 26,
                Height          = 26,
                CornerRadius    = new CornerRadius(13),
                Margin          = new Thickness(0, 0, 6, 6),
                Background      = new SolidColorBrush(ThemeColors.Swatch(hue, _settings.AccentTone)),
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush     = new SolidColorBrush(selected
                                    ? Colors.White
                                    : Color.FromArgb(0x40, 0xff, 0xff, 0xff)),
                Cursor          = new Cursor(StandardCursorType.Hand),
                [ToolTip.TipProperty] = name
            };

            swatch.PointerPressed += (_, _) => SelectAccentColor(name);
            AccentSwatchPanel.Children.Add(swatch);
        }
    }

    private void SelectAccentColor(string name)
    {
        if (_settings.AccentColor == name) return;

        _settings.AccentColor = name;
        _settings.Save();
        ApplyAccentTheme();
        BuildAccentSwatches();   // move the selection ring
    }

    private void AccentTone_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettings) return;
        if (AccentToneBox.SelectedIndex < 0) return;
        if (AccentToneBox.SelectedIndex == _settings.AccentTone) return;

        _settings.AccentTone = AccentToneBox.SelectedIndex;
        _settings.Save();
        ApplyAccentTheme();
        BuildAccentSwatches();   // swatches preview the new tone
    }

    private void RefreshAccentPreview() =>
        AccentPreview.Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint   = new RelativePoint(1, 0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(ThemeColors.Start, 0),
                new GradientStop(ThemeColors.End,   1)
            }
        };

    private void ResetAccent_Click(object? sender, RoutedEventArgs e)
    {
        _settings.AccentColor = ThemeColors.DefaultColor;
        _settings.AccentTone  = ThemeColors.DefaultTone;
        _settings.Save();

        ApplyAccentTheme();

        // try/finally: _suppressSettings gates every handler in the Settings panel, so a throw
        // in RefreshAccentControls would leave it stuck true and silently deaden the whole panel
        // until restart — the same shape as the bug hardened in PopulateSettingsPanel.
        _suppressSettings = true;
        try { RefreshAccentControls(); }
        finally { _suppressSettings = false; }
    }

    /// <summary>
    /// Recolours everything. The XAML side follows automatically because all accent use
    /// is DynamicResource, but anything drawn in code holds its own brushes and has to be
    /// rebuilt: slider fills, visualizer bars, the status pill and the hint banner.
    /// </summary>
    private void ApplyAccentTheme()
    {
        ThemeColors.Apply(_settings);

        BannerHintBrush.Color = ThemeColors.Start;

        for (int i = 0; i < _sliders.Length; i++)
            UpdateSliderVisual(i);

        ApplyVizColorMode();
        SetEqState(_settings.EqEnabled, writeConfig: false);
        RefreshAccentPreview();
        _miniWindow?.RefreshUI();
    }

    // ── Preset visibility ────────────────────────────────────────────────────

    private void RefreshPresetVisibilityPanel()
    {
        PresetVisibilityPanel.Children.Clear();

        foreach (var preset in _presetManager.Presets)
        {
            string name = preset.Name;
            var check = new CheckBox
            {
                Content   = name,
                IsChecked = !_settings.HiddenPresets.Contains(name),
                Margin    = new Thickness(0, 0, 0, 2)
            };
            check.IsCheckedChanged += (_, _) => TogglePresetVisibility(name, check);
            PresetVisibilityPanel.Children.Add(check);
        }
    }

    private void TogglePresetVisibility(string name, CheckBox source)
    {
        if (_suppressSettings) return;

        bool wantVisible = source.IsChecked == true;

        // Hiding the last one would leave a chip row with nothing to click.
        if (!wantVisible && VisiblePresets().Count() <= 1)
        {
            _suppressSettings = true;
            source.IsChecked  = true;
            _suppressSettings = false;
            ShowHintBanner("At least one preset has to stay visible.");
            return;
        }

        if (wantVisible) _settings.HiddenPresets.Remove(name);
        else if (!_settings.HiddenPresets.Contains(name)) _settings.HiddenPresets.Add(name);

        // If the preset in use was just hidden, move to one that is still on screen
        // rather than leaving the row with nothing selected.
        if (!wantVisible && _settings.ActivePreset == name)
        {
            var fallback = VisiblePresets().FirstOrDefault();
            if (fallback != null) OnChipClick(fallback.Name);
        }

        _settings.Save();
        BuildPresetChips();
        SetActiveChip(string.IsNullOrEmpty(_settings.ActivePreset) ? "Custom" : _settings.ActivePreset);
    }

    // ── Customisable hotkeys ─────────────────────────────────────────────────

    private static readonly string[] PresetModifierChoices =
        { "Ctrl+Alt", "Ctrl+Shift", "Alt+Shift", "Ctrl+Alt+Shift", "Win+Alt" };

    /// <summary>Which hotkey the next keypress should be assigned to, if any.</summary>
    private string? _capturingHotkey;

    private void RefreshHotkeyControls()
    {
        HotkeyToggleButton.Content = _settings.HotkeyToggle;
        HotkeyCycleButton.Content  = _settings.HotkeyCycle;

        if (HotkeyPresetModsBox.ItemsSource is null)
            HotkeyPresetModsBox.ItemsSource = PresetModifierChoices;

        HotkeyPresetModsBox.SelectedItem =
            PresetModifierChoices.FirstOrDefault(
                m => string.Equals(m, _settings.HotkeyPresetModifiers, StringComparison.OrdinalIgnoreCase))
            ?? PresetModifierChoices[0];
    }

    private void CaptureHotkey_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string which) return;

        _capturingHotkey = which;
        button.Content   = "Press keys…";

        // A registered global hotkey is delivered to its owner as WM_HOTKEY and never reaches
        // the focused window as key input, so with our own hotkeys still registered the exact
        // combinations the user is most likely to press — the current bindings — could not be
        // captured at all; pressing Ctrl+Alt+E to rebind it just toggled the EQ instead.
        // Release them for the duration of the capture; every exit path re-registers.
        if (OperatingSystem.IsWindows() && _hwnd != IntPtr.Zero)
            HotkeyManager.Unregister(_hwnd);

        Focus();   // so the window, not the button, sees the keystrokes
    }

    /// <summary>Leaves capture mode and puts the global hotkeys back, whatever the exit route.</summary>
    private void CancelHotkeyCapture()
    {
        if (_capturingHotkey is null) return;

        _capturingHotkey = null;
        RefreshHotkeyControls();
        ReapplyHotkeys();
    }

    private void HotkeyPresetMods_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_suppressSettings) return;
        if (HotkeyPresetModsBox.SelectedItem is not string mods) return;
        if (mods == _settings.HotkeyPresetModifiers) return;

        _settings.HotkeyPresetModifiers = mods;
        _settings.Save();
        ReapplyHotkeys();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_capturingHotkey is null)
        {
            base.OnKeyDown(e);
            return;
        }

        if (e.Key == Key.Escape)
        {
            CancelHotkeyCapture();
            e.Handled = true;
            return;
        }

        // Null while only modifiers are held — keep listening for the real key.
        var captured = Hotkey.FromKeyEvent(e.KeyModifiers, e.Key);
        if (captured is null)
        {
            e.Handled = true;
            return;
        }

        switch (_capturingHotkey)
        {
            case "toggle": _settings.HotkeyToggle = captured.Value.ToString(); break;
            case "cycle":  _settings.HotkeyCycle  = captured.Value.ToString(); break;
        }

        _capturingHotkey = null;
        _settings.Save();
        RefreshHotkeyControls();
        ReapplyHotkeys();
        e.Handled = true;
    }

    private void ReapplyHotkeys()
    {
        if (!OperatingSystem.IsWindows() || _hwnd == IntPtr.Zero) return;

        HotkeyManager.Unregister(_hwnd);
        WarnAboutFailedHotkeys(HotkeyManager.Register(_hwnd, _settings));
    }

    /// <summary>
    /// RegisterHotKey fails when another application already owns a combination. That used
    /// to pass unnoticed, leaving the user with a hotkey that simply never fired.
    /// </summary>
    private void WarnAboutFailedHotkeys(List<string> failed)
    {
        if (failed.Count == 0)
        {
            if (_hotkeyWarningShown) { HideErrorBanner(); _hotkeyWarningShown = false; }
            return;
        }

        _hotkeyWarningShown = true;
        ShowHintBanner("Another application already uses " + string.Join("; ", failed) +
                       ". Pick a different combination under Settings → Hotkeys.");
    }

    private bool _hotkeyWarningShown;

    /// <summary>
    /// Ctrl+Alt+1..9 — jump straight to the nth preset chip, in the order they appear
    /// in the chip row. "Custom" is skipped: it is a state, not something to switch to.
    /// Out-of-range indices (fewer presets than hotkeys) are ignored.
    /// </summary>
    private void SelectPresetByIndex(int index)
    {
        var presetChips = _chips.Where(c => c.Name != "Custom").ToList();
        if (index < 0 || index >= presetChips.Count) return;

        OnChipClick(presetChips[index].Name);
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
        catch (Exception ex)
        {
            // This ticks every 2s in the background with no UI of its own, so a throw here used
            // to fail silently forever — per-game switching would just stop working with no
            // trace. Logged, not banner'd: a banner every 2 seconds would be its own bug.
            Logger.Log($"Auto-preset switching failed: {ex.Message}");
        }
    }

    // ── Live mode ────────────────────────────────────────────────────────────

    /// <summary>Restores LIVE/BEAT from settings. Safe to call every time OnOpened fires — unlike
    /// the click handlers this sets an absolute state rather than toggling, so a second OnOpened
    /// (a hide-to-tray/restore cycle re-fires it) cannot flip an already-restored mode back off.</summary>
    private void RestoreAudioVizMode()
    {
        if (_settings.VizBeatMode) SetBeatMode(true);
        else if (_settings.VizLiveMode) SetLiveMode(true);
    }

    /// <summary>Idempotent: setting the mode it is already in is a no-op, which is what makes
    /// <see cref="RestoreAudioVizMode"/> safe to call unconditionally on every OnOpened.</summary>
    private void SetLiveMode(bool enable)
    {
        if (enable == _liveMode) return;
        if (enable && _beatMode) SetBeatMode(false);

        _liveMode = enable;
        if (_liveMode && !StartAnalyzer()) { _liveMode = false; return; }

        LiveModeButton.Content = _liveMode ? "◉ LIVE" : "○ LIVE";
        LiveModeButton.Theme   = _liveMode ? PrimaryButtonTheme : null; // null = implicit theme
        if (!_liveMode) StopAnalyzerIfIdle();
    }

    private void SetBeatMode(bool enable)
    {
        if (enable == _beatMode) return;
        if (enable && _liveMode) SetLiveMode(false);

        _beatMode = enable;
        if (_beatMode && !StartAnalyzer()) { _beatMode = false; return; }

        BeatModeButton.Content = _beatMode ? "◉ BEAT" : "♪ BEAT";
        BeatModeButton.Theme   = _beatMode ? PrimaryButtonTheme : null;
        if (_beatMode) ResetBeatDetectorState();
        else StopAnalyzerIfIdle();
    }

    /// <summary>Starts the shared capture if it is not already running. False if it failed.</summary>
    private bool StartAnalyzer()
    {
        if (_spectrum != null) return true;
        try
        {
            var spectrum = new AudioSpectrumAnalyzer();
            spectrum.OnSpectrum = bars => Dispatcher.UIThread.InvokeAsync(() => OnSpectrumFrame(bars));
            spectrum.OnStopped  = ex => Dispatcher.UIThread.InvokeAsync(() => HandleAnalyzerStopped(ex));
            spectrum.Start();
            _spectrum = spectrum;
            return true;
        }
        catch (Exception ex)
        {
            // Logged as well as shown: the banner is dismissable and used to be the only trace.
            Logger.Log($"WASAPI loopback capture failed to start: {ex}");
            ShowErrorBanner("WASAPI audio capture failed. Is a playback device available?");
            return false;
        }
    }

    /// <summary>
    /// The capture stopped on its own — typically the default playback device changed (a
    /// wireless headset reconnecting is the common case on this hardware). One bounded run of
    /// silent recovery attempts against whatever the default device is now; give up and tell
    /// the user rather than leave LIVE/BEAT frozen and unexplained if it keeps failing.
    /// </summary>
    private void HandleAnalyzerStopped(Exception? ex)
    {
        if (_analyzerStoppingIntentionally) return;   // we stopped it ourselves; nothing to do
        if (!AudioDriven) return;                     // already off; this event is stale

        Logger.Log($"Audio capture stopped unexpectedly (device change?): {ex?.Message ?? "no exception"}");

        _spectrum?.Dispose();
        _spectrum = null;

        var now = DateTime.UtcNow;
        if ((now - _analyzerRecoveryWindowStart).TotalSeconds > 5)
        {
            _analyzerRecoveryWindowStart = now;
            _analyzerRecoveryAttempts    = 0;
        }
        _analyzerRecoveryAttempts++;

        if (_analyzerRecoveryAttempts <= 2 && StartAnalyzer())
            return;   // recovered silently against the current default device

        bool wasLive = _liveMode;
        _liveMode = false;
        _beatMode = false;
        _settings.VizLiveMode = false;
        _settings.VizBeatMode = false;
        _settings.Save();
        LiveModeButton.Content = "○ LIVE"; LiveModeButton.Theme = null;
        BeatModeButton.Content = "♪ BEAT"; BeatModeButton.Theme = null;
        SetVizTargets();
        ShowErrorBanner($"{(wasLive ? "LIVE" : "BEAT")} turned off: audio capture stopped and " +
                         "could not restart. Is a playback device available?");
    }

    private void StopAnalyzerIfIdle()
    {
        if (AudioDriven) return;

        // Dispose() synchronously fires RecordingStopped before returning, so the flag has to
        // be set first — HandleAnalyzerStopped uses it to tell "we did this" from "it died".
        _analyzerStoppingIntentionally = true;
        _spectrum?.Dispose();
        _spectrum = null;
        _analyzerStoppingIntentionally = false;

        Array.Clear(_beatPulse);
        SetVizTargets();
    }

    private void OnSpectrumFrame(double[] bars)
    {
        if (_liveMode)
        {
            for (int j = 0; j < VizBars; j++)
                _vizTarget[j] = bars[j];
            return;
        }

        if (_beatMode) DetectBeat(bars);
    }

    /// <summary>
    /// Flags a beat in a band when its energy jumps above its own recent average. Comparing
    /// against a rolling local average rather than a fixed level is what lets it follow a track
    /// through quiet and loud passages instead of firing constantly in one and never in the other.
    /// Run once per band so a kick, a snare and a hi-hat each surface independently.
    /// </summary>
    private void DetectBeat(double[] bars)
    {
        for (int band = 0; band < BeatBandCount; band++)
            DetectBandBeat(band, BandEnergy(bars, band));
    }

    private double BandEnergy(double[] bars, int band)
    {
        int lo = BeatBandBounds[band];
        int hi = Math.Min(BeatBandBounds[band + 1], bars.Length);
        if (hi <= lo) return 0;

        double energy = 0;
        for (int i = lo; i < hi; i++) energy += bars[i];
        return energy / (hi - lo);
    }

    private void DetectBandBeat(int band, double energy)
    {
        // Resolve whatever last frame flagged as a possible onset, using this frame's energy —
        // this is the one-frame hold described above the constants. A literal collapse to true
        // silence means last frame was a click, not a hit; anything else confirms it.
        if (_beatCandidatePending[band])
        {
            _beatCandidatePending[band] = false;
            if (energy > BeatClickRejectFloor &&
                (DateTime.UtcNow - _lastBeatAt[band]).TotalMilliseconds >= BeatRefractoryMs)
            {
                _lastBeatAt[band] = DateTime.UtcNow;
                _beatPulse[band]  = Math.Max(_beatPulse[band], _beatCandidateStrength[band]);
            }
        }

        double average = 0;
        if (_beatHistoryCount[band] > 0)
        {
            var history = _beatHistory[band];
            for (int i = 0; i < _beatHistoryCount[band]; i++) average += history[i];
            average /= _beatHistoryCount[band];
        }

        double previous = _beatPrevEnergy[band];
        _beatPrevEnergy[band] = energy;

        _beatHistory[band][_beatHistoryPos[band]] = energy;
        _beatHistoryPos[band] = (_beatHistoryPos[band] + 1) % BeatHistoryFrames;
        if (_beatHistoryCount[band] < BeatHistoryFrames) _beatHistoryCount[band]++;

        // Needs a full window before it can judge anything as "louder than usual".
        if (_beatHistoryCount[band] < BeatHistoryFrames) return;
        if (energy < BeatFloor || energy < average * BeatThreshold) return;

        // Must also be rising. Without this the rolling average self-oscillates on sustained
        // sound: a spike lifts the average, the ratio dips under the threshold, the spike ages
        // out, and it fires again — a steady tone produced a phantom beat roughly twice a
        // second in testing. A real onset is a jump from the frame before it.
        if (energy < previous * BeatRise) return;

        // Not fired yet — see the resolve step above and the comment by BeatClickRejectFloor.
        _beatCandidatePending[band]  = true;
        _beatCandidateStrength[band] = Math.Clamp((energy / Math.Max(average, 0.0001) - 1.0) / 1.5, 0.35, 1.0);
    }

    /// <summary>Which beat band a visualizer bar index falls into.</summary>
    private static int BandForBar(int barIndex)
    {
        for (int b = BeatBandCount - 1; b >= 0; b--)
            if (barIndex >= BeatBandBounds[b]) return b;
        return 0;
    }

    private void ResetBeatDetectorState()
    {
        for (int b = 0; b < BeatBandCount; b++)
        {
            Array.Clear(_beatHistory[b]);
            _beatHistoryCount[b] = 0;
            _beatHistoryPos[b]   = 0;
            _beatPrevEnergy[b]   = 0;
            _beatPulse[b]        = 0;
            _lastBeatAt[b]       = DateTime.MinValue;
            _beatCandidatePending[b]  = false;
            _beatCandidateStrength[b] = 0;
        }
    }
}
