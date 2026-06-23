using System.Windows;
using System.Windows.Controls;
using GamingEqualizer.Models;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace GamingEqualizer;

public partial class CalibrationWizard : Window
{
    // The 7 calibration frequencies (indices into the 10-band array)
    private static readonly int[] CalFrequencies = { 125, 250, 500, 1000, 2000, 4000, 8000 };
    private static readonly int[] CalBandIndices = { 2, 3, 4, 5, 6, 7, 8 }; // mapping to 10-band slots

    private static readonly float ReferenceDb = -20f;

    private int _step = 0;
    private readonly float[] _thresholds = new float[7];

    private WaveOutEvent? _waveOut;
    private SignalGenerator? _signalGen;

    public float[]? ResultGains { get; private set; }

    public CalibrationWizard(AppSettings settings)
    {
        InitializeComponent();
        ThresholdSlider.ValueChanged += ThresholdSlider_ValueChanged;
        UpdateStep();
    }

    private void UpdateStep()
    {
        bool isFinal = _step >= CalFrequencies.Length;

        if (isFinal)
        {
            ShowResults();
            return;
        }

        StepTitle.Text = "Calibration Wizard";
        StepSubtitle.Text = $"Step {_step + 1} of {CalFrequencies.Length}";
        FrequencyLabel.Text = $"{CalFrequencies[_step]} Hz";
        ThresholdSlider.Value = -30;
        ThresholdValueLabel.Text = "-30 dB";
        NextButton.Content = _step == CalFrequencies.Length - 1 ? "Finish" : "Next →";

        PlayTone(CalFrequencies[_step]);
    }

    private void PlayTone(int frequency)
    {
        StopTone();
        try
        {
            _signalGen = new SignalGenerator
            {
                Gain = 0.1,
                Frequency = frequency,
                Type = SignalGeneratorType.Sin
            };

            _waveOut = new WaveOutEvent { DesiredLatency = 50 };
            _waveOut.Init(_signalGen);
            _waveOut.Play();
            ErrorBanner.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StopTone();
            ErrorText.Text = $"Audio device error: {ex.Message}. Calibration cancelled.";
            ErrorBanner.Visibility = Visibility.Visible;
            NextButton.IsEnabled = false;
        }
    }

    private void StopTone()
    {
        _waveOut?.Stop();
        _waveOut?.Dispose();
        _waveOut = null;
        _signalGen = null;
    }

    private void ThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ThresholdValueLabel.Text = $"{ThresholdSlider.Value:F0} dB";

        // Adjust tone gain to match slider position
        if (_signalGen != null)
            _signalGen.Gain = Math.Pow(10, ThresholdSlider.Value / 20.0);
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        _thresholds[_step] = (float)ThresholdSlider.Value;
        _step++;
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

        var gains10Band = ComputeGains();
        ResultGains = gains10Band;

        StepTitle.Text = "Calibration Complete";
        StepSubtitle.Text = "Your personal EQ curve has been generated.";
        FrequencyLabel.Text = "";
        InstructionText.Text = "Click Apply to use this curve, or Cancel to discard.";
        ThresholdSlider.Visibility = Visibility.Collapsed;
        ThresholdValueLabel.Visibility = Visibility.Collapsed;
        NextButton.Content = "Apply";
        NextButton.Click -= NextButton_Click;
        NextButton.Click += ApplyButton_Click;
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private float[] ComputeGains()
    {
        // gain = -(threshold - reference), clamped to ±12 dB
        float[] calGains = new float[7];
        for (int i = 0; i < 7; i++)
            calGains[i] = Math.Clamp(-(_thresholds[i] - ReferenceDb), -12f, 12f);

        // Normalize so loudest band = 0 dB
        float max = calGains.Max();
        if (max > 0)
            for (int i = 0; i < calGains.Length; i++)
                calGains[i] -= max;

        // Map into the 10-band array; bands not measured stay at 0
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
