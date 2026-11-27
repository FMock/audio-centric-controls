using AudioCentric.Controls.Scales;

namespace AudioCentric.Controls.Core.Tests;

public class FaderTaperTests
{
    private readonly FaderTaper _taper = FaderTaper.Default;

    [Theory]
    [InlineData(0.05, -60)]
    [InlineData(0.25, -30)]
    [InlineData(0.50, -12)]
    [InlineData(0.75, 0)]
    [InlineData(1.00, 10)]
    public void Anchors(double position, double db)
    {
        Assert.Equal(db, _taper.ToDb(position), 9);
        Assert.Equal(position, _taper.ToPosition(db), 9);
    }

    [Fact]
    public void ZeroIsTrueMute()
    {
        Assert.True(double.IsNegativeInfinity(_taper.ToDb(0)));
        Assert.Equal(0, _taper.ToGain(0));
        Assert.Equal(0, _taper.ToPosition(double.NegativeInfinity));
    }

    [Fact]
    public void UnityGainIsOne()
    {
        Assert.Equal(1.0, _taper.ToGain(0.75), 9);
    }

    [Fact]
    public void IsMonotonicAndContinuousAtRampBoundary()
    {
        double prev = double.NegativeInfinity;
        for (int i = 1; i <= 1000; i++)
        {
            double db = _taper.ToDb(i / 1000.0);
            Assert.True(db > prev);
            prev = db;
        }
        double below = _taper.ToDb(0.05 - 1e-9);
        Assert.Equal(-60, below, 4);
    }

    [Fact]
    public void BottomRampIsLinearInAmplitude()
    {
        double gTop = _taper.ToGain(0.05);
        Assert.Equal(gTop / 2, _taper.ToGain(0.025), 12);
    }

    [Theory]
    [InlineData(0.01)]
    [InlineData(0.3)]
    [InlineData(0.66)]
    [InlineData(0.9)]
    public void InverseRoundTrips(double p)
    {
        Assert.Equal(p, _taper.ToPosition(_taper.ToDb(p)), 9);
    }
}
