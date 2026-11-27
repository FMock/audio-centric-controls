using AudioCentric.Controls.Measurements;

namespace AudioCentric.Controls.Scales;

/// <summary>Ready-made scales matching the design reference. All are immutable and safe to share.</summary>
public static class ScalePresets
{
    /// <summary>
    /// Classic VU: −20…+3 VU, deflection proportional to amplitude (0 VU ≈ 71% / "100"),
    /// red zone 0…+3, secondary 0–100 % dot scale, "−" and "+" end glyphs.
    /// </summary>
    public static ScaleDefinition Vu { get; } = new()
    {
        Minimum = -20,
        Maximum = 3,
        Mapping = new AmplitudeMapping(3),
        Title = "VU",
        Caption = "",
        Unit = MeasurementUnit.Vu,
        LowEndGlyph = "−",
        HighEndGlyph = "+",
        MajorTicks = new ScaleTick[]
        {
            new(-20, "20"), new(-10, "10"), new(-7, "7"), new(-5, "5"), new(-3, "3"),
            new(-2, "2"), new(-1, "1"), new(0, "0"), new(1, "1"), new(2, "2"), new(3, "3"),
        },
        MinorTicks = new[] { -4.0, -1.5, -0.5, 0.5, 1.5, 2.5 },
        Zones = new[] { new ScaleZone(0, 3, ZoneKind.Danger) },
        SecondaryTicks = Enumerable.Range(0, 11)
            .Select(i => new ScaleTick(Decibels.FromAmplitude(i / 10.0), i % 2 == 0 ? (i * 10).ToString(System.Globalization.CultureInfo.InvariantCulture) : null))
            .ToArray(),
    };

    // Position anchors give more room near full scale than uniform dB, like the reference artwork.
    private static readonly (double, double)[] FloatPeakAnchors =
    {
        (-60, 0.00), (-50, 0.09), (-40, 0.19), (-30, 0.30), (-20, 0.43), (-10, 0.58),
        (-6, 0.66), (-3, 0.74), (0, 0.83), (3, 0.915), (6, 1.00),
    };

    private static readonly (double, double)[] FullScaleAnchors =
    {
        (-60, 0.00), (-50, 0.10), (-40, 0.21), (-30, 0.33), (-20, 0.47), (-10, 0.64),
        (-6, 0.74), (-3, 0.85), (0, 1.00),
    };

    private static double[] MinorTicksFor(double min, double max, IEnumerable<ScaleTick> majors)
    {
        var majorValues = majors.Select(t => t.Value).ToHashSet();
        var list = new List<double>();
        for (double v = min; v <= max + 1e-9; v += v < -10 - 1e-9 ? 2 : 1)
        {
            double r = Math.Round(v, 6);
            if (!majorValues.Contains(r)) list.Add(r);
        }
        return list.ToArray();
    }

    private static readonly ScaleTick[] PeakFloatMajors =
    {
        new(-60, "-60"), new(-50, "-50"), new(-40, "-40"), new(-30, "-30"), new(-20, "-20"),
        new(-10, "-10"), new(-6, "-6"), new(-3, "-3"), new(0, "0"), new(3, "+3"), new(6, "+6"),
    };

    private static readonly ScaleTick[] FullScaleMajors =
    {
        new(-60, "-60"), new(-50, "-50"), new(-40, "-40"), new(-30, "-30"), new(-20, "-20"),
        new(-10, "-10"), new(-6, "-6"), new(-3, "-3"), new(0, "0"),
    };

    /// <summary>
    /// Sample-peak meter for floating-point signals: −60…+6 dBFS, red over-range zone 0…+6.
    /// Pair with an overload lamp; a full-scale reading is a warning, not proof of clipping.
    /// </summary>
    public static ScaleDefinition PeakDbfs { get; } = new()
    {
        Minimum = -60,
        Maximum = 6,
        Mapping = new PiecewiseLinearMapping(FloatPeakAnchors),
        Title = "PEAK",
        Caption = "dBFS",
        Unit = MeasurementUnit.DbFs,
        MajorTicks = PeakFloatMajors,
        MinorTicks = MinorTicksFor(-60, 6, PeakFloatMajors),
        Zones = new[] { new ScaleZone(0, 6, ZoneKind.Danger) },
    };

    /// <summary>Sample-peak meter for fixed-point PCM: −60…0 dBFS, red warning zone −3…0.</summary>
    public static ScaleDefinition PeakDbfsPcm { get; } = new()
    {
        Minimum = -60,
        Maximum = 0,
        Mapping = new PiecewiseLinearMapping(FullScaleAnchors),
        Title = "PEAK",
        Caption = "dBFS",
        Unit = MeasurementUnit.DbFs,
        MajorTicks = FullScaleMajors,
        MinorTicks = MinorTicksFor(-60, 0, FullScaleMajors),
        Zones = new[] { new ScaleZone(-3, 0, ZoneKind.Danger) },
    };

    /// <summary>True-peak meter: same layout as <see cref="PeakDbfs"/>, labelled dBTP.</summary>
    public static ScaleDefinition TruePeakDbtp { get; } = new()
    {
        Minimum = -60,
        Maximum = 6,
        Mapping = new PiecewiseLinearMapping(FloatPeakAnchors),
        Title = "PEAK",
        Caption = "dBTP",
        Unit = MeasurementUnit.DbTp,
        MajorTicks = PeakFloatMajors,
        MinorTicks = MinorTicksFor(-60, 6, PeakFloatMajors),
        Zones = new[] { new ScaleZone(0, 6, ZoneKind.Danger) },
    };

    /// <summary>RMS meter: −60…0 dBFS, red level-warning zone −3…0 (a warning, not a clip indicator).</summary>
    public static ScaleDefinition RmsDbfs { get; } = new()
    {
        Minimum = -60,
        Maximum = 0,
        Mapping = new PiecewiseLinearMapping(FullScaleAnchors),
        Title = "RMS",
        Caption = "dBFS",
        Unit = MeasurementUnit.DbFs,
        MajorTicks = FullScaleMajors,
        MinorTicks = MinorTicksFor(-60, 0, FullScaleMajors),
        Zones = new[] { new ScaleZone(-3, 0, ZoneKind.Danger) },
    };

    /// <summary>LED peak thresholds from the design reference: amber above −12, red above −3 dBFS.</summary>
    public static LedScaleDefinition LedPeak { get; } =
        new(new[] { -60.0, -50, -40, -30, -24, -18, -12, -9, -6, -3, 0 }, -12.5, -3.5);

    /// <summary>A denser 24-segment LED scale for console-style bars (−60…0 dBFS), denser near full scale.</summary>
    public static LedScaleDefinition LedPeakDense { get; } =
        new(new[]
        {
            -60.0, -54, -48, -42, -36, -32, -28, -24, -21, -18, -16, -14,
            -12, -10, -8, -7, -6, -5, -4, -3, -2, -1, -0.5, 0,
        }, -12.5, -3.5);
}
