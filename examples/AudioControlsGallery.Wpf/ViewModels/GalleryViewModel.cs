using System.Globalization;
using System.Windows.Threading;
using System.Windows.Media;
using AudioCentric.Controls.Dynamics;
using AudioCentric.Controls.Input;
using AudioCentric.Controls.Measurements;
using AudioCentric.Controls.Plotting;
using AudioCentric.Controls.Wpf.Controls;
using AudioControlsGallery.Simulation;

namespace AudioControlsGallery.ViewModels;

/// <summary>
/// Shared state for every gallery page. At frame rate it pulls the newest measurements from the simulated
/// engine (the "host") and exposes them for binding. The gain controls publish target values back to the engine.
/// </summary>
public sealed class GalleryViewModel : ObservableObject, IDisposable
{
    private readonly SimulatedSignalEngine _engine = new();
    private TimeSpan _lastFrame = TimeSpan.MinValue;
    private double _readoutTimer;
    private double _readoutPeakL = double.NegativeInfinity, _readoutPeakR = double.NegativeInfinity;

    public GalleryViewModel()
    {
        _eq = new SimulatedFrequencyResponseSource(SimulatedSignalEngine.SampleRate);
        _engine.SetEqualizer(_eq.Coefficients);
        _eqBands = _eq.Bands;
        RefreshResponse(); // theoretical now, measured shortly after on a worker thread
        ApplyGain();
        CompositionTarget.Rendering += OnFrame;
    }

    // ------------------------------------------------------------------ transport / gain

    private bool _isPowered = true;
    public bool IsPowered
    {
        get => _isPowered;
        set { if (Set(ref _isPowered, value)) { ApplyGain(); OnPropertyChanged(nameof(PowerText)); } }
    }

    public string PowerText => IsPowered ? "Power on" : "Power off";

    private SignalKind _signal = SignalKind.Program;
    public SignalKind Signal
    {
        get => _signal;
        set { if (Set(ref _signal, value)) _engine.Signal = value; }
    }

    private double _inputTrimDb;
    /// <summary>GAIN knob: input trim −24…+24 dB.</summary>
    public double InputTrimDb
    {
        get => _inputTrimDb;
        set { if (Set(ref _inputTrimDb, value)) { ApplyGain(); OnPropertyChanged(nameof(TrimText)); } }
    }

    private double _faderDb;
    /// <summary>Channel fader target gain in dB (−∞ at the bottom).</summary>
    public double FaderDb
    {
        get => _faderDb;
        set { if (Set(ref _faderDb, value)) { ApplyGain(); OnPropertyChanged(nameof(FaderText)); } }
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set { if (Set(ref _isMuted, value)) ApplyGain(); }
    }

    public string TrimText => FormatDb(InputTrimDb);
    public string FaderText => FormatDb(FaderDb);

    private void ApplyGain()
    {
        double db = !IsPowered || IsMuted || double.IsNegativeInfinity(FaderDb) ? double.NegativeInfinity : InputTrimDb + FaderDb;
        _engine.GainDb = db;
    }

    // ------------------------------------------------------------------ measurements (host → controls)

    private double _vuLeft = double.NegativeInfinity, _vuRight = double.NegativeInfinity;
    private double _peakLeft = double.NegativeInfinity, _peakRight = double.NegativeInfinity;
    private double _rmsLeft = double.NegativeInfinity, _rmsRight = double.NegativeInfinity;
    private double _correlation = double.NaN;
    private bool _overloadLeft, _overloadRight;

    public double VuLeft { get => _vuLeft; private set => Set(ref _vuLeft, value); }
    public double VuRight { get => _vuRight; private set => Set(ref _vuRight, value); }
    public double PeakLeft { get => _peakLeft; private set => Set(ref _peakLeft, value); }
    public double PeakRight { get => _peakRight; private set => Set(ref _peakRight, value); }
    public double RmsLeft { get => _rmsLeft; private set => Set(ref _rmsLeft, value); }
    public double RmsRight { get => _rmsRight; private set => Set(ref _rmsRight, value); }
    public double Correlation { get => _correlation; private set => Set(ref _correlation, value); }
    public bool OverloadLeft { get => _overloadLeft; private set => Set(ref _overloadLeft, value); }
    public bool OverloadRight { get => _overloadRight; private set => Set(ref _overloadRight, value); }
    public bool Overload => OverloadLeft || OverloadRight;

    private string _vuText = "", _peakText = "", _rmsText = "", _correlationText = "";
    public string VuText { get => _vuText; private set => Set(ref _vuText, value); }
    public string PeakText { get => _peakText; private set => Set(ref _peakText, value); }
    public string RmsText { get => _rmsText; private set => Set(ref _rmsText, value); }
    public string CorrelationText { get => _correlationText; private set => Set(ref _correlationText, value); }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.MinValue ? 0 : (now - _lastFrame).TotalSeconds;
        _lastFrame = now;

        VuLeft = _engine.Left.VuDb;
        VuRight = _engine.Right.VuDb;
        RmsLeft = _engine.Left.RmsDbfs;
        RmsRight = _engine.Right.RmsDbfs;
        // Peak maxima are aggregated by the provider across all blocks since the previous frame.
        PeakLeft = _engine.Left.ReadPeakDbfs();
        PeakRight = _engine.Right.ReadPeakDbfs();
        bool wasValid = CorrelationValid;
        Correlation = _engine.Correlation.Value;
        if (wasValid != CorrelationValid) OnPropertyChanged(nameof(CorrelationValid));
        bool ovL = _engine.Left.ReadOverload(), ovR = _engine.Right.ReadOverload();
        if (ovL != OverloadLeft || ovR != OverloadRight)
        {
            OverloadLeft = ovL;
            OverloadRight = ovR;
            OnPropertyChanged(nameof(Overload));
        }

        // Numeric readouts refresh at ~8 Hz so they stay legible; peak readouts show the max in that interval.
        _readoutPeakL = Math.Max(_readoutPeakL, PeakLeft);
        _readoutPeakR = Math.Max(_readoutPeakR, PeakRight);
        _readoutTimer += dt;
        if (_readoutTimer >= 0.125)
        {
            _readoutTimer = 0;
            // Readouts follow channel 1, like the analog meters on the Overview and Meters pages.
            VuText = Format(VuLeft, "VU", -60);
            PeakText = Format(_readoutPeakL, "dBFS", -90);
            RmsText = Format(RmsLeft, "dBFS", -90);
            CorrelationText = double.IsNaN(Correlation) ? "---" : Correlation.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture);
            _readoutPeakL = _readoutPeakR = double.NegativeInfinity;
        }

        if (DisplaysActive)
        {
            UpdateScope();
            var spectrum = _engine.Spectrum.Latest;
            if (!ReferenceEquals(spectrum, Spectrum)) Spectrum = spectrum;
        }
        if (IsPlaying) AdvancePlayback(dt);
    }

    private static string Format(double db, string unit, double floor) =>
        double.IsNegativeInfinity(db) || db < floor
            ? $"−∞ {unit}"
            : db.ToString("0.0;−0.0;0.0", CultureInfo.InvariantCulture) + " " + unit;

    public static string FormatDb(double db) =>
        double.IsNegativeInfinity(db) ? "−∞ dB" : db.ToString("+0.0;−0.0;0.0", CultureInfo.InvariantCulture) + " dB";

    // ------------------------------------------------------------------ stereo correlation (Meters page)

    public bool CorrelationValid => !double.IsNaN(Correlation);

    private bool _isMono;
    /// <summary>Mono audition, carried out by the host engine on the monitor path.</summary>
    public bool IsMono
    {
        get => _isMono;
        set { if (Set(ref _isMono, value)) _engine.Mono = value; }
    }

    public RelayCommand MonoCheckCommand => _monoCheckCommand ??= new RelayCommand(_ => IsMono = !IsMono);
    private RelayCommand? _monoCheckCommand;

    private bool _invertRight;
    /// <summary>Flips the right channel's polarity at the source, so the correlation swings negative.</summary>
    public bool InvertRight
    {
        get => _invertRight;
        set { if (Set(ref _invertRight, value)) _engine.InvertRight = value; }
    }

    // ------------------------------------------------------------------ appearance options (Meters page)

    private NeedleDynamicsMode _needleDynamics = NeedleDynamicsMode.Mechanical;
    public NeedleDynamicsMode NeedleDynamics { get => _needleDynamics; set => Set(ref _needleDynamics, value); }

    private OverloadLatchMode _latchMode = OverloadLatchMode.Latched;
    public OverloadLatchMode LatchMode { get => _latchMode; set => Set(ref _latchMode, value); }

    private AnalogMeterType _peakMeterType = AnalogMeterType.Peak;
    public AnalogMeterType PeakMeterType { get => _peakMeterType; set => Set(ref _peakMeterType, value); }

    private double _backlight = 1.0;
    public double Backlight { get => _backlight; set => Set(ref _backlight, value); }

    private bool _peakHold = true;
    public bool PeakHold { get => _peakHold; set => Set(ref _peakHold, value); }

    private bool _ledDotMode;
    public bool LedDotMode { get => _ledDotMode; set => Set(ref _ledDotMode, value); }

    public static IReadOnlyList<FaceOption> FaceOptions { get; } = new[]
    {
        new FaceOption("Green", Color.FromRgb(0x3C, 0xE4, 0x34)),
        new FaceOption("Amber", Color.FromRgb(0xF2, 0xA6, 0x2A)),
        new FaceOption("Ice", Color.FromRgb(0x7C, 0xC8, 0xF0)),
        new FaceOption("Ivory", Color.FromRgb(0xE8, 0xE0, 0xC4)),
    };

    private FaceOption _face = FaceOptions[0];
    public FaceOption Face { get => _face; set => Set(ref _face, value); }

    // ------------------------------------------------------------------ oscilloscope (Displays page)

    public static double[] TimeSteps { get; } = Steps125.Range(0.0001, 0.05);
    public static double[] AmplitudeSteps { get; } = Steps125.Range(0.01, 1);

    /// <summary>Set by the Displays page while it is visible, so history and spectra are copied only when someone is looking.</summary>
    public bool DisplaysActive { get; set; }

    private double _timeIndex = Array.IndexOf(TimeSteps, 0.001);
    public double TimeIndex
    {
        get => _timeIndex;
        set { if (Set(ref _timeIndex, value)) { OnPropertyChanged(nameof(TimePerDivision)); OnPropertyChanged(nameof(TimePerDivisionText)); } }
    }
    public double TimePerDivision => TimeSteps[Math.Clamp((int)Math.Round(TimeIndex), 0, TimeSteps.Length - 1)];
    public string TimePerDivisionText => Steps125.FormatSeconds(TimePerDivision) + "/div";

    private double _amplitudeIndex = Array.IndexOf(AmplitudeSteps, 0.1);
    public double AmplitudeIndex
    {
        get => _amplitudeIndex;
        set { if (Set(ref _amplitudeIndex, value)) { OnPropertyChanged(nameof(AmplitudePerDivision)); OnPropertyChanged(nameof(AmplitudePerDivisionText)); } }
    }
    public double AmplitudePerDivision => AmplitudeSteps[Math.Clamp((int)Math.Round(AmplitudeIndex), 0, AmplitudeSteps.Length - 1)];
    public string AmplitudePerDivisionText => AmplitudePerDivision.ToString("0.###", CultureInfo.InvariantCulture) + " FS/div";

    private double _triggerLevel;
    public double TriggerLevel { get => _triggerLevel; set { if (Set(ref _triggerLevel, value)) OnPropertyChanged(nameof(TriggerLevelText)); } }
    public string TriggerLevelText => TriggerLevel.ToString("+0.00;−0.00;0.00", CultureInfo.InvariantCulture) + " FS";

    private TriggerMode _scopeMode = TriggerMode.Auto;
    public TriggerMode ScopeMode { get => _scopeMode; set => Set(ref _scopeMode, value); }

    private TriggerSlope _scopeSlope = TriggerSlope.Rising;
    public TriggerSlope ScopeSlope { get => _scopeSlope; set => Set(ref _scopeSlope, value); }

    private bool _scopeFrozen;
    public bool ScopeFrozen { get => _scopeFrozen; set => Set(ref _scopeFrozen, value); }

    private double _persistence = 3;
    public double Persistence { get => _persistence; set => Set(ref _persistence, value); }

    private bool _showChannel2 = true;
    public bool ShowChannel2 { get => _showChannel2; set { if (Set(ref _showChannel2, value) && !value) ScopeTrace2 = null; } }

    private TraceSnapshot? _scopeTrace, _scopeTrace2;
    public TraceSnapshot? ScopeTrace { get => _scopeTrace; private set => Set(ref _scopeTrace, value); }
    public TraceSnapshot? ScopeTrace2 { get => _scopeTrace2; private set => Set(ref _scopeTrace2, value); }

    // Two buffers per channel, alternated each frame. The scope consumes a snapshot synchronously when it is set,
    // so reusing arrays avoids allocating large buffers 60 times a second.
    private readonly float[][] _scopeL = new float[2][];
    private readonly float[][] _scopeR = new float[2][];
    private int _scopeFlip;

    private void UpdateScope()
    {
        var history = _engine.History;
        int visible = (int)Math.Round(TimePerDivision * 10 * history.SampleRate);
        int length = Math.Min(history.Capacity, visible * 2 + history.SampleRate / 20);
        _scopeFlip ^= 1;
        if (_scopeL[_scopeFlip]?.Length != length)
        {
            _scopeL[_scopeFlip] = new float[length];
            _scopeR[_scopeFlip] = new float[length];
        }
        var l = _scopeL[_scopeFlip];
        var r = _scopeR[_scopeFlip];
        long start = history.CopyLatest(l, ShowChannel2 ? r : null);
        // Channel 2 first: the scope processes both channels when channel 1 changes.
        if (ShowChannel2) ScopeTrace2 = new TraceSnapshot(history.SampleRate, start, r);
        ScopeTrace = new TraceSnapshot(history.SampleRate, start, l);
    }

    // ------------------------------------------------------------------ spectrum analyzer (Displays page)

    private SpectrumSnapshot? _spectrum;
    public SpectrumSnapshot? Spectrum { get => _spectrum; private set => Set(ref _spectrum, value); }

    public static int[] FftSizes { get; } = { 1024, 2048, 4096, 8192, 16384 };

    /// <summary>FFT length requested from the analysis provider (the host owns framing and the FFT).</summary>
    public int FftSize
    {
        get => _engine.Spectrum.FftSize;
        set { if (value != _engine.Spectrum.FftSize) { _engine.Spectrum.FftSize = value; OnPropertyChanged(); } }
    }

    private SpectrumDisplayMode _spectrumMode = SpectrumDisplayMode.Line;
    public SpectrumDisplayMode SpectrumMode { get => _spectrumMode; set => Set(ref _spectrumMode, value); }

    private int _bandsPerOctave = 3;
    public int BandsPerOctave { get => _bandsPerOctave; set => Set(ref _bandsPerOctave, value); }

    private BandAggregation _bandAggregation = BandAggregation.Maximum;
    public BandAggregation BandAggregation { get => _bandAggregation; set => Set(ref _bandAggregation, value); }

    private double _spectrumAveraging = 0.25;
    public double SpectrumAveraging { get => _spectrumAveraging; set => Set(ref _spectrumAveraging, value); }

    private bool _spectrumPeakHold = true;
    public bool SpectrumPeakHold { get => _spectrumPeakHold; set => Set(ref _spectrumPeakHold, value); }

    // ------------------------------------------------------------------ equalizer and frequency response (Displays page)

    private readonly SimulatedFrequencyResponseSource _eq;
    private int _measureGeneration;
    private bool _measuring;

    private IReadOnlyList<EqBand> _eqBands;
    /// <summary>Bands as acknowledged by the host after constraining each request.</summary>
    public IReadOnlyList<EqBand> EqBands { get => _eqBands; private set => Set(ref _eqBands, value); }

    private ResponseSnapshot? _eqResponse;
    public ResponseSnapshot? EqResponse { get => _eqResponse; private set => Set(ref _eqResponse, value); }

    private ResponseSnapshot? _eqMeasured;
    public ResponseSnapshot? EqMeasured { get => _eqMeasured; private set => Set(ref _eqMeasured, value); }

    private int _selectedBand = 3;
    public int SelectedBand { get => _selectedBand; set => Set(ref _selectedBand, value); }

    private bool _eqEnabled = true;
    /// <summary>EQ in circuit. When bypassed the processor is flat, and so is the response shown.</summary>
    public bool EqEnabled
    {
        get => _eqEnabled;
        set { if (Set(ref _eqEnabled, value)) { _engine.EqEnabled = value; RefreshResponse(); } }
    }

    private bool _showMeasured = true;
    public bool ShowMeasured
    {
        get => _showMeasured;
        set { if (Set(ref _showMeasured, value)) RefreshResponse(); }
    }

    private bool _showPhase;
    public bool ShowPhase { get => _showPhase; set => Set(ref _showPhase, value); }

    public RelayCommand BandChangeCommand => _bandChangeCommand ??= new RelayCommand(p =>
    {
        if (p is not EqBandChangeRequest request) return;
        _eq.Apply(request); // the host constrains the request; the graph then shows what was applied
        ApplyEq();
    });
    private RelayCommand? _bandChangeCommand;

    public RelayCommand ResetEqCommand => _resetEqCommand ??= new RelayCommand(_ =>
    {
        _eq.SetBands(SimulatedFrequencyResponseSource.DefaultBands);
        ApplyEq();
    });
    private RelayCommand? _resetEqCommand;

    private void ApplyEq()
    {
        _engine.SetEqualizer(_eq.Coefficients);
        EqBands = _eq.Bands;
        RefreshResponse();
    }

    private void RefreshResponse()
    {
        if (!EqEnabled)
        {
            EqResponse = SimulatedFrequencyResponseSource.Flat(_eq.SampleRate);
            EqMeasured = null;
            return;
        }
        EqResponse = _eq.ComputeTheoretical();
        if (ShowMeasured) StartMeasurement();
        else EqMeasured = null;
    }

    /// <summary>
    /// Measures the current coefficients on a worker thread. If the bands change meanwhile, one more measurement
    /// follows, so a fast drag keeps at most one measurement in flight and the final state is always measured.
    /// </summary>
    private void StartMeasurement()
    {
        int generation = ++_measureGeneration;
        if (_measuring) return;
        _measuring = true;
        var bands = _eq.Bands;
        int rate = _eq.SampleRate;
        var dispatcher = Dispatcher.CurrentDispatcher;
        Task.Run(() =>
        {
            var probe = new SimulatedFrequencyResponseSource(rate);
            probe.SetBands(bands);
            return probe.Measure(fftSize: 8192, frames: 24, noiseDb: -66, seed: generation);
        }).ContinueWith(t => dispatcher.BeginInvoke(() =>
        {
            _measuring = false;
            if (t.IsCompletedSuccessfully && ShowMeasured && EqEnabled) EqMeasured = t.Result;
            if (generation != _measureGeneration && ShowMeasured && EqEnabled) StartMeasurement();
        }));
    }

    // ------------------------------------------------------------------ recording (Displays page)

    private IWaveformSource? _recording;
    public IWaveformSource? Recording { get => _recording; private set { if (Set(ref _recording, value)) OnPropertyChanged(nameof(RecordingText)); } }
    public string RecordingText => Recording == null ? "No capture yet" :
        $"{(double)Recording.Length / Recording.SampleRate:0.0} s · {Recording.ChannelCount} ch · {Recording.SampleRate / 1000.0:0.#} kHz";

    public RelayCommand CaptureCommand => _captureCommand ??= new RelayCommand(_ => Capture());
    private RelayCommand? _captureCommand;

    public void Capture()
    {
        IsPlaying = false;
        Recording = _engine.History.Capture(10);
        Playhead = 0;
        SelectionStart = SelectionEnd = double.NaN;
    }

    private double _playhead = double.NaN;
    public double Playhead { get => _playhead; set { if (Set(ref _playhead, value)) OnPropertyChanged(nameof(PlayheadText)); } }
    public string PlayheadText => Recording == null || double.IsNaN(Playhead) ? "" : $"▶ {Playhead / Recording.SampleRate:0.000} s";

    private double _selectionStart = double.NaN, _selectionEnd = double.NaN;
    public double SelectionStart { get => _selectionStart; set { if (Set(ref _selectionStart, value)) OnPropertyChanged(nameof(SelectionText)); } }
    public double SelectionEnd { get => _selectionEnd; set { if (Set(ref _selectionEnd, value)) OnPropertyChanged(nameof(SelectionText)); } }
    public string SelectionText => Recording == null || double.IsNaN(SelectionStart) || double.IsNaN(SelectionEnd) ? "No selection" :
        $"Selection {SelectionStart / Recording.SampleRate:0.000}–{SelectionEnd / Recording.SampleRate:0.000} s ({(SelectionEnd - SelectionStart) / Recording.SampleRate * 1000:0} ms)";

    private bool _isPlaying;
    /// <summary>Moves the playhead in real time (a stand-in for a host transport; no audio is played).</summary>
    public bool IsPlaying { get => _isPlaying; set => Set(ref _isPlaying, value); }

    private void AdvancePlayback(double dt)
    {
        if (Recording == null) { IsPlaying = false; return; }
        double p = double.IsNaN(Playhead) ? 0 : Playhead;
        p += dt * Recording.SampleRate;
        double end = double.IsNaN(SelectionEnd) ? Recording.Length : SelectionEnd;
        double begin = double.IsNaN(SelectionStart) ? 0 : SelectionStart;
        if (p >= end) p = begin; // loop the selection (or the whole capture)
        Playhead = p;
    }

    public void Dispose()
    {
        CompositionTarget.Rendering -= OnFrame;
        _engine.Dispose();
    }
}

public sealed record FaceOption(string Name, Color Color)
{
    public override string ToString() => Name;
}
