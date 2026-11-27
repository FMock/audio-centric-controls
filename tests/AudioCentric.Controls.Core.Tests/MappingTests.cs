using AudioCentric.Controls.Measurements;
using AudioCentric.Controls.Scales;

namespace AudioCentric.Controls.Core.Tests;

public class MappingTests
{
    [Fact]
    public void LogMapping_20Hz_20kHz_MidpointIsAbout632Hz()
    {
        var m = new LogMapping(20, 20000);
        Assert.Equal(632.46, m.FromNormalized(0.5), 2);
        Assert.Equal(20, m.FromNormalized(0), 9);
        Assert.Equal(20000, m.FromNormalized(1), 6);
        Assert.Equal(0.5, m.ToNormalized(632.455532), 6);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.123)]
    [InlineData(0.5)]
    [InlineData(0.987)]
    [InlineData(1.0)]
    public void Mappings_RoundTrip(double u)
    {
        IValueMapping[] maps =
        {
            new LinearMapping(-60, 6),
            new LogMapping(20, 20000),
            new AmplitudeMapping(3),
            ScalePresets.PeakDbfs.Mapping,
            ScalePresets.RmsDbfs.Mapping,
        };
        foreach (var m in maps)
            Assert.Equal(u, m.ToNormalized(m.FromNormalized(u)), 9);
    }

    [Fact]
    public void Mappings_ClampOutOfRange()
    {
        var m = new LinearMapping(-60, 0);
        Assert.Equal(0, m.ToNormalized(-100));
        Assert.Equal(1, m.ToNormalized(12));
        Assert.Equal(0, m.ToNormalized(double.NegativeInfinity));
        Assert.Equal(0, m.ToNormalized(double.NaN));
    }

    [Fact]
    public void VuScale_ZeroVuIsAbout71PercentDeflection()
    {
        var vu = ScalePresets.Vu;
        Assert.Equal(1 / Math.Pow(10, 3.0 / 20), vu.ToNormalized(0), 9);
        Assert.InRange(vu.ToNormalized(0), 0.70, 0.71);
        Assert.Equal(0, vu.ToNormalized(double.NegativeInfinity));
        Assert.Equal(1, vu.ToNormalized(3), 9);
    }

    [Fact]
    public void VuScale_PercentageDotsAlignWithAmplitude()
    {
        var vu = ScalePresets.Vu;
        // "100" sits exactly on 0 VU; "50" at -6.02 VU.
        var hundred = vu.SecondaryTicks.Single(t => t.Label == "100");
        Assert.Equal(0, hundred.Value, 9);
        Assert.Equal(-6.0206, vu.SecondaryTicks[5].Value, 3);
        Assert.True(double.IsNegativeInfinity(vu.SecondaryTicks[0].Value));
    }

    [Fact]
    public void ScalePresets_AreMonotonicAndHaveRedZones()
    {
        foreach (var s in new[] { ScalePresets.Vu, ScalePresets.PeakDbfs, ScalePresets.PeakDbfsPcm, ScalePresets.RmsDbfs })
        {
            double prev = -1;
            foreach (var t in s.MajorTicks)
            {
                double u = s.ToNormalized(t.Value);
                Assert.True(u > prev, $"{s.Title} tick {t.Label} not increasing");
                prev = u;
            }
            Assert.Contains(s.Zones, z => z.Kind == ZoneKind.Danger);
        }
        Assert.Equal(ZoneKind.Danger, ScalePresets.PeakDbfs.ZoneAt(3));
        Assert.Equal(ZoneKind.Normal, ScalePresets.PeakDbfs.ZoneAt(-6));
    }

    [Fact]
    public void RmsScale_HasNoDuplicateLabels()
    {
        var labels = ScalePresets.RmsDbfs.MajorTicks.Select(t => t.Label).ToList();
        Assert.Equal(labels.Count, labels.Distinct().Count());
    }

    [Fact]
    public void PeakScale_IncludesOverRangeMarks()
    {
        var labels = ScalePresets.PeakDbfs.MajorTicks.Select(t => t.Label).ToList();
        Assert.Contains("+3", labels);
        Assert.Contains("+6", labels);
    }

    [Fact]
    public void Decibels_ReferenceValues()
    {
        Assert.Equal(1.995, Decibels.ToAmplitude(6), 3);
        Assert.Equal(0.501, Decibels.ToAmplitude(-6), 3);
        Assert.Equal(-6.02, Decibels.FromAmplitude(0.5), 2);
        Assert.Equal(-20.0, Decibels.FromAmplitude(0.1), 9);
        Assert.True(double.IsNegativeInfinity(Decibels.FromAmplitude(0)));
        Assert.Equal(0, Decibels.ToAmplitude(double.NegativeInfinity));
    }

    [Fact]
    public void ArcGeometry_MapsEndsAndPoints()
    {
        var arc = new ArcGeometry(-50, 50);
        Assert.Equal(-50, arc.AngleFor(0));
        Assert.Equal(0, arc.AngleFor(0.5));
        Assert.Equal(50, arc.AngleFor(2));
        var (x, y) = ArcGeometry.PointAt(0, 0, 10, 0);
        Assert.Equal(0, x, 9);
        Assert.Equal(-10, y, 9);
        (x, y) = ArcGeometry.PointAt(0, 0, 10, 90);
        Assert.Equal(10, x, 9);
        Assert.Equal(0, y, 9);
    }
}
