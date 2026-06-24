using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GamingEqualizer.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GamingEqualizer;

public partial class CalibrationWizard : Window
{
    private static readonly int[] CalFrequencies  = { 125, 250, 500, 1000, 2000, 4000, 8000 };
    private static readonly int[] CalBandIndices  = { 2, 3, 4, 5, 6, 7, 8 };
    private const int FreqCount   = 7;
    private const float ReferenceDb = -20f;

    // Phase 0 = left ear, Phase 1 = right ear
    private int _phase = 0;
    private int _step  = 0;

    private readonly float[] _leftThresholds  = new float[FreqCount];
    private readonly float[] _rightThresholds = new float[FreqCount];

    private WaveOutEvent?      _waveOut;
    private SignalGenerator?   _signalGen;

    // Results written by ShowResults()
    public float[]? ResultGainsLeft  { get; private set; }
    public float[]? ResultGainsRight { get; private set; }
    // Average — kept for slider display in MainWindow
    public float[]? ResultGains      { get; private set; }

    public CalibrationWizard(AppSettings settings)
    {
        InitializeComponent();
        ThresholdSlider.ValueChanged += ThresholdSlider_ValueChanged;
        UpdateStep();
    }

    private void UpdateStep()
    {
        bool finished = _phase >= 2;
        if (finished) { ShowResults(); return; }

        bool isLeft  = _phase == 0;
        int  localStep = _step;          // 0-6 within current phase
        int  totalStep = _phase * FreqCount + localStep + 1;  // 1-14
        int  totalSteps = FreqCount * 2;

        StepTitle.Text    = "Calibration Wizard";
        StepSubtitle.Text = $"{(isLeft ? "Left" : "Right")} ear — Step {totalStep} of {totalSteps}";

        InstructionText.Text = isLeft
            ? "A tone will play in your LEFT ear only.\nDrag the slider until you can just barely hear it, then click Next."
            : "A tone will play in your RIGHT ear only.\nDrag the slider until you can just barely hear it, then click Next.";

        FrequencyLabel.Text    = $"{CalFrequencies[localStep]} Hz";
        ThresholdSlider.Value  = -30;
        ThresholdValueLabel.Text = "-30 dB";

        bool isLastOverall = (_phase == 1 && localStep == FreqCount - 1);
        NextButton.Content = isLastOverall ? "Finish" : "Next →";

        UpdateEarPills(isLeft);
        PlayTone(CalFrequencies[localStep], isLeft ? -1f : 1f);
    }

    private void UpdateEarPills(bool leftActive)
    {
        var activeBg = new LinearGradientBrush(
            System.Windows.Media.Color.FromRgb(0x7c, 0x3a, 0xed),
            System.Windows.Media.Color.FromRgb(0xa8, 0x55, 0xf7),
            new System.Windows.Point(0, 0.5), new System.Windows.Point(1, 0.5));

        if (leftActive)
        {
            LeftEarPill.Background    = activeBg;
            LeftEarPill.BorderBrush   = null;
            LeftEarPill.BorderThickness = new Thickness(0);
            LeftEarLabel.Foreground   = System.Windows.Media.Brushes.White;

            RightEarPill.Background   = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1a, 0x1a, 0x2e));
            RightEarPill.BorderBrush  = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x38));
            RightEarPill.BorderThickness = new Thickness(1);
            RightEarLabel.Foreground  = (SolidColorBrush)FindResource("TextDimBrush");
        }
        else
        {
            RightEarPill.Background   = activeBg;
            RightEarPill.BorderBrush  = null;
            RightEarPill.BorderThickness = new Thickness(0);
            RightEarLabel.Foreground  = System.Windows.Media.Brushes.White;

            LeftEarPill.Background    = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1a, 0x1a, 0x2e));
            LeftEarPill.BorderBrush   = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x25, 0x38));
            LeftEarPill.BorderThickness = new Thickness(1);
            LeftEarLabel.Foreground   = (SolidColorBrush)FindResource("TextDimBrush");
        }
    }

    private void PlayTone(int frequency, float pan)
    {
        StopTone();
        try
        {
            // Mono signal generator + panning
            _signalGen = new SignalGenerator(44100, 1)
            {
                Gain      = 0.1,
                Frequency = frequency,
                Type      = SignalGeneratorType.Sin
            };

            var panned = new PanningSampleProvider(_signalGen) { Pan = pan };

            _waveOut = new WaveOutEvent { DesiredLatency = 50 };
            _waveOut.Init(panned);
            _waveOut.Play();
            ErrorBanner.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StopTone();
            ErrorText.Text = $"Audio device error: {ex.Message}. Calibration cancelled.";
            ErrorBanner.Visibility = Visibility.Visible;
            NextButton.IsEnabled   = false;
        }
    }

    private void StopTone()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut    = null;
        _signalGen  = null;
    }

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ThresholdValueLabel.Text = $"{ThresholdSlider.Value:F0} dB";
        if (_signalGen != null)
            _signalGen.Gain = Math.Pow(10, ThresholdSlider.Value / 20.0);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        float val = (float)ThresholdSlider.Value;
        if (_phase == 0) _leftThresholds[_step]  = val;
        else             _rightThresholds[_step] = val;

        _step++;
        if (_step >= FreqCount)
        {
            _step = 0;
            _phase++;
        }
        UpdateStep();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        StopTone();
        DialogResult = false;
        Close();
    }

    private void ShowResults()
    {
        StopTone();

        ResultGainsLeft  = ComputeGains(_leftThresholds);
        ResultGainsRight = ComputeGains(_rightThresholds);

        // Average of L+R for slider display
        ResultGains = new float[10];
        for (int i = 0; i < 10; i++)
            ResultGains[i] = (ResultGainsLeft[i] + ResultGainsRight[i]) / 2f;

        StepTitle.Text       = "Calibration Complete";
        StepSubtitle.Text    = "Your personal per-ear EQ curve has been generated.";
        FrequencyLabel.Text  = "";
        InstructionText.Text = "Click Apply to use this curve, or Cancel to discard.";
        LeftEarPill.Visibility  = Visibility.Collapsed;
        RightEarPill.Visibility = Visibility.Collapsed;
        ThresholdSlider.Visibility      = Visibility.Collapsed;
        ThresholdValueLabel.Visibility  = Visibility.Collapsed;
        NextButton.Content = "Apply";
        NextButton.Click  -= NextButton_Click;
        NextButton.Click  += ApplyButton_Click;
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private static float[] ComputeGains(float[] thresholds)
    {
        float[] calGains = new float[FreqCount];
        for (int i = 0; i < FreqCount; i++)
            calGains[i] = Math.Clamp(-(thresholds[i] - ReferenceDb), -12f, 12f);

        float max = calGains.Max();
        if (max > 0)
            for (int i = 0; i < calGains.Length; i++)
                calGains[i] -= max;

        float[] result = new float[10];
        for (int i = 0; i < CalBandIndices.Length; i++)
            result[CalBandIndices[i]] = calGains[i];
        return result;
    }

    protected override void OnClosed(EventArgs e)
    {
        StopTone();
        base.OnClosed(e);
    }
}
