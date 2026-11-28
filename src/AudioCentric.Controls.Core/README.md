# AudioCentric.Controls.Core

The framework-neutral part of Audio-Centric-Controls (plain `net8.0`, no UI, audio or DSP dependencies). The WPF controls in `AudioCentric.Controls.Wpf` are built on it, and a renderer for another UI framework could reuse it.

- **Measurements**: `Measurement` values with explicit units, `Decibels` helpers, and snapshot records that hosts hand to plot controls (`TraceSnapshot`, `SpectrumSnapshot`, `ResponseSnapshot`). Silence is `−∞`; invalid is NaN.
- **Scales**: value mappings (linear, log, VU amplitude, piecewise), `FaderTaper`, `ArcGeometry`, `ScaleDefinition`/`LedScaleDefinition`, and ready-made `ScalePresets` (VU, peak, true-peak, RMS, LED, correlation).
- **Dynamics** (display only): `NeedleBallistics`, `PeakHoldTracker`, `DecayFollower`, `OverloadLatch`.
- **Input**: `ParameterModel`, toggle state models, and `EqBand` / `EqBandChangeRequest`.
- **Plotting**: min/max waveform reduction (`MinMaxBucketer`, `MinMaxPyramid`), `ScopeTrigger`, frequency axes, fractional-octave bands, spectrum reduction and response helpers.

Documentation: https://github.com/FMock/audio-centric-controls/tree/main/docs
