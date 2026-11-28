using System.Windows;
using System.Windows.Input;
using AudioCentric.Controls.Input;
using AudioCentric.Controls.Measurements;
using AudioCentric.Controls.Plotting;
using AudioCentric.Controls.Wpf.Controls;

namespace AudioCentric.Controls.Wpf.Tests;

public class AnalysisControlTests
{
    private static SpectrumSnapshot ToneSpectrum(int rate = 48000, int n = 4096, int bin = 100, double db = -6, double floor = -80)
    {
        var f = Enumerable.Range(0, n / 2 + 1).Select(k => (double)k * rate / n).ToArray();
        var rng = new Random(5);
        var l = f.Select((x, k) => floor - 12 * Math.Log10(1 + x / 200) + rng.NextDouble() * 6).ToArray();
        l[bin] = db;
        l[bin - 1] = l[bin + 1] = db - 6.02;
        l[0] = -40;
        return new SpectrumSnapshot(rate, 0, f, l) { FftSize = n, Window = "Hann", NoiseBandwidthBins = 1.5 };
    }

    [Fact]
    public void Spectrum_RendersLineAndBars_AndHoldsPeaks()
    {
        RenderHelper.RunSta(() =>
        {
            var s = new SpectrumAnalyzer { Spectrum = ToneSpectrum() };
            var bmp = RenderHelper.Render(s, new Size(700, 280), 144);
            Assert.True(RenderHelper.InkCoverage(bmp) > 0.9);
            RenderHelper.MaybeSave(bmp, "spectrum-line-144");
            Assert.Equal(-6, s.DisplayedLevels.Where(v => !double.IsNaN(v)).Max(), 6);

            s.DisplayMode = SpectrumDisplayMode.Bars;
            Assert.Equal(31, s.Bands.Count);
            var bars = RenderHelper.Render(s, new Size(700, 280), 144);
            RenderHelper.MaybeSave(bars, "spectrum-bars-144");
            Assert.Equal(-6, s.DisplayedLevels.Max(), 6);

            // The tone disappears; the peak marker (held) does not vanish at once.
            s.Spectrum = ToneSpectrum(db: -80);
            Assert.True(s.DisplayedLevels.Max() < -50);
            var held = RenderHelper.Render(s, new Size(700, 280), 96);
            RenderHelper.MaybeSave(held, "spectrum-bars-held-96");
        });
    }

    [Fact]
    public void Spectrum_AxisStopsAtNyquist()
    {
        RenderHelper.RunSta(() =>
        {
            var s = new SpectrumAnalyzer { Spectrum = ToneSpectrum(rate: 16000, n: 1024, bin: 64) };
            RenderHelper.Render(s, new Size(400, 200));
            Assert.Equal(8000, s.Axis.Maximum, 9);
            Assert.Equal(-6, s.DisplayedLevels.Where(v => !double.IsNaN(v)).Max(), 6);
        });
    }

    private static ResponseSnapshot Response(bool measured = false)
    {
        var f = ResponseMath.LogSpacedFrequencies(20, 20000, 400);
        // Second-order high-pass at 80 Hz plus a +6 dB bump at 2 kHz.
        var g = f.Select(x => -10 * Math.Log10(1 + Math.Pow(80 / x, 4)) + 6 * Math.Exp(-Math.Pow(Math.Log2(x / 2000) * 1.6, 2))).ToArray();
        var p = f.Select(x => ResponseMath.WrapDegrees(180 - 2 * Math.Atan(x / 80) * 180 / Math.PI)).ToArray();
        if (measured) for (int i = 0; i < g.Length; i += 7) g[i] += 0.4;
        return new ResponseSnapshot(f, g, p) { IsMeasured = measured, SampleRate = 48000 };
    }

    [Fact]
    public void ResponseGraph_RendersAndPublishesRequestsInsteadOfEditing()
    {
        RenderHelper.RunSta(() =>
        {
            var bands = new[] { new EqBand(EqBandType.HighPass, 80, 0, 0.707), new EqBand(EqBandType.Peaking, 2000, 6, 1.2) };
            var g = new FrequencyResponseGraph
            {
                Response = Response(),
                OverlayResponse = Response(measured: true),
                Bands = bands,
                SelectedBandIndex = 1,
                ShowPhase = true,
            };
            var bmp = RenderHelper.Render(g, new Size(720, 300), 144);
            Assert.True(RenderHelper.InkCoverage(bmp) > 0.9);
            RenderHelper.MaybeSave(bmp, "response-144");

            var zero = g.ToPoint(1000, 0);
            Assert.True(g.PlotBounds.Contains(zero));
            Assert.Equal(g.PlotBounds.Top + g.PlotBounds.Height / 2, zero.Y, 6);

            EqBandChangeRequest? raised = null, executed = null;
            g.BandChangeRequested += (_, e) => raised = e.Request;
            g.BandChangeCommand = new TestCommand(p => executed = (EqBandChangeRequest)p!);
            g.RequestBandChange(1, bands[1] with { GainDb = 3 });
            Assert.Equal(new EqBandChangeRequest(1, bands[1] with { GainDb = 3 }), raised);
            Assert.Equal(raised, executed);
            Assert.Same(bands, g.Bands); // the graph never edits its bands; the host acknowledges

            executed = null;
            g.BandChangeRequested += (_, e) => e.Handled = true;
            g.RequestBandChange(0, bands[0] with { FrequencyHz = 100 });
            Assert.Null(executed);
        });
    }

    [Fact]
    public void ResponseGraph_ShowsUnavailableResponse()
    {
        RenderHelper.RunSta(() =>
        {
            var g = new FrequencyResponseGraph { Response = Response() with { IsValid = false } };
            var bmp = RenderHelper.Render(g, new Size(500, 240), 96);
            RenderHelper.MaybeSave(bmp, "response-invalid-96");
        });
    }

    [Fact]
    public void CorrelationMeter_MapsValue_AndShowsUnavailableDistinctFromZero()
    {
        RenderHelper.RunSta(() =>
        {
            var m = new CorrelationMeter { Correlation = 0.5, MonoCheckCommand = new TestCommand(_ => { }) };
            Assert.True(m.IsAvailable);
            Assert.Equal(690, m.PointerX, 6); // 120 + 0.75 × 760
            var bmp = RenderHelper.Render(m, new Size(500, 150), 144);
            Assert.True(RenderHelper.InkCoverage(bmp) > 0.9);
            RenderHelper.MaybeSave(bmp, "correlation-144");

            m.Correlation = -0.7;
            Assert.Equal(234, m.PointerX, 6);
            RenderHelper.MaybeSave(RenderHelper.Render(m, new Size(500, 150), 144), "correlation-negative-144");

            m.Correlation = double.NaN;
            Assert.False(m.IsAvailable);
            RenderHelper.MaybeSave(RenderHelper.Render(m, new Size(500, 150), 144), "correlation-unavailable-144");

            m.Correlation = 0;
            m.IsValid = false;
            Assert.False(m.IsAvailable);
        });
    }

    private sealed class TestCommand : ICommand
    {
        private readonly Action<object?> _execute;
        public TestCommand(Action<object?> execute) => _execute = execute;
        public event EventHandler? CanExecuteChanged { add { } remove { } }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => _execute(parameter);
    }
}
