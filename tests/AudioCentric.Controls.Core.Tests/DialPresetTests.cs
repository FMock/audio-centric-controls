using AudioCentric.Controls.Scales;

namespace AudioCentric.Controls.Core.Tests;

public class DialPresetTests
{
    public static IEnumerable<object[]> Bands() =>
        DialPresets.ShortwaveReceiver.Concat(DialPresets.AudioOscillator).Select(b => new object[] { b });

    [Theory]
    [MemberData(nameof(Bands))]
    public void BandIsPrintable(ScaleDefinition band)
    {
        Assert.False(string.IsNullOrEmpty(band.Title));
        Assert.False(string.IsNullOrEmpty(band.Caption));
        // Labels lie on the dial and run in order along it.
        var positions = band.MajorTicks.Select(t => band.ToNormalized(t.Value)).ToArray();
        Assert.All(positions, u => Assert.InRange(u, 0, 1));
        Assert.Equal(positions.OrderBy(u => u), positions);
        Assert.All(band.MinorTicks, v => Assert.InRange(v, band.Minimum, band.Maximum));
        Assert.All(band.Zones, z => { Assert.InRange(z.Start, band.Minimum, band.Maximum); Assert.InRange(z.End, z.Start, band.Maximum); });
        // Position ↔ frequency round-trips (the TuningDial relies on it).
        foreach (var u in new[] { 0.0, 0.25, 0.5, 1.0 }) Assert.Equal(u, band.ToNormalized(band.FromNormalized(u)), 9);
    }

    [Fact]
    public void ShortwaveBandsOverlapSoNothingIsUntunable()
    {
        var b = DialPresets.ShortwaveReceiver;
        for (int i = 1; i < b.Count; i++) Assert.True(b[i].Minimum <= b[i - 1].Maximum);
        Assert.Equal(new[] { "1", "2", "3", "4" }, b.Select(x => x.Title));
    }

    [Fact]
    public void LoggingAndBandspreadScalesAreNumberedEveryFive()
    {
        Assert.Equal(21, DialPresets.LoggingScale.MajorTicks.Count);   // 0 … 100
        Assert.Equal(20, DialPresets.Bandspread.MajorTicks.Count);     // 0 … 95, then the wheel wraps to 0
        Assert.Equal("95", DialPresets.Bandspread.MajorTicks[^1].Label);
    }
}
