using NAudio.Dsp;
using NAudio.Wave;

namespace GamingEqualizer;

public sealed class AudioSpectrumAnalyzer : IDisposable
{
    public const int BarCount = 80;

    private const int    FftSize  = 4096;
    private const double MinFreq  = 20.0;
    private const double MaxFreq  = 20000.0;
    private const double FloorDb  = -80.0;

    private WasapiLoopbackCapture? _capture;
    private readonly float[]        _fftBuffer = new float[FftSize];
    private int                     _bufferPos;
    private readonly object         _lock      = new();

    public Action<double[]>? OnSpectrum { get; set; }

    public void Start()
    {
        _capture              = new WasapiLoopbackCapture();
        _capture.DataAvailable += OnData;
        _capture.StartRecording();
    }

    public void Stop()
    {
        _capture?.StopRecording();
        _capture?.Dispose();
        _capture = null;
    }

    private void OnData(object? sender, WaveInEventArgs e)
    {
        if (_capture == null) return;
        int channels       = _capture.WaveFormat.Channels;
        int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
        int stride         = bytesPerSample * channels;

        for (int i = 0; i + stride <= e.BytesRecorded; i += stride)
        {
            float sample = 0f;
            for (int ch = 0; ch < channels; ch++)
                sample += BitConverter.ToSingle(e.Buffer, i + ch * bytesPerSample);
            sample /= channels;

            lock (_lock)
            {
                _fftBuffer[_bufferPos++] = sample;
                if (_bufferPos >= FftSize)
                {
                    ProcessFft(_capture.WaveFormat.SampleRate);
                    _bufferPos = 0;
                }
            }
        }
    }

    private void ProcessFft(int sampleRate)
    {
        var complex = new Complex[FftSize];
        for (int i = 0; i < FftSize; i++)
        {
            double window  = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (FftSize - 1)));
            complex[i].X   = (float)(_fftBuffer[i] * window);
            complex[i].Y   = 0f;
        }

        FastFourierTransform.FFT(true, (int)Math.Log2(FftSize), complex);

        var bars = new double[BarCount];
        for (int j = 0; j < BarCount; j++)
        {
            double t      = j / (double)(BarCount - 1);
            double freqLo = MinFreq * Math.Pow(MaxFreq / MinFreq, t);
            double freqHi = MinFreq * Math.Pow(MaxFreq / MinFreq, (j + 1.0) / BarCount);

            int binLo = Math.Max(1,           (int)(freqLo * FftSize / sampleRate));
            int binHi = Math.Min(FftSize / 2 - 1, (int)(freqHi * FftSize / sampleRate));
            if (binHi < binLo) binHi = binLo;

            double peak = 0;
            for (int b = binLo; b <= binHi; b++)
            {
                double mag = Math.Sqrt(complex[b].X * complex[b].X + complex[b].Y * complex[b].Y);
                if (mag > peak) peak = mag;
            }

            double dB  = peak > 1e-10 ? 20 * Math.Log10(peak * 2.0 / FftSize) : FloorDb;
            dB         = Math.Max(FloorDb, dB);
            bars[j]    = Math.Max(0, (dB - FloorDb) / (-FloorDb) * 12.0);
        }

        OnSpectrum?.Invoke(bars);
    }

    public void Dispose() => Stop();
}
