using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using AudioCentric.Controls.Dynamics;
using AudioCentric.Controls.Scales;
using AudioCentric.Controls.Wpf.Controls;

namespace AudioCentric.Controls.Wpf.Tests;

public class AnalogMeterTests
{
    [Theory]
    [InlineData(AnalogMeterType.Vu, 0.0)]
    [InlineData(AnalogMeterType.Peak, -6.0)]
    [InlineData(AnalogMeterType.Rms, -6.0)]
    public void RendersAtCommonDpis(AnalogMeterType type, double value)
    {
        RenderHelper.RunSta(() =>
        {
            foreach (var dpi in new[] { 96.0, 144, 192, 288 })
            {
                var meter = new AnalogMeter { MeterType = type, Value = value, NeedleDynamics = NeedleDynamicsMode.None };
                var bmp = RenderHelper.Render(meter, new Size(400, 400), dpi);
                Assert.Equal((int)(400 * dpi / 96), bmp.PixelWidth);
                Assert.True(RenderHelper.InkCoverage(bmp) > 0.5, "meter should cover most of its square");
                RenderHelper.MaybeSave(bmp, $"analog-{type}-{dpi}");
            }
        });
    }

    [Fact]
    public void NeedleAngleFollowsSharedArcMapping()
    {
        RenderHelper.RunSta(() =>
        {
            var meter = new AnalogMeter { MeterType = AnalogMeterType.Vu, NeedleDynamics = NeedleDynamicsMode.None };
            var arc = AnalogMeter.NeedleArc;
            meter.Value = 3;
            Assert.Equal(arc.EndAngle, meter.NeedleAngle, 6);
            meter.Value = 0;
            Assert.Equal(arc.AngleFor(ScalePresets.Vu.ToNormalized(0)), meter.NeedleAngle, 6);
            meter.Value = double.NegativeInfinity;
            Assert.Equal(arc.StartAngle, meter.NeedleAngle, 6);
            meter.Value = 0;
            meter.IsValueValid = false;
            Assert.Equal(arc.StartAngle, meter.NeedleAngle, 6);
        });
    }

    [Fact]
    public void OverloadLampLatchesUntilReset()
    {
        RenderHelper.RunSta(() =>
        {
            var meter = new AnalogMeter { MeterType = AnalogMeterType.Peak, OverloadLatchMode = OverloadLatchMode.Latched };
            int resets = 0;
            meter.OverloadReset += (_, _) => resets++;
            meter.IsOverloaded = true;
            meter.IsOverloaded = false;
            Assert.True(meter.IsOverloadLit);
            meter.ResetOverload();
            Assert.False(meter.IsOverloadLit);
            Assert.Equal(1, resets);
        });
    }

    [Fact]
    public void ExposesReadOnlyRangeValue()
    {
        RenderHelper.RunSta(() =>
        {
            var meter = new AnalogMeter { MeterType = AnalogMeterType.Peak, Value = -12 };
            var peer = UIElementAutomationPeer.CreatePeerForElement(meter);
            var range = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue);
            Assert.True(range.IsReadOnly);
            Assert.Equal(-12, range.Value);
            Assert.Equal(-60, range.Minimum);
            Assert.Equal(6, range.Maximum);
        });
    }
}
