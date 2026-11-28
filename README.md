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
| `SpectrumAnalyzer`: log/linear axis to Nyquist, trace or fractional-octave bars (max or calibrated power sum), display averaging, peak hold, DC readout, cursor readout | ✅ |
| `FrequencyResponseGraph`: gain and phase, theoretical vs measured curves, −3 dB markers, draggable EQ band handles that publish change requests | ✅ |
| `CorrelationMeter`: −1…+1 edgewise meter, zone-coloured bar, explicit NO SIGNAL state, convention and window caption, MONO command button | ✅ |

All artwork is procedural vector graphics. It stays sharp at any size and DPI.

## Install

```
dotnet add package AudioCentric.Controls.Wpf
```

Targets `net8.0-windows`. Then start with [docs/getting-started.md](docs/getting-started.md).

## Documentation

- [Getting started](docs/getting-started.md): install, XAML namespace, first controls, sizing
- [Host integration](docs/host-integration.md): units, silence vs invalid, threading, peak aggregation, snapshots, requests and acknowledgement
- [Control reference](docs/controls.md): every control with its key properties and a binding example
- [Packaging and verification](docs/packaging.md): building, checking and publishing the NuGet packages

## Repository layout

```
src/AudioCentric.Controls.Core     framework-neutral mappings, scales, display dynamics, state models (no WPF)
src/AudioCentric.Controls.Wpf      WPF renderer (the controls)
tests/                             xUnit tests for Core and WPF
examples/AudioControlsGallery.Wpf  live gallery driven by a simulated 48 kHz signal; no audio device needed
examples/PackageConsumer.Wpf       minimal app that uses the controls only through the NuGet packages
scripts/verify-packages.ps1        build, test, pack, then build and smoke-test the package consumer
docs/                              guides and the design reference
assets/                            package icon
```

## Build

Requires Windows and the .NET 8 (or newer) SDK, or Visual Studio 2022. The only platform is x64.

```
dotnet build Audio-Centric-Controls.sln -c Release
dotnet test  Audio-Centric-Controls.sln
bin\x64\Release\AudioControlsGallery.Wpf.exe
dotnet pack  Audio-Centric-Controls.sln -c Release --no-build       # → bin\x64\Release\packages\
powershell -ExecutionPolicy Bypass -File scripts\verify-packages.ps1  # full package check
```

## Using a control

```xml
<Window xmlns:acc="https://github.com/FMock/audio-centric-controls" ...>
    <acc:AnalogMeter MeterType="Vu" Value="{Binding VuLevel}" IsOverloaded="{Binding Clipped}" Width="300" Height="300" />
    <acc:RotaryKnob Minimum="-24" Maximum="24" Detent="0" Value="{Binding GainDb}" />
</Window>
```

The controls display measurements your application supplies (VU, dBFS, dBTP, correlation, spectra, responses) and publish parameter requests back. Audio I/O, analysis and parameter smoothing stay in your application; see [docs/host-integration.md](docs/host-integration.md).

## License and Attribution

Audio-Centric-Controls is released under the MIT License and is freely available for personal, educational, open-source, and commercial software development.

If you incorporate these controls into your application, attribution to the Audio-Centric-Controls project is appreciated, although not required beyond the terms of the MIT License.

If you find this project useful, please consider starring the repository, contributing improvements, or acknowledging the project in your application's documentation or About dialog.
