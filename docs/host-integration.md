# Host integration

The controls are a display and input layer. Your application (the **host**) owns audio I/O, analysis and parameter processing. This page is the contract between the two. Each rule comes from the design reference, and the gallery's `examples/AudioControlsGallery.Wpf/Simulation/` implements all of them.

## Who does what

| | Host (your code) | Controls (this library) |
|---|---|---|
| Level meters | Calibrated VU dynamics, sample/true peak, RMS windows, over-range detection | Needle inertia, display release, peak-hold marker, LED decay, overload lamp latch and reset |
| Correlation | Σ LR / √(Σ L² Σ R²) over a window; validity | −1…+1 mapping, zones, NO SIGNAL state |
| Waveform | Decoding, storage, an `IWaveformSource` | Peak-preserving reduction, zoom, scroll, playhead and selection |
| Oscilloscope | Recent sample history | Timebase, triggering, persistence, freeze |
| Spectrum | Framing, window, FFT, calibration | Axis, bins → columns or bands, display averaging, peak hold |
| Response / EQ | Coefficients, response maths, applying band changes | Curves, cutoff markers, handles that publish **requests** |
| Knobs, faders, switches | Applying targets with smoothing; reporting the actual state | Interaction, value mapping, showing requested vs actual state |

**Never smooth twice.** A calibrated VU or RMS value already has its dynamics. `AnalogMeter` adds only mechanical needle inertia, which you can switch off with `NeedleDynamics="None"`. Peak scales get a 20 dB/s display release by default; you can change it with `DisplayReleaseDbPerSecond`.

## Units, silence and invalid values

- Bind each value in the unit the control expects: VU for `MeterType="Vu"`, dBFS for peak and RMS, dBTP for true peak, a −1…+1 ratio for correlation, relative dB (not dBFS) for responses.
- **Silence is `double.NegativeInfinity`**, not −120 or 0. Meters fall to the stop; spectra draw at the floor.
- **Invalid is `NaN` or a validity flag** (`AnalogMeter.IsValueValid`, `CorrelationMeter.IsValid`, `ResponseSnapshot.IsValid`). Keep the two cases separate. A correlation meter must show NO SIGNAL when a channel is silent, never a 0 reading.
- `Decibels.FromAmplitude(0)` returns −∞, so converting a silent block gives the right answer.

## Threading: compute on the audio thread, publish at frame rate

Audio callbacks must never block or allocate, so they cannot touch WPF objects. The pattern the gallery uses:

1. **Audio thread:** process each block and write the newest results to fields without locks (`Volatile.Write`, `Interlocked`).
2. **UI thread:** once per rendered frame (`CompositionTarget.Rendering`), read the newest values and set the bound properties.

```csharp
CompositionTarget.Rendering += (_, e) =>
{
    VuLeft    = engine.Left.VuDb;            // latest value: a plain volatile read
    PeakLeft  = engine.Left.ReadPeakDbfs();  // aggregated: max since the previous read, then reset
    Overload  = engine.Left.ReadOverload();  // any over-range since the previous read, then reset
    Spectrum  = engine.Spectrum.Latest;      // immutable snapshot, swapped atomically
};
```

### Peaks must be aggregated, not sampled

At 48 kHz with 256-sample blocks, about three blocks arrive per 60 Hz frame. If the UI read only the last block's peak, a transient in an earlier block would never reach the meter. Accumulate the maximum on the audio thread and take-and-reset it from the UI:

```csharp
private long _peakBits; // max |x| since last read, as the bits of a non-negative double (these order like longs)

public void Process(ReadOnlySpan<float> block)            // audio thread
{
    double peak = 0;
    foreach (var x in block) peak = Math.Max(peak, Math.Abs(x));
    long bits = BitConverter.DoubleToInt64Bits(peak), current = Interlocked.Read(ref _peakBits);
    while (bits > current)
    {
        long seen = Interlocked.CompareExchange(ref _peakBits, bits, current);
        if (seen == current) break;
        current = seen;
    }
}

public double ReadPeakDbfs()                                 // UI thread
{
    double p = BitConverter.Int64BitsToDouble(Interlocked.Exchange(ref _peakBits, 0));
    return p <= 0 ? double.NegativeInfinity : Decibels.FromAmplitude(p);
}
```

Overload flags work the same way and may be true for a single frame. The controls latch on the rising edge, so a one-frame `true` is enough to light the lamp.

## Snapshots

Plot controls take immutable records from `AudioCentric.Controls.Measurements`. Build a new snapshot for each update and never change one after handing it over: the control reads it on the UI thread, and it may keep a reference.

| Record | Used by | Notes |
|---|---|---|
| `TraceSnapshot(SampleRate, StartSample, Samples)` | `Oscilloscope.Trace`, `Trace2` | Recent history, newest sample last, at least two screens long. `StartSample` is the absolute index of `Samples[0]`, used for holdoff. Set `Trace2` before `Trace`; setting `Trace` triggers processing. |
| `SpectrumSnapshot(SampleRate, Channel, Frequencies, LevelsDb, Unit)` with `FftSize`, `Window`, `NoiseBandwidthBins`, `StartSample` | `SpectrumAnalyzer.Spectrum` | Ascending bin frequencies. Bin 0 may be DC. Levels are peak-calibrated dB; for Hann, `NoiseBandwidthBins = 1.5` keeps power-sum bands calibrated. |
| `ResponseSnapshot(Frequencies, GainDb, PhaseDegrees, IsValid)` with `IsMeasured`, `SampleRate` | `FrequencyResponseGraph.Response`, `OverlayResponse` | Relative dB. A NaN gain breaks the curve (a masked, unreliable point). |
| `IWaveformSource` / `InMemoryWaveformSource` | `WaveformDisplay.Source` | Provides min/max summaries for any range; raise `Changed` when it grows. |

Reusing arrays is fine when the consumer is done with them synchronously. The gallery's scope alternates between two buffers, because `Oscilloscope` processes a trace the moment it is set. Spectra and responses must not be changed after you hand them over.

### Spectrum calibration in brief

With an unnormalized FFT X[k] of N windowed samples and W = Σ w[n]: A[k] = 2|X[k]|/W for interior bins and |X[k]|/W at DC and Nyquist, so a full-scale bin-centred sine reads 0 dBFS. Bin spacing is fs/N. Hann at N = 4096 and 48 kHz gives 11.7 Hz bins and 85 ms frames. The gallery's `SpectrumAnalysis.cs` is a dependency-free implementation that is tested against a reference DFT.

## Parameters: requests and acknowledgement

Input controls publish what the user asked for. Your engine decides what actually happens.

**Continuous targets** (`RotaryKnob.Value`, `ConsoleFader.ValueDb`, `ConsoleFader.IsMuted`) bind two-way by default. Smooth them in the engine, for example with a one-pole filter over about 20 ms, to avoid zipper noise. The control is not the smoother.

**Switches** separate the request from the result:

```xml
<acc:IlluminatedToggle OnLabel="Record" IsChecked="{Binding ArmRequested}"
                       ActualState="{Binding ArmActual}" Status="{Binding ArmStatus}" />
```

Set `Status = Pending` while the engine works on it, then set `ActualState` and `Status = Ready`, or `Fault` if it failed. The lamp follows `ActualState`, not the click.

**EQ bands** never change in the graph itself. A drag, wheel or key press executes `BandChangeCommand` with an `EqBandChangeRequest(Index, Requested)`, and also raises the `BandChangeRequested` routed event. Your host constrains the request, applies it, and sends back both the bands and the response:

```csharp
BandChangeCommand = new RelayCommand(p =>
{
    var r = (EqBandChangeRequest)p!;
    var applied = r.Requested with { GainDb = Math.Clamp(r.Requested.GainDb, -12, 12) }; // the host's rules
    var bands = Bands.ToArray();
    bands[r.Index] = applied;
    eq.SetBands(bands);                 // recompute coefficients, hand them to the audio thread
    Bands = bands;                      // acknowledged parameters → handles move
    Response = eq.ComputeResponse();    // acknowledged response → curve follows the real processor
});
```

If you ignore a request, the handle stays where it was, which is correct: the graph always shows the processor's real state.

**Host commands** such as `CorrelationMeter.MonoCheckCommand` only execute your `ICommand`. The mono sum happens in the engine, and you report it back through `IsMonoActive`. On a console, mono affects the monitor path, not the bus being metered.

## Testing with deterministic providers

Hosts are easiest to test with deterministic mock providers: functions of time or seeded generators, so the same input gives the same output. `examples/PackageConsumer.Wpf/DemoHost.cs` drives every pattern on this page without any audio. The gallery's providers are verified in `tests/AudioCentric.Controls.Core.Tests/GalleryProviderTests.cs`. Those tests cover the cases from the design reference's verification lists: bin-centred sines, DC and Nyquist, leakage, silence, unity and cutoff responses, cascade sums, measured vs theoretical, and correlation of identical, inverted, quadrature and silent signals.
