namespace GamingEqualizer;

public partial class OnboardingWizard : Window
{
    public bool ShouldRunCalibration { get; private set; }

    private int _step = 1;
    private const int TotalSteps = 4;

    private Control[] _pages = null!;
    private Ellipse[] _dots  = null!;

    public OnboardingWizard()
    {
        InitializeComponent();
        _pages = new Control[] { Page1, Page2, Page3, Page4 };
        _dots  = new Ellipse[] { Dot1, Dot2, Dot3, Dot4 };
        UpdateView();
    }

    private void Next_Click(object? sender, RoutedEventArgs e)
    {
        if (_step < TotalSteps)
        {
            _step++;
            UpdateView();
        }
        else
        {
            ShouldRunCalibration = RunCalibrationCheck.IsChecked == true;
            Close(true);
        }
    }

    private void Back_Click(object? sender, RoutedEventArgs e)
    {
        if (_step > 1)
        {
            _step--;
            UpdateView();
        }
    }

    private void UpdateView()
    {
        var activeColor   = Color.FromRgb(0x7c, 0x3a, 0xed);
        var inactiveColor = Color.FromRgb(0x25, 0x25, 0x38);

        for (int i = 0; i < TotalSteps; i++)
        {
            _pages[i].IsVisible = (i + 1 == _step);
            _dots[i].Fill       = new SolidColorBrush(i + 1 == _step ? activeColor : inactiveColor);
        }

        BackBtn.IsVisible = _step > 1;
        NextBtn.Content   = _step == TotalSteps ? "Get Started" : "Next";
    }
}
