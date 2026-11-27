using AudioCentric.Controls.Dynamics;

namespace AudioCentric.Controls.Core.Tests;

public class DynamicsTests
{
    [Fact]
    public void PeakHold_HoldsThenDecaysAt20DbPerSecond()
    {
        var p = new PeakHoldTracker { HoldSeconds = 2, DecayDbPerSecond = 20 };
        p.Update(-6, 0);
        p.Update(-40, 1.0);
        Assert.Equal(-6, p.Value);       // still holding
        p.Update(-40, 1.5);              // 1 s hold left, then 0.5 s decay
        Assert.Equal(-16, p.Value, 9);
        p.Update(-40, 10);
        Assert.Equal(-40, p.Value, 9);   // never below the live level
    }

    [Fact]
    public void PeakHold_NewMaximumIsImmediate()
    {
        var p = new PeakHoldTracker();
        p.Update(-30, 0.01);
        Assert.Equal(-1, p.Update(-1, 0.01));
    }

    [Fact]
    public void DecayFollower_ImmediateAttackLinearRelease()
    {
        var d = new DecayFollower { ReleaseDbPerSecond = 20 };
        Assert.Equal(-3, d.Update(-3, 0.016));
        Assert.Equal(-5, d.Update(-60, 0.1), 9);
        Assert.Equal(-1, d.Update(-1, 0.1));
    }

    [Fact]
    public void Needle_NoneModeTracksExactly()
    {
        var n = new NeedleBallistics { Mode = NeedleDynamicsMode.None, Target = 0.42 };
        Assert.Equal(0.42, n.Update(0.016));
    }

    [Fact]
    public void Needle_MechanicalSettlesOnTargetIndependentOfFrameRate()
    {
        double Run(double dt)
        {
            var n = new NeedleBallistics { Target = 0.7 };
            for (double t = 0; t < 2.0; t += dt) n.Update(dt);
            return n.Position;
        }
        Assert.Equal(0.7, Run(1 / 60.0), 3);
        Assert.Equal(0.7, Run(1 / 30.0), 3);
        Assert.Equal(Run(1 / 60.0), Run(1 / 144.0), 3);
    }

    [Fact]
    public void Needle_MechanicalOvershootsSlightly()
    {
        var n = new NeedleBallistics { Target = 1.0 };
        double max = 0;
        for (int i = 0; i < 240; i++) max = Math.Max(max, n.Update(1 / 120.0));
        Assert.InRange(max, 1.0, 1.05);
    }

    [Fact]
    public void Latch_Momentary()
    {
        var l = new OverloadLatch { Mode = OverloadLatchMode.Momentary };
        Assert.True(l.Update(true, 0.01));
        Assert.False(l.Update(false, 0.01));
    }

    [Fact]
    public void Latch_HoldTime()
    {
        var l = new OverloadLatch { Mode = OverloadLatchMode.HoldTime, HoldSeconds = 2 };
        l.Update(true, 0.01);
        Assert.True(l.Update(false, 1.5));
        Assert.False(l.Update(false, 0.6));
    }

    [Fact]
    public void Latch_LatchedStaysOnUntilReset()
    {
        var l = new OverloadLatch { Mode = OverloadLatchMode.Latched };
        l.Update(true, 0.01);
        Assert.True(l.Update(false, 100));
        Assert.Equal(1, l.EventCount);
        l.Reset();
        Assert.False(l.IsLit);
        Assert.False(l.Update(false, 0.01));
        Assert.True(l.Update(true, 0.01));   // relights on a new overload
    }

    [Fact]
    public void Latch_CountsRisingEdges()
    {
        var l = new OverloadLatch();
        l.Update(true, 0); l.Update(true, 0); l.Update(false, 0); l.Update(true, 0);
        Assert.Equal(2, l.EventCount);
    }
}
