using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using AudioCentric.Controls.Measurements;
using AudioCentric.Controls.Plotting;
using AudioCentric.Controls.Wpf.Rendering;

namespace AudioCentric.Controls.Wpf.Controls;

public enum SpectrumDisplayMode
{
    /// <summary>A continuous trace with a soft fill, one point per device pixel column.</summary>
    Line,
    /// <summary>Fractional-octave bars (see <see cref="SpectrumAnalyzer.BandsPerOctave"/>).</summary>
    Bars,
}

/// <summary>
/// FFT spectrum analyzer display. The host's analysis provider supplies <see cref="SpectrumSnapshot"/> frames (framing,
/// windowing, FFT and calibration are its job). The control handles the axes, the reduction of bins to pixel columns or
/// fractional-octave bands, optional display averaging, visual peak hold and the cursor readout. It never touches audio.
/// <para>
/// Frequency runs 20 Hz to min(20 kHz, Nyquist) on a log axis by default. A 0 Hz bin, which has no place on a log axis,
/// is shown as a separate "DC" readout. Silence (−∞) draws at the floor; NaN breaks the trace.
/// </para>
/// </summary>
public class SpectrumAnalyzer : FrameworkElement, IRenderTickable
{
    private readonly VisualCollection _children;
    private readonly DrawingVisual _static = new();
    private readonly DrawingVisual _data = new();
    private readonly DrawingVisual _overlay = new();
    private readonly DrawingVisual _glass = new();

    private Size _size;
    private Rect _screen, _plot;
    private FrequencyAxis _axis = new(20, 20000);
    private FrequencyBand[] _bands = Array.Empty<FrequencyBand>();
    private double[] _live = Array.Empty<double>(), _shown = Array.Empty<double>(), _peak = Array.Empty<double>(), _peakTimer = Array.Empty<double>();
    private bool _hasData;
    private double _pendingElapsed, _sinceData;
    private bool _registered;
    private Point? _cursor;

    static SpectrumAnalyzer()
    {
        FocusVisualStyleProperty.OverrideMetadata(typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(null));
    }

    public SpectrumAnalyzer()
    {
        _children = new VisualCollection(this) { _static, _data, _overlay, _glass };
        Loaded += (_, _) => { if (!_registered) { _registered = true; RenderScheduler.Register(this); } };
        Unloaded += (_, _) => { if (_registered) { _registered = false; RenderScheduler.Unregister(this); } };
    }

    #region Dependency properties

    /// <summary>Newest spectrum frame from the provider. Each new snapshot advances averaging and peak hold.</summary>
    public SpectrumSnapshot? Spectrum { get => (SpectrumSnapshot?)GetValue(SpectrumProperty); set => SetValue(SpectrumProperty, value); }
    public static readonly DependencyProperty SpectrumProperty = DependencyProperty.Register(
        nameof(Spectrum), typeof(SpectrumSnapshot), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(null, OnSpectrumChanged));

    public double MinimumFrequency { get => (double)GetValue(MinimumFrequencyProperty); set => SetValue(MinimumFrequencyProperty, value); }
    public static readonly DependencyProperty MinimumFrequencyProperty = DependencyProperty.Register(
        nameof(MinimumFrequency), typeof(double), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(20.0, OnAxisChanged), v => (double)v >= 0);

    /// <summary>Upper frequency; the axis stops at Nyquist if that is lower.</summary>
    public double MaximumFrequency { get => (double)GetValue(MaximumFrequencyProperty); set => SetValue(MaximumFrequencyProperty, value); }
    public static readonly DependencyProperty MaximumFrequencyProperty = DependencyProperty.Register(
        nameof(MaximumFrequency), typeof(double), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(20000.0, OnAxisChanged), v => (double)v > 0);

    public FrequencyScale FrequencyScale { get => (FrequencyScale)GetValue(FrequencyScaleProperty); set => SetValue(FrequencyScaleProperty, value); }
    public static readonly DependencyProperty FrequencyScaleProperty = DependencyProperty.Register(
        nameof(FrequencyScale), typeof(FrequencyScale), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(FrequencyScale.Logarithmic, OnAxisChanged));

    /// <summary>Level at the top of the plot (dB in the snapshot's unit). Default 0.</summary>
    public double MaximumDb { get => (double)GetValue(MaximumDbProperty); set => SetValue(MaximumDbProperty, value); }
    public static readonly DependencyProperty MaximumDbProperty = DependencyProperty.Register(
        nameof(MaximumDb), typeof(double), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(0.0, OnAxisChanged));

    /// <summary>Visible level range below <see cref="MaximumDb"/>. Default 90 dB.</summary>
    public double DisplayRangeDb { get => (double)GetValue(DisplayRangeDbProperty); set => SetValue(DisplayRangeDbProperty, value); }
    public static readonly DependencyProperty DisplayRangeDbProperty = DependencyProperty.Register(
        nameof(DisplayRangeDb), typeof(double), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(90.0, OnAxisChanged), v => (double)v > 0);

    public SpectrumDisplayMode DisplayMode { get => (SpectrumDisplayMode)GetValue(DisplayModeProperty); set => SetValue(DisplayModeProperty, value); }
    public static readonly DependencyProperty DisplayModeProperty = DependencyProperty.Register(
        nameof(DisplayMode), typeof(SpectrumDisplayMode), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(SpectrumDisplayMode.Line, OnAxisChanged));

    /// <summary>Bands per octave in <see cref="SpectrumDisplayMode.Bars"/> mode (1 = octaves, 3 = third-octaves …). Default 3.</summary>
    public int BandsPerOctave { get => (int)GetValue(BandsPerOctaveProperty); set => SetValue(BandsPerOctaveProperty, value); }
    public static readonly DependencyProperty BandsPerOctaveProperty = DependencyProperty.Register(
        nameof(BandsPerOctave), typeof(int), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(3, OnAxisChanged), v => (int)v is >= 1 and <= 48);

    /// <summary>How bins combine into a bar. <see cref="Plotting.BandAggregation.Maximum"/> keeps tone levels; PowerSum shows band energy.</summary>
    public BandAggregation BandAggregation { get => (BandAggregation)GetValue(BandAggregationProperty); set => SetValue(BandAggregationProperty, value); }
    public static readonly DependencyProperty BandAggregationProperty = DependencyProperty.Register(
        nameof(BandAggregation), typeof(BandAggregation), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(BandAggregation.Maximum, OnAxisChanged));

    /// <summary>
    /// Display averaging time constant in seconds (exponential, in the power domain); 0 = off. This is presentation
    /// smoothing only. If the provider already averages, leave it at 0.
    /// </summary>
    public double AveragingTime { get => (double)GetValue(AveragingTimeProperty); set => SetValue(AveragingTimeProperty, value); }
    public static readonly DependencyProperty AveragingTimeProperty = DependencyProperty.Register(
        nameof(AveragingTime), typeof(double), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(0.0, (d, _) => ((SpectrumAnalyzer)d).DrawOverlay()), v => (double)v >= 0);

    public bool IsPeakHoldEnabled { get => (bool)GetValue(IsPeakHoldEnabledProperty); set => SetValue(IsPeakHoldEnabledProperty, value); }
    public static readonly DependencyProperty IsPeakHoldEnabledProperty = DependencyProperty.Register(
        nameof(IsPeakHoldEnabled), typeof(bool), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(true, OnPeakSettingChanged));

    public double PeakHoldSeconds { get => (double)GetValue(PeakHoldSecondsProperty); set => SetValue(PeakHoldSecondsProperty, value); }
    public static readonly DependencyProperty PeakHoldSecondsProperty = DependencyProperty.Register(
        nameof(PeakHoldSeconds), typeof(double), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(1.5));

    public double PeakDecayDbPerSecond { get => (double)GetValue(PeakDecayDbPerSecondProperty); set => SetValue(PeakDecayDbPerSecondProperty, value); }
    public static readonly DependencyProperty PeakDecayDbPerSecondProperty = DependencyProperty.Register(
        nameof(PeakDecayDbPerSecond), typeof(double), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(20.0));

    public Color TraceColor { get => (Color)GetValue(TraceColorProperty); set => SetValue(TraceColorProperty, value); }
    public static readonly DependencyProperty TraceColorProperty = DependencyProperty.Register(
        nameof(TraceColor), typeof(Color), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(Color.FromRgb(0x7C, 0xFF, 0x5A), (d, _) => ((SpectrumAnalyzer)d).DrawData()));

    public Color PeakColor { get => (Color)GetValue(PeakColorProperty); set => SetValue(PeakColorProperty, value); }
    public static readonly DependencyProperty PeakColorProperty = DependencyProperty.Register(
        nameof(PeakColor), typeof(Color), typeof(SpectrumAnalyzer), new FrameworkPropertyMetadata(PlotArt.Amber, (d, _) => ((SpectrumAnalyzer)d).DrawData()));

    #endregion

    /// <summary>The axis currently drawn (range clipped to Nyquist).</summary>
    public FrequencyAxis Axis => _axis;

    /// <summary>Displayed levels: one per pixel column (Line) or per band (Bars), after averaging. NaN where there is no data.</summary>
    public IReadOnlyList<double> DisplayedLevels => _shown;

    /// <summary>The bands drawn in Bars mode.</summary>
    public IReadOnlyList<FrequencyBand> Bands => _bands;

    /// <summary>Clears averaging and peak hold.</summary>
    public void ResetPeaks()
    {
        Array.Fill(_peak, double.NaN);
        Array.Fill(_peakTimer, 0);
        DrawData();
    }

    private double MinimumDb => MaximumDb - DisplayRangeDb;

    private static void OnSpectrumChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var o = (SpectrumAnalyzer)d;
        var old = e.OldValue as SpectrumSnapshot;
        var s = e.NewValue as SpectrumSnapshot;
        if (old == null || s == null || old.SampleRate != s.SampleRate) o.Rebuild(); // the axis may end at a new Nyquist
        else o.Ingest();
    }

    private static void OnAxisChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SpectrumAnalyzer)d).Rebuild();

    private static void OnPeakSettingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SpectrumAnalyzer)d).ResetPeaks();

    // ---------------------------------------------------------------- processing

    /// <summary>Recomputes axis, bands and buffers, then redraws everything.</summary>
    private void Rebuild()
    {
        var s = Spectrum;
        double max = MaximumFrequency;
        if (s != null && s.SampleRate > 0) max = Math.Min(max, s.SampleRate / 2.0);
        double min = MinimumFrequency;
        if (FrequencyScale == FrequencyScale.Logarithmic) min = Math.Max(min, 1);
        if (max <= min) max = min * 2;
        _axis = new FrequencyAxis(min, max, FrequencyScale);
        Layout();
        _bands = DisplayMode == SpectrumDisplayMode.Bars ? FractionalOctaveBands.Create(BandsPerOctave, Math.Max(1, min), max) : Array.Empty<FrequencyBand>();
        int count = DisplayMode == SpectrumDisplayMode.Bars ? _bands.Length : Math.Max(0, ColumnCount());
        // New geometry means new columns or bands, so averaging and peak hold start over.
        _live = new double[count];
        _shown = new double[count];
        _peak = new double[count];
        _peakTimer = new double[count];
        Array.Fill(_shown, double.NaN);
        Array.Fill(_peak, double.NaN);
        _hasData = false;
        DrawStatic();
        if (Spectrum != null) Ingest(fresh: true);
        else { DrawData(); DrawOverlay(); }
    }

    private int ColumnCount()
    {
        if (_plot.Width < 1) return 0;
        var dpi = VisualTreeHelper.GetDpi(this);
        return Math.Max(1, (int)Math.Round(_plot.Width * dpi.DpiScaleX));
    }

    private void Ingest(bool fresh = false)
    {
        var s = Spectrum;
        if (s == null || _live.Length == 0) { DrawData(); DrawOverlay(); return; }

        double dt = _registered ? _pendingElapsed : 1.0 / 60;
        _pendingElapsed = 0;
        _sinceData = 0;

        if (DisplayMode == SpectrumDisplayMode.Bars)
            SpectrumReducer.ToBands(s.Frequencies, s.LevelsDb, _bands, BandAggregation, s.NoiseBandwidthBins, _axis, _live);
        else
            SpectrumReducer.ToColumns(s.Frequencies, s.LevelsDb, _axis, _live);

        double tau = AveragingTime;
        if (fresh) dt = 0;
        if (tau > 0 && _hasData && dt > 0)
        {
            double a = Math.Exp(-dt / tau);
            for (int i = 0; i < _live.Length; i++) _shown[i] = AveragePower(_shown[i], _live[i], a);
        }
        else if (tau <= 0 || !_hasData)
        {
            Array.Copy(_live, _shown, _live.Length);
        }
        _hasData = true;

        if (IsPeakHoldEnabled) UpdatePeaks(dt);
        DrawData();
        DrawOverlay();
    }

    private static double AveragePower(double avgDb, double liveDb, double a)
    {
        if (double.IsNaN(liveDb)) return avgDb;
        if (double.IsNaN(avgDb)) return liveDb;
        double p = a * Math.Pow(10, avgDb / 10) + (1 - a) * Math.Pow(10, liveDb / 10);
        return p > 0 ? 10 * Math.Log10(p) : double.NegativeInfinity;
    }

    private void UpdatePeaks(double dt)
    {
        double hold = PeakHoldSeconds, decay = PeakDecayDbPerSecond, floor = MinimumDb - 10;
        for (int i = 0; i < _shown.Length; i++)
        {
            double v = _shown[i];
            if (double.IsNaN(v)) continue;
            if (double.IsNaN(_peak[i]) || v >= _peak[i])
            {
                _peak[i] = v;
                _peakTimer[i] = hold;
                continue;
            }
            double t = dt;
            if (_peakTimer[i] > 0)
            {
                double used = Math.Min(_peakTimer[i], t);
                _peakTimer[i] -= used;
                t -= used;
            }
            if (t > 0)
            {
                _peak[i] = Math.Max(v, _peak[i] - decay * t);
                if (_peak[i] < floor) _peak[i] = v;
            }
        }
    }

    void IRenderTickable.Tick(double elapsedSeconds)
    {
        _pendingElapsed += elapsedSeconds;
        _sinceData += elapsedSeconds;
        // If frames stop arriving, let the peak markers keep falling.
        if (IsPeakHoldEnabled && _hasData && _sinceData > 0.1)
        {
            UpdatePeaks(elapsedSeconds);
            DrawData();
        }
    }

    // ---------------------------------------------------------------- layout

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 640 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 280 : availableSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (finalSize != _size)
        {
            _size = finalSize;
            Rebuild();
        }
        return finalSize;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => Rebuild();

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;

    private double FontSize => PlotArt.TextSize(_size);

    private void Layout()
    {
        double b = PlotArt.BezelWidth(_size), fs = FontSize;
        _screen = new Rect(b, b, Math.Max(1, _size.Width - 2 * b), Math.Max(1, _size.Height - 2 * b));
        double left = fs * 3.3, right = fs * 1.4, top = fs * 2.1, bottom = fs * 2.0;
        _plot = new Rect(_screen.Left + left, _screen.Top + top,
            Math.Max(1, _screen.Width - left - right), Math.Max(1, _screen.Height - top - bottom));
    }

    private double X(double hz) => _plot.Left + _axis.ToNormalized(hz) * _plot.Width;

    private double Y(double db)
    {
        if (double.IsNegativeInfinity(db)) return _plot.Bottom;
        double t = (db - MinimumDb) / DisplayRangeDb;
        return _plot.Bottom - Math.Clamp(t, 0, 1.02) * _plot.Height;
    }

    // ---------------------------------------------------------------- drawing

    private void DrawStatic()
    {
        using var dc = _static.RenderOpen();
        if (_size.Width < 4 || _size.Height < 4) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        PlotArt.DrawFrame(dc, _size, dpi.DpiScaleX);
        double fs = FontSize, ppd = dpi.PixelsPerDip;
        var label = PlotArt.LabelBrush();

        dc.PushClip(new RectangleGeometry(_plot));
        foreach (var t in _axis.Ticks())
        {
            double x = X(t.Value);
            dc.DrawLine(PlotArt.GridPen(t.IsMajor ? (byte)55 : (byte)24), new Point(x, _plot.Top), new Point(x, _plot.Bottom));
        }
        foreach (var t in AxisTicks.Linear(MinimumDb, MaximumDb, Math.Max(3, (int)(_plot.Height / (fs * 2.2)))))
        {
            if (!t.IsMajor) continue;
            double y = Y(t.Value);
            dc.DrawLine(PlotArt.GridPen(Math.Abs(t.Value) < 1e-9 ? (byte)90 : (byte)45), new Point(_plot.Left, y), new Point(_plot.Right, y));
        }
        dc.Pop();
        dc.DrawRectangle(null, PlotArt.GridPen(80), _plot);

        // Frequency labels, skipping any that would collide.
        double lastRight = double.NegativeInfinity;
        foreach (var t in _axis.Ticks())
        {
            if (t.Label == null) continue;
            var ft = VintageArt.Text(t.Label, fs, label, FontWeights.SemiBold, ppd);
            double x = X(t.Value) - ft.Width / 2;
            if (x < lastRight + fs * 0.5 || x + ft.Width > _screen.Right - 2) continue;
            dc.DrawText(ft, new Point(x, _plot.Bottom + fs * 0.35));
            lastRight = x + ft.Width;
        }
        foreach (var t in AxisTicks.Linear(MinimumDb, MaximumDb, Math.Max(3, (int)(_plot.Height / (fs * 2.2)))))
        {
            if (t.Label == null) continue;
            string text = t.Value > 0 ? "+" + t.Label : t.Label.Replace('-', '−');
            PlotArt.DrawRightAligned(dc, VintageArt.Text(text, fs, label, FontWeights.SemiBold, ppd), _plot.Left - fs * 0.4, Y(t.Value));
        }

        using (var g = _glass.RenderOpen()) PlotArt.DrawGlass(g, _screen);
    }

    private void DrawData()
    {
        using var dc = _data.RenderOpen();
        if (!_hasData || _shown.Length == 0) return;
        dc.PushClip(new RectangleGeometry(new Rect(_plot.Left, _plot.Top - 2, _plot.Width, _plot.Height + 2)));
        if (DisplayMode == SpectrumDisplayMode.Bars) DrawBars(dc);
        else DrawLine(dc);
        dc.Pop();
    }

    private void DrawLine(DrawingContext dc)
    {
        int n = _shown.Length;
        double dx = _plot.Width / n;
        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var lc = line.Open())
        using (var fc = fill.Open())
        {
            int i = 0;
            while (i < n)
            {
                while (i < n && double.IsNaN(_shown[i])) i++;
                if (i >= n) break;
                int start = i;
                double x0 = _plot.Left + (i + 0.5) * dx;
                lc.BeginFigure(new Point(x0, Y(_shown[i])), false, false);
                fc.BeginFigure(new Point(x0, _plot.Bottom), true, true);
                for (; i < n && !double.IsNaN(_shown[i]); i++)
                {
                    var p = new Point(_plot.Left + (i + 0.5) * dx, Y(_shown[i]));
                    if (i > start) lc.LineTo(p, true, true);
                    fc.LineTo(p, false, false);
                }
                fc.LineTo(new Point(_plot.Left + (i - 0.5) * dx, _plot.Bottom), false, false);
            }
        }
        line.Freeze();
        fill.Freeze();

        if (IsPeakHoldEnabled)
        {
            var peak = new StreamGeometry();
            using (var pc = peak.Open())
            {
                bool open = false;
                for (int i = 0; i < n; i++)
                {
                    if (double.IsNaN(_peak[i])) { open = false; continue; }
                    var p = new Point(_plot.Left + (i + 0.5) * dx, Y(_peak[i]));
                    if (!open) { pc.BeginFigure(p, false, false); open = true; }
                    else pc.LineTo(p, true, true);
                }
            }
            peak.Freeze();
            dc.DrawGeometry(null, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(PeakColor, 0.9)), 1.2), peak);
        }
        // Trace over the held peaks, so the marker shows only where it is above the live level.
        var c = TraceColor;
        var fillBrush = new LinearGradientBrush(VintageArt.WithAlpha(c, 0.38), VintageArt.WithAlpha(c, 0.03), 90);
        fillBrush.Freeze();
        dc.DrawGeometry(fillBrush, null, fill);
        dc.DrawGeometry(null, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(c, 0.22)), 5, PenLineCap.Round), line);
        dc.DrawGeometry(null, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.Blend(c, Colors.White, 0.35)), 1.4, PenLineCap.Round), line);
    }

    private void DrawBars(DrawingContext dc)
    {
        var bars = new StreamGeometry();
        var caps = new StreamGeometry();
        using (var bc = bars.Open())
        using (var cc = caps.Open())
        {
            for (int i = 0; i < _bands.Length; i++)
            {
                var (x0, x1) = BarExtent(i);
                if (x1 - x0 < 0.5) continue;
                double v = _shown[i];
                if (!double.IsNaN(v) && !double.IsNegativeInfinity(v))
                {
                    double y = Y(v);
                    if (y < _plot.Bottom - 0.5) AddRect(bc, new Rect(x0, y, x1 - x0, _plot.Bottom - y));
                }
                if (IsPeakHoldEnabled && !double.IsNaN(_peak[i]) && !double.IsNegativeInfinity(_peak[i]))
                {
                    double py = Y(_peak[i]);
                    if (py < _plot.Bottom - 2) AddRect(cc, new Rect(x0, py - 1.25, x1 - x0, 2.5));
                }
            }
        }
        bars.Freeze();
        caps.Freeze();

        var c = TraceColor;
        var body = new LinearGradientBrush(new GradientStopCollection
        {
            new(VintageArt.Blend(c, Colors.White, 0.3), 0), new(c, 0.25), new(VintageArt.Blend(c, Colors.Black, 0.55), 1),
        }, new Point(0, _plot.Top), new Point(0, _plot.Bottom))
        { MappingMode = BrushMappingMode.Absolute };
        body.Freeze();
        dc.DrawGeometry(VintageArt.Frozen(VintageArt.WithAlpha(c, 0.12)), VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(c, 0.18)), 3), bars);
        dc.DrawGeometry(body, null, bars);

        // Segment gaps give the bars a lit-LED texture.
        double seg = Math.Max(3, FontSize * 0.42);
        dc.PushClip(bars);
        var gap = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(150, 0x03, 0x08, 0x04)), Math.Max(0.8, seg * 0.22));
        for (double y = _plot.Bottom - seg; y > _plot.Top; y -= seg) dc.DrawLine(gap, new Point(_plot.Left, y), new Point(_plot.Right, y));
        dc.Pop();

        dc.DrawGeometry(VintageArt.Frozen(PeakColor), null, caps);
    }

    private (double X0, double X1) BarExtent(int i)
    {
        var band = _bands[i];
        double x0 = Math.Max(_plot.Left, X(band.Low)), x1 = Math.Min(_plot.Right, X(band.High));
        double w = x1 - x0, gap = Math.Max(1, w * 0.18);
        return (x0 + gap / 2, x1 - gap / 2);
    }

    private static void AddRect(StreamGeometryContext ctx, Rect r)
    {
        ctx.BeginFigure(r.TopLeft, true, true);
        ctx.LineTo(r.TopRight, false, false);
        ctx.LineTo(r.BottomRight, false, false);
        ctx.LineTo(r.BottomLeft, false, false);
    }

    private void DrawOverlay()
    {
        using var dc = _overlay.RenderOpen();
        if (_size.Width < 4 || _size.Height < 4) return;
        var ci = CultureInfo.InvariantCulture;
        double fs = FontSize, ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var dim = PlotArt.LabelBrush(200);
        var s = Spectrum;
        double y = _screen.Top + (_plot.Top - _screen.Top) / 2;

        var parts = new List<string>();
        if (s != null)
        {
            if (s.FftSize > 0) parts.Add($"FFT {s.FftSize}");
            if (!string.IsNullOrEmpty(s.Window)) parts.Add(s.Window);
            if (s.BinSpacing > 0) parts.Add($"Δf {s.BinSpacing.ToString(s.BinSpacing < 10 ? "0.00" : "0.0", ci)} Hz");
        }
        if (DisplayMode == SpectrumDisplayMode.Bars)
            parts.Add($"1/{BandsPerOctave} oct {(BandAggregation == BandAggregation.Maximum ? "max" : "power")}");
        if (AveragingTime > 0) parts.Add($"AVG {AveragingTime.ToString("0.##", ci)} s");
        if (parts.Count > 0)
        {
            var ft = VintageArt.Text(string.Join("  ·  ", parts), fs, dim, FontWeights.SemiBold, ppd);
            dc.DrawText(ft, new Point(_plot.Left, y - ft.Height / 2));
        }

        string unit = (s?.Unit ?? MeasurementUnit.DbFs).Symbol();
        string right = IsPeakHoldEnabled ? $"{unit}  ·  PEAK HOLD" : unit;
        // DC has no position on a log axis, so it gets its own readout.
        if (s != null && s.Frequencies.Length > 0 && s.Frequencies[0] <= 0 && _axis.Minimum > 0 && s.LevelsDb.Length > 0)
            right = $"DC {FormatDb(s.LevelsDb[0])}   " + right;
        var rt = VintageArt.Text(right, fs, VintageArt.Frozen(VintageArt.WithAlpha(PeakColor, 0.9)), FontWeights.SemiBold, ppd);
        dc.DrawText(rt, new Point(_plot.Right - rt.Width, y - rt.Height / 2));

        if (_cursor is Point c && _plot.Contains(c) && _hasData)
        {
            double hz = _axis.FromNormalized((c.X - _plot.Left) / _plot.Width);
            double level = double.NaN;
            if (DisplayMode == SpectrumDisplayMode.Bars)
            {
                for (int i = 0; i < _bands.Length; i++)
                    if (hz >= _bands[i].Low && hz < _bands[i].High) { level = _shown[i]; hz = _bands[i].Center; break; }
            }
            else
            {
                int i = Math.Clamp((int)((c.X - _plot.Left) / _plot.Width * _shown.Length), 0, _shown.Length - 1);
                level = _shown[i];
            }
            double x = DisplayMode == SpectrumDisplayMode.Bars ? X(hz) : c.X;
            dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(120, 255, 255, 255)), 1), new Point(x, _plot.Top), new Point(x, _plot.Bottom));
            var ft = VintageArt.Text($"{FrequencyAxis.FormatHzWithUnit(hz)}   {FormatDb(level)} {unit}", fs, VintageArt.Frozen(Colors.White), FontWeights.SemiBold, ppd);
            PlotArt.DrawReadoutBox(dc, ft, new Point(x + 8, _plot.Top + 6), _plot);
        }
    }

    private static string FormatDb(double db) =>
        double.IsNaN(db) ? "---" : double.IsNegativeInfinity(db) ? "−∞" : db.ToString("0.0;−0.0;0.0", CultureInfo.InvariantCulture);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _cursor = e.GetPosition(this);
        DrawOverlay();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _cursor = null;
        DrawOverlay();
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new SpectrumAnalyzerAutomationPeer(this);
}

public sealed class SpectrumAnalyzerAutomationPeer : FrameworkElementAutomationPeer
{
    public SpectrumAnalyzerAutomationPeer(SpectrumAnalyzer owner) : base(owner) { }
    protected override string GetClassNameCore() => nameof(SpectrumAnalyzer);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        if (!string.IsNullOrEmpty(name)) return name;
        var o = (SpectrumAnalyzer)Owner;
        return $"Spectrum analyzer, {FrequencyAxis.FormatHzWithUnit(o.Axis.Minimum)} to {FrequencyAxis.FormatHzWithUnit(o.Axis.Maximum)}, {o.DisplayMode}";
    }
}
