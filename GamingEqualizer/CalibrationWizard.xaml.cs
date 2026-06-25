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

    // Phase -1 = reference level step, Phase 0 = left ear, Phase 1 = right ear
    private int _phase = -1;
    private int _step  = 0;

    private readonly float[] _leftThresholds  = new float[FreqCount];
    private readonly float[] _rightThresholds = new float[FreqCount];

    // Re-test state: when retesting a single band, these track which one
    private int _retestStep  = 0;   // 0-6 index into CalFrequencies
    private int _retestPhase = 0;   // 0=left, 1=right

    private WaveOutEvent?      _waveOut;
    private SignalGenerator?   _signalGen;

    // Results written by ShowResults()
    public float[]? ResultGainsLeft  { get; private set; }
    public float[]? ResultGainsRight { get; private set; }
    // Average — kept for slider display in MainWindow
    public float[]? ResultGains      { get; private set; }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DwmHelper.ApplyDarkTitlebar(this);
    }

    public CalibrationWizard(AppSettings settings)
    {
        InitializeComponent();
        ThresholdSlider.ValueChanged += ThresholdSlider_ValueChanged;
        UpdateStep();
    }

    private void UpdateStep()
    {
        if (_phase == -1)
        {
            ShowReferenceStep();
            return;
        }

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

        ThresholdSlider.Visibility     = Visibility.Visible;
        ThresholdValueLabel.Visibility = Visibility.Visible;
        LeftEarPill.Visibility         = Visibility.Visible;
        RightEarPill.Visibility        = Visibility.Visible;

        UpdateEarPills(isLeft);
        PlayTone(CalFrequencies[localStep], isLeft ? -1f : 1f);
    }

    private void ShowReferenceStep()
    {
        StepTitle.Text    = "Set Your Volume";
        StepSubtitle.Text = "Step 0 of 14 — Reference level";

        InstructionText.Text =
            "A reference tone is playing through both ears at a fixed level.\n\n" +
            "Adjust your system volume until the tone is clear and comfortable — " +
            "not too loud, not too quiet. Keep this volume for the entire calibration.\n\n" +
            "Click Next when you're ready.";

        FrequencyLabel.Text = "1000 Hz  ·  Reference";
        NextButton.Content  = "Next →";

        ThresholdSlider.Visibility     = Visibility.Collapsed;
        ThresholdValueLabel.Visibility = Visibility.Collapsed;
        LeftEarPill.Visibility         = Visibility.Collapsed;
        RightEarPill.Visibility        = Visibility.Collapsed;

        // Play 1kHz reference tone centered at a fixed moderate level
        PlayReferenceTone();
    }

    private void PlayReferenceTone()
    {
        StopTone();
        try
        {
            _signalGen = new SignalGenerator(44100, 1)
            {
                Gain      = Math.Pow(10, ReferenceDb / 20.0),
                Frequency = 1000,
                Type      = SignalGeneratorType.Sin
            };

            var panned = new PanningSampleProvider(_signalGen) { Pan = 0f };

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
        if (_phase == -1)
        {
            _phase = 0;
            UpdateStep();
            return;
        }

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
        // During a re-test, Cancel goes back to the results screen rather than closing
        if (ResultsPanel.Visibility == Visibility.Collapsed && ResultGains != null)
        {
            StopTone();
            NextButton.Click -= RetestDone_Click;
            NextButton.Click -= ApplyButton_Click;
            NextButton.Click += NextButton_Click;
            ShowResults();
            return;
        }

        StopTone();
        DialogResult = false;
        Close();
    }

    private void ShowResults()
    {
        StopTone();

        ResultGainsLeft  = ComputeGains(_leftThresholds);
        ResultGainsRight = ComputeGains(_rightThresholds);

        ResultGains = new float[10];
        for (int i = 0; i < 10; i++)
            ResultGains[i] = (ResultGainsLeft[i] + ResultGainsRight[i]) / 2f;

        StepTitle.Text    = "Calibration Complete";
        StepSubtitle.Text = "Review your results below. Re-test any band you want to redo.";

        LeftEarPill.Visibility         = Visibility.Collapsed;
        RightEarPill.Visibility        = Visibility.Collapsed;
        WizardPanel.Visibility         = Visibility.Collapsed;
        ResultsPanel.Visibility        = Visibility.Visible;

        BuildResultsGrid();

        NextButton.Content = "Apply";
        NextButton.Click  -= NextButton_Click;
        NextButton.Click  -= RetestDone_Click;
        NextButton.Click  += ApplyButton_Click;
    }

    private void BuildResultsGrid()
    {
        ResultsStack.Children.Clear();

        // Header row
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        header.Margin = new Thickness(0, 0, 0, 6);

        void AddHeaderCell(string text, int col)
        {
            var tb = new TextBlock
            {
                Text       = text,
                FontSize   = 10,
                FontWeight = FontWeights.Bold,
                Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush"),
                HorizontalAlignment = col == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Center
            };
            Grid.SetColumn(tb, col);
            header.Children.Add(tb);
        }
        AddHeaderCell("FREQ", 0);
        AddHeaderCell("LEFT", 1);
        AddHeaderCell("", 2);
        AddHeaderCell("RIGHT", 3);
        AddHeaderCell("", 4);
        ResultsStack.Children.Add(header);

        var accentBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        var textBrush   = (System.Windows.Media.Brush)FindResource("TextBrush");
        var dimBrush    = (System.Windows.Media.Brush)FindResource("TextDimBrush");

        for (int i = 0; i < FreqCount; i++)
        {
            int capturedI = i;

            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });

            // Frequency label
            var freqLabel = new TextBlock
            {
                Text       = $"{CalFrequencies[i]} Hz",
                FontSize   = 13,
                FontWeight = FontWeights.Bold,
                Foreground = accentBrush,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(freqLabel, 0);
            row.Children.Add(freqLabel);

            // Left threshold value
            var leftVal = new TextBlock
            {
                Text       = $"{_leftThresholds[i]:F0} dB",
                FontSize   = 12,
                Foreground = textBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            Grid.SetColumn(leftVal, 1);
            row.Children.Add(leftVal);

            // Left re-test button
            var retestL = new System.Windows.Controls.Button
            {
                Content             = "↻ L",
                FontSize            = 11,
                Padding             = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            retestL.Click += (_, _) => StartRetest(capturedI, isLeft: true);
            Grid.SetColumn(retestL, 2);
            row.Children.Add(retestL);

            // Right threshold value
            var rightVal = new TextBlock
            {
                Text       = $"{_rightThresholds[i]:F0} dB",
                FontSize   = 12,
                Foreground = textBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            Grid.SetColumn(rightVal, 3);
            row.Children.Add(rightVal);

            // Right re-test button
            var retestR = new System.Windows.Controls.Button
            {
                Content             = "↻ R",
                FontSize            = 11,
                Padding             = new Thickness(6, 3, 6, 3),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center
            };
            retestR.Click += (_, _) => StartRetest(capturedI, isLeft: false);
            Grid.SetColumn(retestR, 4);
            row.Children.Add(retestR);

            ResultsStack.Children.Add(row);
        }
    }

    private void StartRetest(int freqIndex, bool isLeft)
    {
        _retestStep  = freqIndex;
        _retestPhase = isLeft ? 0 : 1;

        ResultsPanel.Visibility = Visibility.Collapsed;
        WizardPanel.Visibility  = Visibility.Visible;
        LeftEarPill.Visibility  = Visibility.Visible;
        RightEarPill.Visibility = Visibility.Visible;

        StepTitle.Text    = "Re-test";
        StepSubtitle.Text = $"{(isLeft ? "Left" : "Right")} ear — {CalFrequencies[freqIndex]} Hz";
        InstructionText.Text = isLeft
            ? "A tone will play in your LEFT ear only.\nDrag the slider until you can just barely hear it, then click Done."
            : "A tone will play in your RIGHT ear only.\nDrag the slider until you can just barely hear it, then click Done.";

        FrequencyLabel.Text      = $"{CalFrequencies[freqIndex]} Hz";
        ThresholdSlider.Value    = isLeft ? _leftThresholds[freqIndex] : _rightThresholds[freqIndex];
        ThresholdValueLabel.Text = $"{ThresholdSlider.Value:F0} dB";

        ThresholdSlider.Visibility     = Visibility.Visible;
        ThresholdValueLabel.Visibility = Visibility.Visible;
        UpdateEarPills(isLeft);
        PlayTone(CalFrequencies[freqIndex], isLeft ? -1f : 1f);

        NextButton.Content = "Done";
        NextButton.Click  -= NextButton_Click;
        NextButton.Click  -= ApplyButton_Click;
        NextButton.Click  += RetestDone_Click;
    }

    private void RetestDone_Click(object sender, RoutedEventArgs e)
    {
        float val = (float)ThresholdSlider.Value;
        if (_retestPhase == 0) _leftThresholds[_retestStep]  = val;
        else                   _rightThresholds[_retestStep] = val;

        ShowResults();
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
