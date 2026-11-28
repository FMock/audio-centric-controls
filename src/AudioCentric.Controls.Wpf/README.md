# AudioCentric.Controls.Wpf

Realistic, DPI-independent vintage audio controls for WPF. All artwork is procedural vector graphics, so the controls stay sharp at any size and DPI.

| Control | What it is |
|---|---|
| `AnalogMeter` | Backlit VU, sample-peak (to +6 dBFS or PCM 0 dBFS), true-peak and RMS meters with a latching overload lamp |
| `LedBarMeter` | Console LED bar, 1–2 channels, horizontal or vertical, bar or dot mode, peak hold, clip LED |
| `RotaryKnob` | Linear or log parameter knob with detents and steps; drag, wheel and keyboard input |
| `ConsoleFader` | dB-tapered fader with a unity mark and −∞ |
| `IlluminatedToggle` | Push-button, square-cap and lever switches; requested vs actual state; pending and fault states |
| `WaveformDisplay` | Peak-preserving overview, sample-level zoom, ruler, playhead, selection |
| `Oscilloscope` | CRT scope with Free/Auto/Normal/Single triggering, persistence and two channels |
| `SpectrumAnalyzer` | FFT display: log axis, trace or fractional-octave bars, averaging, peak hold |
| `FrequencyResponseGraph` | Gain and phase, theoretical vs measured curves, draggable EQ band handles |
| `CorrelationMeter` | −1…+1 stereo phase correlation with an explicit no-signal state |
| `TuningDial`, `BandspreadDial` | Vintage receiver tuning dial (rotating band scales) and bandspread wheel |
| `JewelLamp` | Faceted jewel pilot light with bulb warm-up |

`RotaryKnob` also comes as a vintage chicken-head knob (`KnobStyle="ChickenHead"`), and `ScaleLabels` turns it into a labelled rotary selector.

```xml
<Window xmlns:acc="https://github.com/FMock/audio-centric-controls">
    <acc:AnalogMeter MeterType="Vu" Value="{Binding VuLeft}" IsOverloaded="{Binding Clipped}" Width="280" Height="280" />
    <acc:RotaryKnob Minimum="-24" Maximum="24" Detent="0" Unit="dB" Value="{Binding TrimDb}" />
</Window>
```

The controls **display measurements your application supplies** (VU, dBFS, dBTP, correlation, spectra, responses) and **publish parameter requests** (knob values, fader positions, EQ band edits). Audio I/O, metering ballistics and DSP stay in your application. Display-only effects such as needle inertia, peak hold, LED decay and overload latching are built in.

Targets `net8.0-windows`. Documentation, binding examples and a live gallery: https://github.com/FMock/audio-centric-controls
