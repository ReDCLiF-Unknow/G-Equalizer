using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;

namespace GamingEqualizer;

public partial class OnboardingWizard : Window
{
    public bool ShouldRunCalibration { get; private set; }

    private int _step = 1;
    private const int TotalSteps = 4;

    private UIElement[] _pages = null!;
    private Ellipse[]   _dots  = null!;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DwmHelper.ApplyDarkTitlebar(this);
    }

    public OnboardingWizard()
    {
        InitializeComponent();
        _pages = new UIElement[] { Page1, Page2, Page3, Page4 };
        _dots  = new Ellipse[]   { Dot1, Dot2, Dot3, Dot4 };
        UpdateView();
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step < TotalSteps)
        {
            _step++;
            UpdateView();
        }
        else
        {
            ShouldRunCalibration = RunCalibrationCheck.IsChecked == true;
            DialogResult = true;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (_step > 1)
        {
            _step--;
            UpdateView();
        }
    }

    private void UpdateView()
    {
        var active   = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7c, 0x3a, 0xed));
        var inactive = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x38));

        for (int i = 0; i < TotalSteps; i++)
        {
            _pages[i].Visibility = (i + 1 == _step) ? Visibility.Visible : Visibility.Collapsed;
            _dots[i].Fill = (i + 1 == _step) ? active : inactive;
        }

        BackBtn.Visibility = _step > 1 ? Visibility.Visible : Visibility.Hidden;
        NextBtn.Content    = _step == TotalSteps ? "Get Started" : "Next";
    }
}
