using System.Windows;
using AudioCentric.Controls.Measurements;
using AudioCentric.Controls.Plotting;
using AudioCentric.Controls.Wpf.Controls;

namespace AudioCentric.Controls.Wpf.Tests;

public class DisplayControlTests
{
    private static float[] Tone(int n, double hz, int rate, double amp, double phase = 0) =>
        Enumerable.Range(0, n).Select(i => (float)(amp * Math.Sin(2 * Math.PI * hz * i / rate + phase))).ToArray();

    [Fact]
    public void Waveform_RendersStereoAndZooms()
    {
        RenderHelper.RunSta(() =>
        {
            const int rate = 48000;
            var l = Tone(rate * 3, 3, rate, 0.8);
            var r = Tone(rate * 3, 220, rate, 0.3);
            for (int i = 0; i < l.Length; i++) l[i] *= (float)(0.5 + 0.5 * Math.Sin(i * 0.0007));
            l[100_000] = 0.99f; // single impulse must survive the overview
            var w = new WaveformDisplay
            {
                Source = new InMemoryWaveformSource(rate, l, r),
                Playhead = rate * 1.2,
                SelectionStart = rate * 0.5,
                SelectionEnd = rate * 0.9,
            };
            var bmp = RenderHelper.Render(w, new Size(700, 240), 144);
            Assert.True(RenderHelper.InkCoverage(bmp) > 0.9);
            RenderHelper.MaybeSave(bmp, "waveform-144");

            w.Zoom(0.001, rate * 1.0);
            var (s, e) = w.EffectiveView;
            Assert.InRange(e - s, 8, 200);
            Assert.InRange(s, rate * 1.0 - 200, rate * 1.0);
            var zoomed = RenderHelper.Render(w, new Size(700, 240), 144);
            RenderHelper.MaybeSave(zoomed, "waveform-zoomed-144");

            w.ZoomToFit();
            Assert.Equal((0.0, (double)l.Length), w.EffectiveView);
        });
    }

    [Fact]
    public void Scope_TriggersAndRenders()
    {
        RenderHelper.RunSta(() =>
        {
            const int rate = 48000;
            var scope = new Oscilloscope { TimePerDivision = 0.0005, AmplitudePerDivision = 0.25, TriggerMode = TriggerMode.Normal };
            var h1 = Tone(rate / 10, 1000, rate, 0.6, 0.3);
            var h2 = Tone(rate / 10, 2000, rate, 0.3);
            scope.Trace2 = new TraceSnapshot(rate, 0, h2);
            scope.Trace = new TraceSnapshot(rate, 0, h1);
            Assert.Equal(ScopeStatus.Triggered, scope.Status);
            var bmp = RenderHelper.Render(scope, new Size(600, 492), 144);
            RenderHelper.MaybeSave(bmp, "scope-144");
            Assert.True(RenderHelper.InkCoverage(bmp) > 0.4); // the dark bezel is close to the test background
        });
    }

    [Fact]
    public void Scope_NormalModeWaitsOnSilenceAndFreezeIgnoresData()
    {
        RenderHelper.RunSta(() =>
        {
            var scope = new Oscilloscope { TriggerMode = TriggerMode.Normal };
            scope.Trace = new TraceSnapshot(48000, 0, new float[9600]);
            Assert.Equal(ScopeStatus.Waiting, scope.Status);
            scope.IsFrozen = true;
            scope.Trace = new TraceSnapshot(48000, 9600, Tone(9600, 1000, 48000, 0.5));
            Assert.Equal(ScopeStatus.Waiting, scope.Status);
        });
    }
}
