namespace AudioCentric.Controls.Scales;

/// <summary>Semantic zone color; the renderer chooses the actual brushes.</summary>
public enum ZoneKind
{
    Normal,
    Warning,
    Danger,
}

/// <summary>A tick at a value in engineering units, optionally labelled.</summary>
public readonly record struct ScaleTick(double Value, string? Label = null);

/// <summary>A highlighted band of the scale (e.g. the red 0..+3 VU region).</summary>
public readonly record struct ScaleZone(double Start, double End, ZoneKind Kind);

/// <summary>
/// Framework-neutral description of a meter scale: range, mapping, ticks, zones and captions.
/// Renderers draw everything from this through one <see cref="IValueMapping"/> and an <see cref="ArcGeometry"/>.
/// </summary>
public sealed class ScaleDefinition
{
    public required double Minimum { get; init; }
    public required double Maximum { get; init; }
    public required IValueMapping Mapping { get; init; }

    public IReadOnlyList<ScaleTick> MajorTicks { get; init; } = Array.Empty<ScaleTick>();
    public IReadOnlyList<double> MinorTicks { get; init; } = Array.Empty<double>();
    public IReadOnlyList<ScaleZone> Zones { get; init; } = Array.Empty<ScaleZone>();

    /// <summary>Optional secondary scale drawn as dots inside the main arc (e.g. the VU percentage scale).</summary>
    public IReadOnlyList<ScaleTick> SecondaryTicks { get; init; } = Array.Empty<ScaleTick>();

    /// <summary>Large legend on the face, e.g. "VU", "PEAK", "RMS".</summary>
    public string Title { get; init; } = "";

    /// <summary>Small caption under the title, e.g. "dBFS". Empty for none.</summary>
    public string Caption { get; init; } = "";

    /// <summary>Optional glyphs at the scale ends ("−" / "+").</summary>
    public string? LowEndGlyph { get; init; }
    public string? HighEndGlyph { get; init; }

    /// <summary>Unit used for readouts.</summary>
    public Measurements.MeasurementUnit Unit { get; init; } = Measurements.MeasurementUnit.DbFs;

    public double ToNormalized(double value) => Mapping.ToNormalized(value);
    public double FromNormalized(double normalized) => Mapping.FromNormalized(normalized);

    /// <summary>The most severe zone containing <paramref name="value"/>.</summary>
    public ZoneKind ZoneAt(double value)
    {
        var kind = ZoneKind.Normal;
        foreach (var z in Zones)
            if (value >= z.Start && value <= z.End && z.Kind > kind) kind = z.Kind;
        return kind;
    }
}
