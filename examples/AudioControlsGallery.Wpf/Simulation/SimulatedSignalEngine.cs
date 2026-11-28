using System.Diagnostics;
using AudioCentric.Controls.Measurements;

namespace AudioControlsGallery.Simulation;

public enum SignalKind
{
    /// <summary>A synthetic drum-and-bass groove with pad, plus an occasional hot transient that exceeds full scale.</summary>
    Program,
    /// <summary>1 kHz sine at −18 dBFS sample peak (= 0 VU at unity gain).</summary>
    ReferenceTone,
    /// <summary>Uncorrelated pink-ish noise at about −20 dBFS RMS.</summary>
    Noise,
    /// <summary>Logarithmic sine sweep, 20 Hz → 20 kHz every 8 s, at −12 dBFS.</summary>
    Sweep,
    /// <summary>Digital silence.</summary>
    Silence,
    /// <summary>Tunable test oscillator (see <see cref="SimulatedSignalEngine.OscillatorFrequency"/>), mono.</summary>
    Oscillator,
}

public enum OscillatorWaveform
{
    Sine,
    Triangle,
    Square,
}

/// <summary>
/// The gallery's stand-in for a host audio application. A background thread produces 48 kHz stereo blocks in
/// real time, applies a smoothed gain (one-pole, about 20 ms, to avoid zipper noise) and feeds the analysis
/// providers. The UI reads the newest results at frame rate. No audio device is used.
/// </summary>
public sealed class SimulatedSignalEngine : IDisposable
{
    public const int SampleRate = 48000;
    private const int BlockSize = 256;

    private readonly Thread _thread;
    private volatile bool _running = true;
    private readonly float[] _left = new float[BlockSize];
    private readonly float[] _right = new float[BlockSize];
    private readonly Random _rng = new(7);

    private double _targetGain = 1.0;
    private double _gain = 1.0;
    private readonly double _gainCoeff = Math.Exp(-1.0 / (SampleRate * 0.02));
    private long _sample;
    private volatile int _signal = (int)SignalKind.Program;

    // Program-source state
    private double _kickPhase, _bassPhase, _tonePhase, _sweepPhase;

    // Equalizer (host processing). The UI swaps in a new coefficient array; the audio thread keeps its filter state.
    private const int MaxEqBands = 8;
    private volatile Biquad[] _eq = Array.Empty<Biquad>();
    private volatile bool _eqEnabled = true;
    private readonly BiquadState[] _eqL = new BiquadState[MaxEqBands], _eqR = new BiquadState[MaxEqBands];
    private volatile bool _invertRight, _mono;

    // Test oscillator: band-limited (PolyBLEP) square, and a triangle integrated from it, so the spectrum stays clean.
    private double _oscFrequency = 1000, _oscLevel = Decibels.ToAmplitude(-12);
    private volatile int _oscWaveform;
    private double _oscPhase, _oscTriangle;
    private readonly double[] _padPhase = new double[6];
    private double _pinkL, _pinkR, _hatHpL, _hatHpR;
    private double _nextHotHit = 9.0;

    public SimulatedSignalEngine()
    {
        Left = new SimulatedMeterSource(SampleRate);
        Right = new SimulatedMeterSource(SampleRate);
        Correlation = new SimulatedCorrelationSource(SampleRate);
        History = new SimulatedWaveformSource(SampleRate);
        Spectrum = new SimulatedSpectrumSource(SampleRate);
        _thread = new Thread(Run) { IsBackground = true, Name = "Simulated audio engine", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public SimulatedMeterSource Left { get; }
    public SimulatedMeterSource Right { get; }
    public SimulatedCorrelationSource Correlation { get; }

    /// <summary>Oscillator frequency in Hz (20 Hz … 0.45 fs).</summary>
    public double OscillatorFrequency
    {
        get => Volatile.Read(ref _oscFrequency);
        set => Volatile.Write(ref _oscFrequency, Math.Clamp(value, 1, SampleRate * 0.45));
    }

    /// <summary>Oscillator peak level in dBFS.</summary>
    public double OscillatorLevelDb
    {
        get => Decibels.FromAmplitude(Volatile.Read(ref _oscLevel));
        set => Volatile.Write(ref _oscLevel, Decibels.ToAmplitude(value));
    }

    public OscillatorWaveform OscillatorWaveform
    {
        get => (OscillatorWaveform)_oscWaveform;
        set => _oscWaveform = (int)value;
    }

    /// <summary>FFT analysis of the monitored (mid) signal.</summary>
    public SimulatedSpectrumSource Spectrum { get; }

    /// <summary>Replaces the equalizer cascade (at most 8 sections). Safe to call from the UI thread.</summary>
    public void SetEqualizer(Biquad[] coefficients) => _eq = coefficients.Take(MaxEqBands).ToArray();

    /// <summary>Equalizer in circuit (false = bypass).</summary>
    public bool EqEnabled { get => _eqEnabled; set => _eqEnabled = value; }

    /// <summary>Flips the polarity of the right channel, a wiring fault that drives correlation negative.</summary>
    public bool InvertRight { get => _invertRight; set => _invertRight = value; }

    /// <summary>
    /// Mono check: the monitor path (level meters, scope, spectrum, history) hears (L+R)/2. Like a console's mono button
    /// it does not change the stereo bus, so the correlation meter keeps reading the stereo signal.
    /// </summary>
    public bool Mono { get => _mono; set => _mono = value; }

    /// <summary>Recent stereo history for the oscilloscope and waveform captures.</summary>
    public SimulatedWaveformSource History { get; }

    public SignalKind Signal
    {
        get => (SignalKind)_signal;
        set => _signal = (int)value;
    }

    /// <summary>Overall gain in dB applied to the signal (−∞ = off).</summary>
    public double GainDb
    {
        get => Decibels.FromAmplitude(Volatile.Read(ref _targetGain));
        set => Volatile.Write(ref _targetGain, Decibels.ToAmplitude(value));
    }

    private void Run()
    {
        var clock = Stopwatch.StartNew();
        long produced = 0;
        while (_running)
        {
            long due = (long)(clock.Elapsed.TotalSeconds * SampleRate);
            // If the process was suspended, skip ahead rather than racing to catch up.
            if (due - produced > SampleRate / 4) produced = due - BlockSize;
            while (produced + BlockSize <= due)
            {
                RenderBlock();
                Correlation.Process(_left, _right); // stereo bus
                if (_mono)
                {
                    for (int i = 0; i < BlockSize; i++) _left[i] = _right[i] = 0.5f * (_left[i] + _right[i]);
                }
                Left.Process(_left);
                Right.Process(_right);
                History.Append(_left, _right);
                Spectrum.Process(_left, _right);
                produced += BlockSize;
            }
            Thread.Sleep(2);
        }
    }

    private void RenderBlock()
    {
        var kind = Signal;
        double target = Volatile.Read(ref _targetGain);
        var eq = _eqEnabled ? _eq : Array.Empty<Biquad>();
        bool invert = _invertRight;
        for (int i = 0; i < BlockSize; i++)
        {
            double t = (double)_sample / SampleRate;
            double l, r;
            switch (kind)
            {
                case SignalKind.ReferenceTone:
                    _tonePhase += 2 * Math.PI * 1000.0 / SampleRate;
                    if (_tonePhase > 2 * Math.PI) _tonePhase -= 2 * Math.PI;
                    l = r = Decibels.ToAmplitude(-18) * Math.Sin(_tonePhase);
                    break;
                case SignalKind.Noise:
                    l = Pink(ref _pinkL) * 0.33;
                    r = Pink(ref _pinkR) * 0.33;
                    break;
                case SignalKind.Sweep:
                    double f = 20 * Math.Pow(1000, t % 8.0 / 8.0);
                    _sweepPhase += 2 * Math.PI * f / SampleRate;
                    if (_sweepPhase > 2 * Math.PI) _sweepPhase -= 2 * Math.PI;
                    l = r = Decibels.ToAmplitude(-12) * Math.Sin(_sweepPhase);
                    break;
                case SignalKind.Silence:
                    l = r = 0;
                    break;
                case SignalKind.Oscillator:
                    l = r = Oscillator();
                    break;
                default:
                    (l, r) = Program(t);
                    break;
            }

            for (int k = 0; k < eq.Length; k++)
            {
                l = _eqL[k].Process(eq[k], l);
                r = _eqR[k].Process(eq[k], r);
            }
            if (invert) r = -r;

            _gain = _gainCoeff * _gain + (1 - _gainCoeff) * target;
            _left[i] = (float)(l * _gain);
            _right[i] = (float)(r * _gain);
            _sample++;
        }
    }

    private double Oscillator()
    {
        double dt = Volatile.Read(ref _oscFrequency) / SampleRate;
        double amp = Volatile.Read(ref _oscLevel);
        _oscPhase += dt;
        if (_oscPhase >= 1) _oscPhase -= 1;
        double t = _oscPhase;
        switch ((OscillatorWaveform)_oscWaveform)
        {
            case OscillatorWaveform.Sine:
                return amp * Math.Sin(2 * Math.PI * t);
            default:
                double sq = (t < 0.5 ? 1 : -1) + PolyBlep(t, dt) - PolyBlep((t + 0.5) % 1, dt);
                if ((OscillatorWaveform)_oscWaveform == OscillatorWaveform.Square) return amp * 0.8 * sq;
                // Leaky integration of the square gives a band-limited triangle of about ±1.
                _oscTriangle = 0.9995 * _oscTriangle + 4 * dt * sq;
                return amp * _oscTriangle;
        }
    }

    /// <summary>Polynomial band-limited step correction for a discontinuity at phase 0.</summary>
    private static double PolyBlep(double t, double dt)
    {
        if (t < dt) { t /= dt; return t + t - t * t - 1; }
        if (t > 1 - dt) { t = (t - 1) / dt; return t * t + t + t + 1; }
        return 0;
    }

    private double White() => _rng.NextDouble() * 2 - 1;

    private double Pink(ref double state)
    {
        // Leaky-integrated white noise: a cheap, warm-sounding approximation of pink noise.
        state = 0.97 * state + 0.03 * White() * 6.0;
        return Math.Clamp(state + White() * 0.25, -1, 1);
    }

    /// <summary>120 BPM groove: kick on every beat, snare on 2 and 4, hats on eighths, bass and a slow pad.</summary>
    private (double L, double R) Program(double t)
    {
        const double beat = 0.5;
        double inBeat = t % beat;
        int beatIndex = (int)(t / beat);
        int bar = beatIndex / 4;

        // Kick: pitch-swept sine with fast decay
        double kickEnv = Math.Exp(-inBeat / 0.11);
        double kickFreq = 48 + 95 * Math.Exp(-inBeat / 0.03);
        _kickPhase += 2 * Math.PI * kickFreq / SampleRate;
        if (inBeat < 1.0 / SampleRate) _kickPhase = 0;
        double kick = 0.52 * kickEnv * Math.Sin(_kickPhase);

        // Snare on beats 2 and 4
        double snare = 0;
        if (beatIndex % 2 == 1) snare = 0.34 * Math.Exp(-inBeat / 0.09) * White();

        // Hats on eighths, high-passed noise, panned right
        double inEighth = t % (beat / 2);
        double hatRaw = White();
        double hatL = hatRaw - _hatHpL; _hatHpL = hatRaw * 0.6;
        double hatR = hatRaw - _hatHpR; _hatHpR = hatRaw * 0.6;
        double hatEnv = 0.075 * Math.Exp(-inEighth / 0.025);

        // Bass line (A1 → C2 → D2 → G1)
        double[] notes = { 55.0, 65.41, 73.42, 49.0 };
        double bassFreq = notes[bar % 4];
        _bassPhase += 2 * Math.PI * bassFreq / SampleRate;
        if (_bassPhase > 2 * Math.PI) _bassPhase -= 2 * Math.PI;
        double bassEnv = 0.20 * (0.55 + 0.45 * Math.Exp(-inBeat / 0.25));
        double bass = bassEnv * (Math.Sin(_bassPhase) + 0.3 * Math.Sin(2 * _bassPhase));

        // Pad: slightly detuned chord, swelling slowly
        double[] padFreqs = { 220.0, 261.63, 329.63 };
        double swell = 0.5 + 0.5 * Math.Sin(2 * Math.PI * t / 8.0);
        double padL = 0, padR = 0;
        for (int i = 0; i < 3; i++)
        {
            _padPhase[i] += 2 * Math.PI * padFreqs[i] / SampleRate;
            _padPhase[i + 3] += 2 * Math.PI * padFreqs[i] * 1.004 / SampleRate;
            padL += Math.Sin(_padPhase[i]);
            padR += Math.Sin(_padPhase[i + 3]);
        }
        foreach (ref var p in _padPhase.AsSpan()) if (p > 2 * Math.PI) p -= 2 * Math.PI;
        padL *= 0.035 * (0.4 + 0.6 * swell);
        padR *= 0.035 * (0.4 + 0.6 * swell);

        // Occasional "hot" hit that overshoots full scale, to exercise peak meters and overload lamps.
        double hot = 0;
        if (t >= _nextHotHit)
        {
            double dt = t - _nextHotHit;
            if (dt < 0.25) hot = 1.25 * Math.Exp(-dt / 0.05) * Math.Sin(2 * Math.PI * 90 * dt);
            else _nextHotHit = t + 8 + _rng.NextDouble() * 6;
        }

        // Mix trim puts typical program around 0 VU at unity gain (−18 dBFS alignment).
        const double mix = 0.62;
        double center = (kick + snare + bass) * mix + hot;
        return (center + (hatL * hatEnv * 0.6 + padL) * mix, center * 0.97 + (hatR * hatEnv + padR) * mix);
    }

    public void Dispose()
    {
        _running = false;
        _thread.Join(500);
    }
}
