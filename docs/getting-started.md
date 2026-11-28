# Getting started

## Requirements

- Windows, .NET 8 or later (`net8.0-windows`), WPF.
- Any processor architecture. The library assemblies are AnyCPU. This repository builds them as part of its x64-only solution.

## Install

From a package feed:

```
dotnet add package AudioCentric.Controls.Wpf
```

`AudioCentric.Controls.Wpf` depends on `AudioCentric.Controls.Core`, which comes along automatically. To use packages you built yourself, see [packaging.md](packaging.md). To work on the library, reference the projects directly, as the gallery does:

```xml
<ProjectReference Include="..\..\src\AudioCentric.Controls.Wpf\AudioCentric.Controls.Wpf.csproj" />
```

## The XAML namespace

All controls are in one XML namespace, prefix `acc`:

```xml
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:acc="https://github.com/FMock/audio-centric-controls">
```

In C# the controls are in `AudioCentric.Controls.Wpf.Controls`. Scales, snapshots, band models and the like are in the `AudioCentric.Controls.*` namespaces of the Core package.

## A first meter and knob

```xml
<StackPanel Orientation="Horizontal">
    <acc:AnalogMeter MeterType="Vu" Width="260" Height="260"
                     Value="{Binding VuLeft}" IsOverloaded="{Binding Overload}" />
    <acc:RotaryKnob Width="90" Height="90" Minimum="-24" Maximum="24" Detent="0" Step="0.5" Unit="dB"
                    Value="{Binding TrimDb}" />
</StackPanel>
```

```csharp
public sealed class ChannelViewModel : INotifyPropertyChanged
{
    // Written by your metering code at frame rate (see host-integration.md).
    public double VuLeft { get; set; } = double.NegativeInfinity;   // VU, −∞ = silence
    public bool Overload { get; set; }                               // over-range flag; the meter latches the lamp

    // Written by the knob (two-way by default); apply it to your engine with smoothing.
    public double TrimDb { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}
```

That is the whole model. **Measurements go in, requests come out.** The meter never computes a VU reading; it shows the one you give it, with realistic needle motion. The knob never changes the gain; it publishes the value the user asked for, and your engine applies it.

## Sizing and DPI

Every control is vector-drawn and scales without loss: set `Width` and `Height`, or let the layout stretch it.

- **Dial instruments** keep their proportions and centre themselves in the space they get: `AnalogMeter` (square), `RotaryKnob` (square), `Oscilloscope` (1000:820) and `CorrelationMeter` (10:3).
- **Plots** fill any rectangle and keep their text at a readable size: `WaveformDisplay`, `SpectrumAnalyzer` and `FrequencyResponseGraph`.
- **Strips** have a fixed cross-axis proportion and a variable length: `ConsoleFader` and `LedBarMeter`.

Moving a window between monitors with different DPI rebuilds the cached artwork at the new resolution.

## Next

- [host-integration.md](host-integration.md) explains how to feed the controls correctly from an audio thread.
- [controls.md](controls.md) has an example for every control.
- The gallery (`examples/AudioControlsGallery.Wpf`) shows them all running.
