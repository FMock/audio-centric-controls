using System.Globalization;
using AudioCentric.Controls.Measurements;

namespace AudioCentric.Controls.Scales;

/// <summary>
/// Printed scales for tuning dials. Each band is a <see cref="ScaleDefinition"/>: <see cref="ScaleDefinition.Title"/> is the
/// short band marker printed at the end of the arc ("1", "×10"), <see cref="ScaleDefinition.Caption"/> is the text printed
/// along the arc, and labelled <see cref="ScaleDefinition.Zones"/> mark sub-bands (e.g. amateur allocations).
/// The mapping gives each frequency's position along the dial's travel (0…1). Tuning capacitors give a roughly
/// logarithmic spread, so the presets use <see cref="LogMapping"/>.
/// </summary>
public static class DialPresets
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    /// <summary>A 0–100 logging scale (linear), printed round the outside of a tuning dial.</summary>
    public static ScaleDefinition LoggingScale { get; } = new()
    {
        Minimum = 0,
        Maximum = 100,
        Mapping = new LinearMapping(0, 100),
        Caption = "LOGGING SCALE",
        Unit = MeasurementUnit.Linear,
        MajorTicks = Enumerable.Range(0, 21).Select(i => new ScaleTick(i * 5, (i * 5).ToString(Ci))).ToArray(),
        MinorTicks = Enumerable.Range(0, 101).Where(i => i % 5 != 0).Select(i => (double)i).ToArray(),
    };

    /// <summary>
    /// A bandspread wheel: 0–100 with a number every 5 units. Printed once round a wheel, 95 is followed by 0.
    /// </summary>
    public static ScaleDefinition Bandspread { get; } = new()
    {
        Minimum = 0,
        Maximum = 100,
        Mapping = new LinearMapping(0, 100),
        Unit = MeasurementUnit.Linear,
        MajorTicks = Enumerable.Range(0, 20).Select(i => new ScaleTick(i * 5, (i * 5).ToString(Ci))).ToArray(),
        MinorTicks = Enumerable.Range(0, 100).Where(i => i % 5 != 0).Select(i => (double)i).ToArray(),
    };

    /// <summary>
    /// A general-coverage shortwave receiver: four bands from the medium-wave broadcast band to 44 MHz,
    /// with the amateur allocations marked.
    /// </summary>
    public static IReadOnlyList<ScaleDefinition> ShortwaveReceiver { get; } = new[]
    {
        Band(0.54, 1.7, "1", "0.54–1.7 MHz", MeasurementUnit.Hz,
            new[] { 0.55, 0.6, 0.7, 0.8, 0.9, 1.0, 1.2, 1.4, 1.7 },
            Step(0.55, 1.7, 0.05), Array.Empty<ScaleZone>(), "0.0#"),
        Band(1.7, 5.4, "2", "1.7–5.4 MHz", MeasurementUnit.Hz,
            new[] { 1.8, 2.0, 2.5, 3.0, 3.5, 4.0, 4.5, 5.0 },
            Step(1.7, 5.4, 0.1), new[] { new ScaleZone(1.8, 2.0, ZoneKind.Good, "160 m"), new ScaleZone(3.5, 4.0, ZoneKind.Good, "80 m") }, "0.0"),
        Band(5.3, 16, "3", "5.3–16 MHz", MeasurementUnit.Hz,
            new[] { 6.0, 7.0, 8.0, 9.0, 10.0, 12.0, 14.0, 16.0 },
            Step(5.5, 16, 0.5), new[] { new ScaleZone(7.0, 7.3, ZoneKind.Good, "40 m"), new ScaleZone(14.0, 14.35, ZoneKind.Good, "20 m") }, "0"),
        Band(15.5, 44, "4", "15.5–44 MHz", MeasurementUnit.Hz,
            new[] { 16.0, 18.0, 20.0, 22.0, 25.0, 30.0, 35.0, 40.0 },
            Step(16, 44, 1), new[] { new ScaleZone(21.0, 21.45, ZoneKind.Good, "15 m"), new ScaleZone(28.0, 29.7, ZoneKind.Good, "10 m") }, "0"),
    };

    /// <summary>A three-range audio oscillator: 20–200 Hz (×1), 200 Hz–2 kHz (×10) and 2–20 kHz (×100).</summary>
    public static IReadOnlyList<ScaleDefinition> AudioOscillator { get; } = new[]
    {
        Band(20, 200, "×1", "20–200 Hz", MeasurementUnit.Hz,
            new[] { 20.0, 25, 30, 40, 50, 60, 80, 100, 150, 200 }, Step(20, 200, 5), Array.Empty<ScaleZone>(), "0"),
        Band(200, 2000, "×10", "200 Hz–2 kHz", MeasurementUnit.Hz,
            new[] { 200.0, 250, 300, 400, 500, 600, 800, 1000, 1500, 2000 }, Step(200, 2000, 50), Array.Empty<ScaleZone>(), "0", k: true),
        Band(2000, 20000, "×100", "2–20 kHz", MeasurementUnit.Hz,
            new[] { 2000.0, 2500, 3000, 4000, 5000, 6000, 8000, 10000, 15000, 20000 }, Step(2000, 20000, 500), Array.Empty<ScaleZone>(), "0", k: true),
    };

    private static double[] Step(double from, double to, double step)
    {
        var list = new List<double>();
        for (double v = from; v <= to + step * 1e-6; v += step) list.Add(Math.Round(v, 6));
        return list.ToArray();
    }

    private static ScaleDefinition Band(double min, double max, string marker, string caption, MeasurementUnit unit,
        double[] labels, double[] minors, ScaleZone[] zones, string format, bool k = false) => new()
    {
        Minimum = min,
        Maximum = max,
        Mapping = new LogMapping(min, max),
        Title = marker,
        Caption = caption,
        Unit = unit,
        MajorTicks = labels.Select(v => new ScaleTick(v, k ? FormatK(v) : v.ToString(format, Ci))).ToArray(),
        MinorTicks = minors.Where(m => m >= min && m <= max && !labels.Any(l => Math.Abs(l - m) < 1e-9)).ToArray(),
        Zones = zones,
    };

    private static string FormatK(double hz) => hz >= 1000 ? (hz / 1000).ToString("0.#", Ci) + "k" : hz.ToString("0", Ci);
}
