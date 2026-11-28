# Control reference

Every control is in the `acc` namespace (`xmlns:acc="https://github.com/FMock/audio-centric-controls"`). This page lists the properties that matter for each control; IntelliSense and the XML docs in the package cover the rest. Defaults are in brackets.

- [AnalogMeter](#analogmeter) · [LedBarMeter](#ledbarmeter) · [CorrelationMeter](#correlationmeter)
- [RotaryKnob](#rotaryknob) · [ConsoleFader](#consolefader) · [IlluminatedToggle](#illuminatedtoggle)
- [WaveformDisplay](#waveformdisplay) · [Oscilloscope](#oscilloscope) · [SpectrumAnalyzer](#spectrumanalyzer) · [FrequencyResponseGraph](#frequencyresponsegraph)

---

## AnalogMeter

Backlit moving-coil meter with a latching overload lamp. `MeterType` picks a preset scale: `Vu` (−20…+3 VU), `Peak` (−60…+6 dBFS), `PeakPcm` (−60…0 dBFS), `TruePeak` (dBTP) or `Rms`. You can also set your own `Scale` (a `ScaleDefinition`).

| Property | Meaning |
|---|---|
| `Value` [−∞] | Reading in the scale's unit (VU or dB). −∞ = silence |
| `IsValueValid` [true] | False when the host has no reading; the needle rests |
| `NeedleDynamics` [Mechanical] | `None` tracks the value exactly. Use it when you want no visual inertia |
| `DisplayReleaseDbPerSecond` [NaN = auto] | Auto is 20 dB/s on peak scales and none on VU/RMS (already calibrated) |
| `IsPeakHoldEnabled` [false], `PeakHoldSeconds` [2] | Amber marker at the recent maximum, computed by the control |
| `PeakHoldValue` [NaN] | Alternative: a held peak supplied by the host |
| `IsOverloaded` [false] | Over-range flag from the host; the lamp latches on its rising edge |
| `OverloadLatchMode` [Latched] | `Momentary`, `HoldTime` (`OverloadHoldSeconds`), or `Latched` until reset |
| `ShowOverloadLamp` [true], `OverloadLabel` ["OVL"] | Lamp and its legend |
| `FaceColor`, `NeedleColor`, `IsBacklightEnabled`, `BacklightIntensity` | Appearance |

Reset the lamp by clicking it, by calling `ResetOverload()`, or with `AnalogMeter.ResetOverloadCommand`. The `OverloadReset` event reports each reset.

```xml
<acc:AnalogMeter x:Name="PeakMeter" MeterType="Peak" Width="260" Height="260"
                 Value="{Binding PeakLeftDbfs}" IsOverloaded="{Binding OverLeft}"
                 IsPeakHoldEnabled="True" OverloadLatchMode="Latched" />
<Button Content="Reset" Command="{x:Static acc:AnalogMeter.ResetOverloadCommand}"
        CommandTarget="{Binding ElementName=PeakMeter}" />
```

## LedBarMeter

Console LED bar for one or two channels, with a clip LED.

| Property | Meaning |
|---|---|
| `Value`, `Value2` [−∞] | dBFS readings for channels 1 and 2 |
| `ChannelCount` [1] | 1 or 2 |
| `Scale` [`ScalePresets.LedPeakDense`] | `LedScaleDefinition`: segment thresholds and amber/red boundaries |
| `MeterMode` [Peak] | `Peak`, `TruePeak`, `Rms`, `Vu`; sets the release default and the label |
| `ReleaseDbPerSecond` [NaN = auto] | 20 dB/s in peak modes, none otherwise |
| `IsDotMode` [false] | Light only the top segment |
| `IsPeakHoldEnabled` [true], `PeakHoldSeconds` [2] | Held peak segment |
| `IsClipping`, `ClipLatchMode` [HoldTime], `ShowClipLed` [true] | Clip LED; click it, call `ResetClip()` or use `ResetClipCommand` |
| `Orientation` [Horizontal], `ShowScaleLabels` [false] | Layout |

```xml
<acc:LedBarMeter Height="44" ChannelCount="2" ShowScaleLabels="True"
                 Value="{Binding PeakL}" Value2="{Binding PeakR}" IsClipping="{Binding Over}" />
```

## CorrelationMeter

Edgewise −1…+1 stereo phase correlation meter: green above 0, amber and red below, with an explicit NO SIGNAL state.

| Property | Meaning |
|---|---|
| `Correlation` [NaN] | ρ from the host. NaN = unavailable |
| `IsValid` [true] | False also shows NO SIGNAL |
| `WindowDuration` [0.3 s], `Convention` ["uncentered"] | Printed on the dial so readers know what they are looking at |
| `NeedleDynamics` [None] | The host's window already smooths |
| `ShowBar` [true] | Zone-coloured bar from 0 to the reading |
| `MonoCheckCommand`, `IsMonoActive` | When a command is set, a MONO button appears; it lights from `IsMonoActive` |
| `Scale` [`ScalePresets.Correlation`], `FaceColor`, `NeedleColor`, backlight | Appearance |

```xml
<acc:CorrelationMeter Height="120" Correlation="{Binding Correlation}"
                      MonoCheckCommand="{Binding MonoCommand}" IsMonoActive="{Binding IsMono}" />
```

---

## RotaryKnob

| Property | Meaning |
|---|---|
| `Value` [0] (two-way) | Target value in engineering units |
| `Minimum` [0], `Maximum` [1], `DefaultValue` [0] | Range; double-click resets to the default |
| `Mapping` [Linear] | `Logarithmic` for frequency-style knobs (20 Hz–20 kHz puts 632 Hz at the centre) |
| `Step` [0 = continuous], `Detent` [NaN] | Quantization and a sticky detent (0 dB, centre pan) |
| `SweepAngle` [270], `TickCount` [11], `ShowValueArc` [true], `IndicatorColor` | Appearance |
| `Unit` [""], `IsReadOnly` [false] | Used by the automation peer and readouts |

Input: vertical drag (Shift for fine), mouse wheel, arrow keys, PageUp/PageDown, Home/End, double-click.

```xml
<acc:RotaryKnob Width="80" Height="80" Minimum="20" Maximum="20000" Mapping="Logarithmic"
                DefaultValue="1000" Unit="Hz" Value="{Binding CutoffHz}" />
```

## ConsoleFader

| Property | Meaning |
|---|---|
| `ValueDb` [0] (two-way) | Target gain in dB; the bottom is −∞ |
| `IsMuted` [false] (two-way) | Mute |
| `Taper` [`FaderTaper.Default`] | Position ↔ dB law (console-style anchors, unity mark, linear-amplitude ramp to −∞) |
| `DefaultValueDb` [0], `Orientation` [Vertical], `IsReadOnly`, `IndicatorColor` | Behaviour and look |

```xml
<acc:ConsoleFader Height="300" ValueDb="{Binding ChannelGainDb}" IsMuted="{Binding Muted}" />
```

## IlluminatedToggle

A `ToggleButton`, so `IsChecked`, `Command`, `Click` and keyboard support work as usual.

| Property | Meaning |
|---|---|
| `SwitchStyle` [PushButton] | `PushButton`, `SquareCap`, `Lever` |
| `Mode` [Latching] | `Momentary` is on only while pressed |
| `IsChecked` | The user's **request** |
| `ActualState` [null] | The engine's **result**; the lamp follows it when set |
| `Status` [Ready] | `Pending` (amber) or `Fault` (lamp off) |
| `OnLabel` ["ON"], `OffLabel` [""], `LampColor`, `ShowPowerIcon` | Appearance |

```xml
<acc:IlluminatedToggle OnLabel="Bypass" LampColor="#FFFFB01E"
                       IsChecked="{Binding BypassRequested}" ActualState="{Binding BypassActual}" Status="{Binding BypassStatus}" />
```

---

## WaveformDisplay

| Property | Meaning |
|---|---|
| `Source` | An `IWaveformSource`, e.g. `new InMemoryWaveformSource(48000, left, right)` |
| `VisibleStartSample`, `VisibleEndSample` (two-way) | View range; end ≤ start shows everything |
| `Playhead` [NaN], `SelectionStart`, `SelectionEnd` (two-way) | Transport overlay; NaN hides |
| `AmplitudeScale` [1], `ShowRuler` [true], `TraceColor` | Display |

Methods: `ZoomToFit()`, `Zoom(factor, anchorSample)`, `Scroll(samples)`. Mouse: click places the playhead, drag selects, the wheel zooms at the pointer, Shift+wheel or right-drag scrolls, Ctrl+wheel zooms the amplitude. Keys: arrows, +/−, Home/End, 0, Esc.

```xml
<acc:WaveformDisplay Height="240" Source="{Binding Recording}" Playhead="{Binding Playhead}"
                     SelectionStart="{Binding SelStart}" SelectionEnd="{Binding SelEnd}" />
```

## Oscilloscope

| Property | Meaning |
|---|---|
| `Trace`, `Trace2` | `TraceSnapshot`s of recent history; set `Trace2` first |
| `TimePerDivision` [1 ms], `AmplitudePerDivision` [0.25 FS] | 10 × 8 divisions; `Steps125.Range` makes 1-2-5 selector values |
| `TriggerMode` [Auto] | `Free`, `Auto`, `Normal`, `Single` (re-arm with `Arm()` or `ArmCommand`) |
| `TriggerSlope`, `TriggerLevel`, `TriggerPosition` [0.5], `HoldoffSeconds` | Trigger |
| `IsFrozen`, `Persistence` [3 traces] | Freeze and phosphor afterglow (`ClearPersistence()`) |
| `Status` (read-only) | `Triggered`, `FreeRunning`, `Waiting`, `Stopped` |

```xml
<acc:Oscilloscope Height="400" Trace2="{Binding ScopeR}" Trace="{Binding ScopeL}"
                  TimePerDivision="0.0005" TriggerMode="Normal" TriggerLevel="0.1" />
```

## SpectrumAnalyzer

| Property | Meaning |
|---|---|
| `Spectrum` | The newest `SpectrumSnapshot`; each new one advances averaging and peak hold |
| `MinimumFrequency` [20], `MaximumFrequency` [20000], `FrequencyScale` [Logarithmic] | The axis stops at Nyquist if that is lower |
| `MaximumDb` [0], `DisplayRangeDb` [90] | Level window |
| `DisplayMode` [Line] | `Line` (one point per device pixel, peak-preserving) or `Bars` |
| `BandsPerOctave` [3], `BandAggregation` [Maximum] | Bars: base-2 fractional octaves. `Maximum` keeps tone levels; `PowerSum` shows band energy |
| `AveragingTime` [0] | Display-only exponential averaging in the power domain (seconds) |
| `IsPeakHoldEnabled` [true], `PeakHoldSeconds` [1.5], `PeakDecayDbPerSecond` [20] | Amber held peaks (`ResetPeaks()`) |
| `TraceColor`, `PeakColor` | Appearance |

A DC bin is shown as a separate readout, because 0 Hz has no place on a log axis. Hover for a frequency and level readout.

```xml
<acc:SpectrumAnalyzer Height="300" Spectrum="{Binding Spectrum}" DisplayMode="Bars" BandsPerOctave="3" AveragingTime="0.25" />
```

## FrequencyResponseGraph

| Property | Meaning |
|---|---|
| `Response`, `OverlayResponse` | `ResponseSnapshot`s. Theoretical curves are drawn solid with a glow; `IsMeasured` ones dashed |
| `ShowPhase` [false] | Phase on a ±180° right-hand axis, wrapped, with breaks at the wrap |
| `ShowCutoffMarkers` [true], `CutoffLevelDb` [−3] | Diamonds and labels where the main response crosses the level |
| `MinimumDb` [−24], `MaximumDb` [24], `MinimumFrequency`, `MaximumFrequency` | Axes |
| `Bands` | `IReadOnlyList<EqBand>`, one handle each. Replace the list to update it |
| `SelectedBandIndex` [−1] (two-way), `IsEditable` [true] | Selection and editing |
| `BandChangeCommand`, `BandChangeRequested` event | Receive `EqBandChangeRequest`s (see [host-integration.md](host-integration.md#parameters-requests-and-acknowledgement)) |
| `TraceColor`, `OverlayColor`, `PhaseColor` | Appearance |

Mouse: drag a handle (frequency and gain), wheel for Q, double-click to zero the gain, right-click to bypass. Keys: 1–9 select a band, ←/→ frequency (Shift for fine), ↑/↓ gain, PgUp/PgDn Q, Space bypass, 0 zero gain. `RequestBandChange(index, band)` sends the same request from code.

```xml
<acc:FrequencyResponseGraph Height="320" Response="{Binding Theory}" OverlayResponse="{Binding Measured}"
                            Bands="{Binding Bands}" SelectedBandIndex="{Binding Selected}"
                            BandChangeCommand="{Binding BandChangeCommand}" ShowPhase="True" />
```
