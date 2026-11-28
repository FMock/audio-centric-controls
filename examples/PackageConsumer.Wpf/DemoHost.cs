using System.ComponentModel;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AudioCentric.Controls.Input;
using AudioCentric.Controls.Measurements;
using AudioCentric.Controls.Plotting;

namespace PackageConsumer;

/// <summary>
/// A deterministic mock host. Every value is a function of elapsed time, so the window looks the same on every run and
/// no audio device is needed. It shows the integration contract the controls expect:
/// <list type="bullet">
/// <item>measurements in explicit units (VU, dBFS, correlation), with −∞ for silence and NaN for "unavailable";</item>
/// <item>parameter targets that the controls write back (knob, fader);</item>
/// <item>a switch whose actual state is acknowledged by the host after a delay (requested vs actual);</item>
/// <item>EQ band change requests that the host constrains before it sends back bands and response;</item>
/// <item>host commands (mono check).</item>
/// </list>
/// A real host computes the same values from audio on its own thread and hands the UI the newest ones at frame rate.
/// </summary>
public sealed class DemoHost : INotifyPropertyChanged
{
    private const int SampleRate = 48000, FftSize = 4096;
    private static readonly double[] BinFrequencies = Enumerable.Range(0, FftSize / 2 + 1).Select(k => (double)k * SampleRate / FftSize).ToArray();
    private static readonly double[] ResponseGrid = ResponseMath.LogSpacedFrequencies(20, 20000, 400);

    private double _time;
    private double _powerAckDue = double.NaN;

    public DemoHost()
    {
        _bands = new[]
        {
            new EqBand(EqBandType.LowShelf, 100, 3, 0.7),
            new EqBand(EqBandType.Peaking, 1000, -2, 1.0),
            new EqBand(EqBandType.HighShelf, 8000, 2, 0.7),
        };
        _response = ComputeResponse(_bands);
        BandChangeCommand = new Command(p => { if (p is EqBandChangeRequest r) ApplyBandRequest(r); });
        MonoCheckCommand = new Command(_ => IsMono = !IsMono);
        Advance(0);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // ------------------------------------------------------------------ parameters written by the controls

    private double _trimDb;
    /// <summary>Bound two-way to a RotaryKnob.</summary>
    public double TrimDb { get => _trimDb; set => Set(ref _trimDb, value); }

    private double _faderDb;
    /// <summary>Bound two-way to a ConsoleFader (−∞ at the bottom).</summary>
    public double FaderDb { get => _faderDb; set => Set(ref _faderDb, value); }

    private bool _powerRequested = true;
    /// <summary>The switch's request (IlluminatedToggle.IsChecked). The engine applies it 300 ms later.</summary>
    public bool PowerRequested
    {
        get => _powerRequested;
        set
        {
            if (!Set(ref _powerRequested, value)) return;
            _powerAckDue = _time + 0.3;
            PowerStatus = ToggleStatus.Pending;
        }
    }

    private bool _powerActual = true;
    /// <summary>What the engine actually did (IlluminatedToggle.ActualState): the lamp follows this, not the click.</summary>
    public bool PowerActual { get => _powerActual; private set => Set(ref _powerActual, value); }

    private ToggleStatus _powerStatus = ToggleStatus.Ready;
    public ToggleStatus PowerStatus { get => _powerStatus; private set => Set(ref _powerStatus, value); }

    private bool _isMono;
    public bool IsMono { get => _isMono; private set => Set(ref _isMono, value); }

    public ICommand MonoCheckCommand { get; }

    // ------------------------------------------------------------------ measurements read by the controls

    private double _vu = double.NegativeInfinity, _peakL = double.NegativeInfinity, _peakR = double.NegativeInfinity, _correlation = double.NaN;
    private bool _overload;
    private SpectrumSnapshot? _spectrum;

    public double VuLeft { get => _vu; private set => Set(ref _vu, value); }
    public double PeakLeft { get => _peakL; private set => Set(ref _peakL, value); }
    public double PeakRight { get => _peakR; private set => Set(ref _peakR, value); }
    public bool Overload { get => _overload; private set => Set(ref _overload, value); }

    /// <summary>NaN when either channel is silent: the meter then shows NO SIGNAL instead of a misleading 0.</summary>
    public double Correlation { get => _correlation; private set => Set(ref _correlation, value); }

    public SpectrumSnapshot? Spectrum { get => _spectrum; private set => Set(ref _spectrum, value); }

    // ------------------------------------------------------------------ equalizer (request → constrain → acknowledge)

    private IReadOnlyList<EqBand> _bands;
    private ResponseSnapshot _response;

    public IReadOnlyList<EqBand> Bands { get => _bands; private set => Set(ref _bands, value); }
    public ResponseSnapshot Response { get => _response; private set => Set(ref _response, value); }
    public ICommand BandChangeCommand { get; }

    private void ApplyBandRequest(EqBandChangeRequest request)
    {
        var current = _bands[request.Index];
        var r = request.Requested;
        var applied = current with
        {
            FrequencyHz = Math.Clamp(r.FrequencyHz, 20, 20000),
            GainDb = Math.Round(Math.Clamp(r.GainDb, -12, 12), 1), // this host allows ±12 dB, whatever was requested
            Q = Math.Clamp(r.Q, 0.3, 8),
            IsEnabled = r.IsEnabled,
        };
        var bands = _bands.ToArray();
        bands[request.Index] = applied;
        Bands = bands;
        Response = ComputeResponse(bands);
    }

    /// <summary>Analog-prototype magnitude and phase of the band cascade, sampled on a log grid.</summary>
    private static ResponseSnapshot ComputeResponse(IReadOnlyList<EqBand> bands)
    {
        var gain = new double[ResponseGrid.Length];
        var phase = new double[ResponseGrid.Length];
        for (int i = 0; i < ResponseGrid.Length; i++)
        {
            Complex h = Complex.One;
            foreach (var b in bands) if (b.IsEnabled) h *= Section(b, ResponseGrid[i]);
            gain[i] = 20 * Math.Log10(h.Magnitude);
            phase[i] = h.Phase * 180 / Math.PI;
        }
        return new ResponseSnapshot(ResponseGrid, gain, phase) { SampleRate = SampleRate };
    }

    private static Complex Section(EqBand b, double hz)
    {
        var s = new Complex(0, hz / b.FrequencyHz);
        double a = Math.Pow(10, b.GainDb / 40), q = b.Q, k = Math.Sqrt(a) / q;
        return b.Type switch
        {
            EqBandType.LowShelf => a * (s * s + k * s + a) / (a * s * s + k * s + 1),
            EqBandType.HighShelf => a * (a * s * s + k * s + 1) / (s * s + k * s + a),
            EqBandType.HighPass => s * s / (s * s + s / q + 1),
            EqBandType.LowPass => 1 / (s * s + s / q + 1),
            _ => (s * s + s * a / q + 1) / (s * s + s / (a * q) + 1),
        };
    }

    // ------------------------------------------------------------------ the "engine"

    /// <summary>Advances the mock engine. Call once per rendered frame on the UI thread.</summary>
    public void Advance(double dt)
    {
        _time += dt;
        if (!double.IsNaN(_powerAckDue) && _time >= _powerAckDue)
        {
            _powerAckDue = double.NaN;
            PowerActual = PowerRequested;
            PowerStatus = ToggleStatus.Ready;
        }

        double gain = TrimDb + FaderDb;
        if (!PowerActual || double.IsNegativeInfinity(gain))
        {
            // Silence: −∞, never a large negative number, and correlation becomes unavailable.
            VuLeft = PeakLeft = PeakRight = double.NegativeInfinity;
            Overload = false;
            Correlation = double.NaN;
            Spectrum = BuildSpectrum(double.NegativeInfinity);
            return;
        }

        // A sine-like program level that swells between about −26 and −6 dBFS peak, with a hot hit every 7 s.
        double peak = -16 + 10 * Math.Sin(2 * Math.PI * _time / 5.0) + gain;
        if (_time % 7.0 < 0.12) peak += 14;
        PeakLeft = peak;
        PeakRight = peak - 1.5;
        VuLeft = peak - 3.01 + 18; // sine RMS is 3 dB below peak; −18 dBFS RMS = 0 VU
        Overload = peak > 0;
        Correlation = 0.75 + 0.2 * Math.Sin(2 * Math.PI * _time / 9.0);
        Spectrum = BuildSpectrum(peak);
    }

    /// <summary>A peak-calibrated spectrum of three harmonic tones over a pink floor, shaped by the EQ.</summary>
    private SpectrumSnapshot BuildSpectrum(double levelDbfs)
    {
        var levels = new double[BinFrequencies.Length];
        for (int k = 0; k < levels.Length; k++)
        {
            if (double.IsNegativeInfinity(levelDbfs)) { levels[k] = double.NegativeInfinity; continue; }
            double f = Math.Max(BinFrequencies[k], 1);
            double floor = levelDbfs - 50 - 10 * Math.Log10(f / 100) + 3 * Math.Sin(k * 1.7);
            levels[k] = floor + ResponseMath.Interpolate(Response.Frequencies, Response.GainDb, Math.Clamp(f, 20, 20000));
        }
        foreach (var (hz, rel) in new[] { (220.0, 0.0), (440.0, -9.0), (660.0, -15.0) })
        {
            int k = (int)Math.Round(hz * FftSize / SampleRate);
            double l = levelDbfs + rel + ResponseMath.Interpolate(Response.Frequencies, Response.GainDb, hz);
            levels[k] = Math.Max(levels[k], l);
            levels[k - 1] = Math.Max(levels[k - 1], l - 6);
            levels[k + 1] = Math.Max(levels[k + 1], l - 6);
        }
        return new SpectrumSnapshot(SampleRate, 0, BinFrequencies, levels)
        {
            FftSize = FftSize,
            Window = "Hann",
            NoiseBandwidthBins = 1.5,
        };
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    private sealed class Command : ICommand
    {
        private readonly Action<object?> _execute;
        public Command(Action<object?> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
    }
}
