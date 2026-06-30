using Avalonia.Controls.Primitives;

namespace GamingEqualizer;

public partial class MiniWindow : Window
{
    private readonly AppSettings    _settings;
    private readonly PresetManager  _presetManager;
    private readonly Action         _onToggle;
    private readonly Action         _onExpand;
    private readonly Action<string> _onPresetClick;

    private readonly List<(string Name, ToggleButton Chip)> _chips = new();
    private DispatcherTimer? _pulseTimer;
    private bool _pulseHigh = true;
    private bool _suppressChip = false;

    private ControlTheme ChipTheme         => (ControlTheme)this.FindResource("ChipTheme")!;
    private ControlTheme PrimaryButtonTheme => (ControlTheme)this.FindResource("PrimaryButtonTheme")!;
    private ControlTheme DangerButtonTheme  => (ControlTheme)this.FindResource("DangerButtonTheme")!;

    public MiniWindow(
        AppSettings    settings,
        PresetManager  presetManager,
        Action         onToggle,
        Action         onExpand,
        Action<string> onPresetClick)
    {
        InitializeComponent();
        _settings      = settings;
        _presetManager = presetManager;
        _onToggle      = onToggle;
        _onExpand      = onExpand;
        _onPresetClick = onPresetClick;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        BuildChips();
        RefreshUI();
        StartPulse();
    }

    private void BuildChips()
    {
        foreach (var preset in _presetManager.Presets)
        {
            string name = preset.Name;
            var chip = new ToggleButton
            {
                Content = name,
                Theme   = ChipTheme,
                Padding = new Thickness(10, 3, 10, 3),
                FontSize = 11,
                Margin  = new Thickness(0, 0, 3, 0)
            };
            chip.Click += (_, _) =>
            {
                if (_suppressChip) return;
                _onPresetClick(name);
                RefreshUI();
            };
            _chips.Add((name, chip));
            MiniChipPanel.Children.Add(chip);
        }
    }

    public void RefreshUI()
    {
        bool on = _settings.EqEnabled;

        if (on)
        {
            MiniToggleButton.Content   = "■ OFF";
            MiniToggleButton.Theme     = DangerButtonTheme;
            MiniStatusLabel.Text       = "EQ ACTIVE";
            MiniStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(167, 139, 250));
            MiniStatusDot.Fill         = new SolidColorBrush(Color.FromRgb(124, 58, 237));
            MiniStatusPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0x7c, 0x3a, 0xed));
            _pulseTimer?.Start();
        }
        else
        {
            MiniToggleButton.Content   = "▶ ON";
            MiniToggleButton.Theme     = PrimaryButtonTheme;
            MiniStatusLabel.Text       = "EQ OFF";
            MiniStatusLabel.Foreground = new SolidColorBrush(Color.FromRgb(68, 68, 90));
            MiniStatusDot.Fill         = new SolidColorBrush(Color.FromRgb(42, 42, 68));
            MiniStatusDot.Opacity      = 1;
            MiniStatusPill.BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0x44, 0x44, 0x5a));
            _pulseTimer?.Stop();
        }

        _suppressChip = true;
        foreach (var (name, chip) in _chips)
            chip.IsChecked = name == _settings.ActivePreset;
        _suppressChip = false;
    }

    private void StartPulse()
    {
        _pulseTimer       = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _pulseTimer.Tick += (_, _) =>
        {
            _pulseHigh            = !_pulseHigh;
            MiniStatusDot.Opacity = _pulseHigh ? 1.0 : 0.35;
        };
        if (_settings.EqEnabled) _pulseTimer.Start();
    }

    public void AddChip(string name)
    {
        if (_chips.Any(c => c.Name == name)) return;
        var chip = new ToggleButton
        {
            Content  = name,
            Theme    = ChipTheme,
            Padding  = new Thickness(10, 3, 10, 3),
            FontSize = 11,
            Margin   = new Thickness(0, 0, 3, 0)
        };
        chip.Click += (_, _) =>
        {
            if (_suppressChip) return;
            _onPresetClick(name);
            RefreshUI();
        };
        _chips.Add((name, chip));
        MiniChipPanel.Children.Add(chip);
    }

    private void MiniToggle_Click(object? sender, RoutedEventArgs e)
    {
        _onToggle();
        RefreshUI();
    }

    private void Expand_Click(object? sender, RoutedEventArgs e) => _onExpand();

    private void Window_PointerPressed(object? sender, PointerPressedEventArgs e) => BeginMoveDrag(e);
}
