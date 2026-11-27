# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Audio-Centric-Controls is an MIT-licensed library of reusable, realistic, DPI-scalable vintage audio controls (analog VU/peak/RMS meters, LED bar meters, rotary knobs, console faders, illuminated switches; planned: waveform display, oscilloscope, FFT spectrum analyzer, frequency-response graph, stereo correlation meter). WPF is the first renderer. The design spec is `docs/Audio-Centric-Controls-Public.docx` (per-control formulas, display defaults, API properties, verification lists). Read the relevant section before implementing a control. From the shell: unzip `word/document.xml` and strip tags. The images in the docx (`word/media/image1-3.png`) are the visual targets for the VU, Peak and RMS meters. `docs/very-basic-galley-concept.jpg` only sketches the gallery layout; its meter scales are wrong.

## Build and test

Visual Studio 2022 or .NET SDK 8/9. Only **x64 Debug/Release** exist. All output goes to `bin\x64\<Configuration>\` at the repo root.

```
dotnet build Audio-Centric-Controls.sln -c Debug          # or -c Release (warnings are errors)
dotnet test  Audio-Centric-Controls.sln
dotnet test  tests/AudioCentric.Controls.Core.Tests --filter "FullyQualifiedName~FaderTaperTests"
bin\x64\Debug\AudioControlsGallery.Wpf.exe                  # live gallery
bin\x64\Debug\AudioControlsGallery.Wpf.exe --screenshots <dir>   # saves a PNG per gallery page, then exits
```

`Directory.Build.props` sets the TFM (`net8.0-windows`; Core overrides it to plain `net8.0`), forces Platform=x64 when it is empty or AnyCPU, and sets OutputPath. WPF tests save renders of the controls when `ACC_RENDER_DIR` is set. Use that, or `--screenshots`, to check artwork visually after rendering changes.

## Architecture

- `src/AudioCentric.Controls.Core` (namespace `AudioCentric.Controls.*`) has **no WPF, audio or DSP dependency**. It holds everything a future renderer would reuse:
  - `Scales/`: `IValueMapping` (Linear, Log, Amplitude for VU, PiecewiseLinear), `FaderTaper`, `ArcGeometry`, `ScaleDefinition`, `LedScaleDefinition`, `ScalePresets`
  - `Dynamics/`: display-only `NeedleBallistics`, `PeakHoldTracker`, `DecayFollower`, `OverloadLatch`
  - `Input/`: `ParameterModel`, `ToggleStateModel`
  - `Plotting/`: `MinMaxBucketer`
  - `Measurements/`: `Measurement`, `Decibels`, snapshot records
- `src/AudioCentric.Controls.Wpf` (controls in `...Wpf.Controls`, XAML namespace `https://github.com/FMock/audio-centric-controls`, prefix `acc`) holds the renderers.
- `examples/AudioControlsGallery.Wpf` is the live gallery. `Simulation/` plays the **host**: a background thread synthesizes 48 kHz stereo and computes calibrated VU, peak, RMS and correlation. `GalleryViewModel` pulls results at frame rate and binds them to the controls.

### Boundaries (from the design doc)

- Controls display ready-made measurements and publish parameter *requests*. They never touch audio. Calibrated VU dynamics, RMS windows and correlation integration belong to the provider (the gallery's `Simulation/`). Display smoothing, peak hold, LED decay, needle inertia and overload latch/reset belong to Core/Wpf. Never double-smooth a calibrated value (`NeedleDynamicsMode.None`).
- Silence is `double.NegativeInfinity`; invalid is NaN or `IsValueValid=false`. Keep the two distinct.
- The host's peak readings must be aggregated across blocks until the UI reads them (see `SimulatedMeterSource.ReadPeakDbfs`). Overload flags may last a single frame, so controls latch on the rising edge.

### Rendering conventions (all controls)

- Controls derive from `FrameworkElement` and own a `VisualCollection` of `DrawingVisual` layers drawn in a fixed **design space** (`AnalogMeter` 1000×1000, `RotaryKnob` 200×200, `ConsoleFader` 260 across × variable length). One uniform `MatrixTransform` scales it to the arranged size. Never stretch bitmaps. Static layers are redrawn only on property, size or DPI change. Per frame, only transforms change (needle `RotateTransform`, fader cap offset) or one small visual is redrawn (LED segments, only when the lit count changes).
- Texture is `VintageArt.GrainBrush`: a procedural noise tile at one texel per device pixel, so it never pixelates. Shared artwork (screws, lamps, text helpers) is in `Rendering/VintageArt.cs`.
- Animation goes through `RenderScheduler` (one `CompositionTarget.Rendering` subscription). Controls register on Loaded and unregister on Unloaded. When not loaded (tests, `RenderTargetBitmap`), value changes apply immediately.
- `MeasureOverride` must never return more than the constraint. WPF would otherwise arrange at the larger size and clip.
- `IlluminatedToggle` is a `ToggleButton` drawn in `OnRender`; its template comes from `Themes/Generic.xaml`. The other controls need no template.
- Every control has an AutomationPeer (RangeValue or Toggle) and keyboard support for inputs.

### Gallery rule

Add every new control to the gallery as soon as it works. Register a page in `MainWindow.xaml.cs` (`_pages`), or add it to an existing page in `Views/`, and feed it from `Simulation/`.

## Roadmap status

Done: scaffold, Core foundations, AnalogMeter + overload lamp, LedBarMeter, RotaryKnob, ConsoleFader, IlluminatedToggle, gallery (Overview / Meters / Inputs / Scaling).
Next: WaveformDisplay + Oscilloscope, then SpectrumAnalyzer, FrequencyResponseGraph, CorrelationMeter (a "Displays" gallery page), then docs and NuGet packaging (`dotnet pack` for Core and Wpf).
