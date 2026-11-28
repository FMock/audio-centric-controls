# audio-centric-controls
An open-source library of reusable audio controls and visualization components, including vintage-style analog meters, rotary knobs, faders, waveform displays, oscilloscopes, and spectrum analyzers. Designed for audio applications, digital signal processing, and professional audio software development.

## What's here

| Control | Status |
|---|---|
| `AnalogMeter`: backlit VU, sample-peak (to +6 dBFS or PCM 0 dBFS), true-peak and RMS meters, with a latching overload lamp and reset | ✅ |
| `LedBarMeter`: console LED bar, 1–2 channels, horizontal/vertical, bar/dot mode, peak hold, clip LED | ✅ |
| `RotaryKnob`: linear/log mapping, detents, steps, drag/wheel/keyboard, double-click reset | ✅ |
| `ConsoleFader`: dB taper with unity mark and −∞, vertical/horizontal | ✅ |
| `IlluminatedToggle`: push-button, square cap and lever styles; momentary/latching; pending/fault states | ✅ |
| `WaveformDisplay`: peak-preserving min/max overview, sample-level zoom, ruler, playhead, selection, stereo lanes | ✅ |
| `Oscilloscope`: CRT look, 1-2-5 timebase, Free/Auto/Normal/Single trigger with level, slope and holdoff, freeze, persistence, 2 channels | ✅ |
| FFT spectrum analyzer, frequency-response graph, stereo correlation meter | planned |

All artwork is procedural vector graphics. It stays sharp at any size and DPI.

## Repository layout

```
src/AudioCentric.Controls.Core   framework-neutral mappings, scales, display dynamics, state models (no WPF)
src/AudioCentric.Controls.Wpf    WPF renderer (the controls)
tests/                           xUnit tests for Core and WPF
examples/AudioControlsGallery.Wpf  live gallery driven by a simulated 48 kHz signal; no audio device needed
docs/                            design reference
```

## Build

Requires Windows and the .NET 8 (or newer) SDK, or Visual Studio 2022. The only platform is x64.

```
dotnet build Audio-Centric-Controls.sln -c Release
dotnet test  Audio-Centric-Controls.sln
bin\x64\Release\AudioControlsGallery.Wpf.exe
```

## Using a control

```xml
<Window xmlns:acc="https://github.com/FMock/audio-centric-controls" ...>
    <acc:AnalogMeter MeterType="Vu" Value="{Binding VuLevel}" IsOverloaded="{Binding Clipped}" Width="300" Height="300" />
    <acc:RotaryKnob Minimum="-24" Maximum="24" Detent="0" Value="{Binding GainDb}" />
</Window>
```

The controls display measurements your application supplies (VU, dBFS, dBTP) and publish parameter targets back. Audio I/O, analysis and parameter smoothing stay in your application.

## License and Attribution

Audio-Centric-Controls is released under the MIT License and is freely available for personal, educational, open-source, and commercial software development.

If you incorporate these controls into your application, attribution to the Audio-Centric-Controls project is appreciated, although not required beyond the terms of the MIT License.

If you find this project useful, please consider starring the repository, contributing improvements, or acknowledging the project in your application's documentation or About dialog.
