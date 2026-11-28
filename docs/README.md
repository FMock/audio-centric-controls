# Audio-Centric-Controls documentation

| Document | Read it for |
|---|---|
| [Getting started](getting-started.md) | Installing the packages, the XAML namespace, your first meter and knob |
| [Host integration](host-integration.md) | The contract between your audio engine and the controls: units, silence vs invalid, threading, snapshots, requests and acknowledgement |
| [Control reference](controls.md) | Every control: what it shows, the properties that matter, and a binding example |
| [Packaging and verification](packaging.md) | Building the NuGet packages, the package-consumer check, versioning and publishing |
| [`Audio-Centric-Controls-Public.docx`](Audio-Centric-Controls-Public.docx) | The design reference: per-control formulas, display defaults and verification lists |

Two runnable examples sit in `examples/`:

- **AudioControlsGallery.Wpf** shows every control working on a simulated 48 kHz signal. Its `Simulation/` folder is a complete reference host: calibrated VU, peak with cross-block aggregation, RMS, correlation, FFT analysis and a real biquad EQ. Run `bin\x64\Release\AudioControlsGallery.Wpf.exe`.
- **PackageConsumer.Wpf** is a minimal application that uses the controls only through the NuGet packages. Its `DemoHost.cs` is a deterministic mock provider that shows each integration pattern in about 250 lines.
