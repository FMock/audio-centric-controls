using AudioCentric.Controls.Plotting;

namespace AudioCentric.Controls.Core.Tests;

public class WaveformAndScopeTests
{
    private static float[] Noise(int n, int seed)
    {
        var r = new Random(seed);
        return Enumerable.Range(0, n).Select(_ => (float)(r.NextDouble() * 2 - 1)).ToArray();
    }

    [Fact]
    public void Pyramid_MatchesBruteForceForArbitraryRanges()
    {
        var x = Noise(100_003, 1);
        var p = new MinMaxPyramid(x);
        var r = new Random(2);
        for (int k = 0; k < 500; k++)
        {
            int a = r.Next(x.Length), b = r.Next(x.Length);
            int s = Math.Min(a, b), e = Math.Max(a, b) + 1;
            var (lo, hi) = p.Range(s, e);
            Assert.Equal(x.AsSpan(s, e - s).ToArray().Min(), lo);
            Assert.Equal(x.AsSpan(s, e - s).ToArray().Max(), hi);
        }
        Assert.True(p.LevelCount > 3);
    }

    [Fact]
    public void Pyramid_EmptyRangeIsNaN()
    {
        var p = new MinMaxPyramid(new float[] { 1, 2, 3 });
        Assert.True(float.IsNaN(p.Range(2, 2).Lo));
    }

    [Fact]
    public void InMemorySource_MatchesBucketerAndPreservesImpulse()
    {
        var x = new float[480_000];
        x[123_457] = -0.8f;
        var src = new InMemoryWaveformSource(48000, x);
        var lo1 = new float[700]; var hi1 = new float[700];
        var lo2 = new float[700]; var hi2 = new float[700];
        src.GetMinMax(0, 1000, 470_000, lo1, hi1);
        MinMaxBucketer.Reduce(x, 1000, 470_000, lo2, hi2);
        Assert.Equal(lo2, lo1);
        Assert.Equal(hi2, hi1);
        Assert.Equal(-0.8f, lo1.Min());
    }

    private static float[] Sine(int n, double cyclesPerSample, double phase = 0) =>
        Enumerable.Range(0, n).Select(i => (float)Math.Sin(2 * Math.PI * cyclesPerSample * i + phase)).ToArray();

    [Fact]
    public void Trigger_RisingZeroCrossingLandsAtTriggerPosition()
    {
        var s = Sine(4800, 1000.0 / 48000, 0.7);
        var t = new ScopeTrigger { Mode = TriggerMode.Normal, Level = 0, TriggerPosition = 0.5 };
        int visible = 480;
        int start = t.Select(s, 0, visible);
        Assert.Equal(ScopeStatus.Triggered, t.Status);
        int trig = start + visible / 2;
        Assert.True(s[trig] >= 0 && s[trig - 1] < 0, "trigger sample is a rising zero crossing");
        Assert.True(start + visible <= s.Length);
    }

    [Fact]
    public void Trigger_FallingSlope()
    {
        var s = Sine(4800, 0.01);
        var t = new ScopeTrigger { Mode = TriggerMode.Normal, Slope = TriggerSlope.Falling, Level = 0.5f, TriggerPosition = 0.25 };
        int start = t.Select(s, 0, 400);
        int trig = start + 100;
        Assert.True(s[trig] <= 0.5f && s[trig - 1] > 0.5f);
    }

    [Fact]
    public void Trigger_HysteresisRejectsNoiseAroundLevel()
    {
        // Small chatter around 0 never crosses the arm band, so nothing triggers.
        var s = Enumerable.Range(0, 2000).Select(i => (float)(0.005 * Math.Sin(i))).ToArray();
        var t = new ScopeTrigger { Mode = TriggerMode.Normal, Level = 0, Hysteresis = 0.02f };
        Assert.Equal(-1, t.Select(s, 0, 200));
        Assert.Equal(ScopeStatus.Waiting, t.Status);
    }

    [Fact]
    public void Trigger_AutoFreeRunsWithoutSignalAndNormalWaits()
    {
        var silence = new float[2000];
        var auto = new ScopeTrigger { Mode = TriggerMode.Auto };
        Assert.Equal(2000 - 300, auto.Select(silence, 0, 300));
        Assert.Equal(ScopeStatus.FreeRunning, auto.Status);
        var normal = new ScopeTrigger { Mode = TriggerMode.Normal };
        Assert.Equal(-1, normal.Select(silence, 0, 300));
    }

    [Fact]
    public void Trigger_SingleStopsUntilArmed()
    {
        var s = Sine(4800, 0.005);
        var t = new ScopeTrigger { Mode = TriggerMode.Single };
        Assert.True(t.Select(s, 0, 400) >= 0);
        Assert.Equal(-1, t.Select(s, 4800, 400));
        Assert.Equal(ScopeStatus.Stopped, t.Status);
        t.Arm();
        Assert.True(t.Select(s, 9600, 400) >= 0);
    }

    [Fact]
    public void Trigger_HoldoffSkipsTriggersTooSoon()
    {
        var s = Sine(4800, 0.01);
        var t = new ScopeTrigger { Mode = TriggerMode.Normal, HoldoffSamples = 10_000 };
        Assert.True(t.Select(s, 0, 400) >= 0);
        Assert.Equal(-1, t.Select(s, 100, 400)); // same signal, history shifted slightly: within holdoff
        Assert.True(t.Select(s, 20_000, 400) >= 0);
    }

    [Fact]
    public void Steps125_Sequence()
    {
        Assert.Equal(new[] { 0.0001, 0.0002, 0.0005, 0.001, 0.002, 0.005, 0.01 }, Steps125.Range(0.0001, 0.01));
        Assert.Equal("500 µs", Steps125.FormatSeconds(0.0005));
        Assert.Equal("2 ms", Steps125.FormatSeconds(0.002));
    }
}
