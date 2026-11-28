using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using AudioCentric.Controls.Input;
using AudioCentric.Controls.Measurements;
using AudioCentric.Controls.Plotting;
using AudioCentric.Controls.Wpf.Rendering;

namespace AudioCentric.Controls.Wpf.Controls;

/// <summary>Event data for <see cref="FrequencyResponseGraph.BandChangeRequestedEvent"/>.</summary>
public sealed class EqBandChangeRequestedEventArgs : RoutedEventArgs
{
    public EqBandChangeRequestedEventArgs(RoutedEvent routedEvent, object source, EqBandChangeRequest request) : base(routedEvent, source) =>
        Request = request;

    public EqBandChangeRequest Request { get; }
}

/// <summary>
/// Frequency-response graph: relative gain in dB (not dBFS) against log frequency, with optional phase. The host supplies
/// <see cref="ResponseSnapshot"/>s computed from its coefficients or measured from excitation and capture, and the graph
/// draws theoretical curves solid and measured curves dashed. It marks the 0 dB line and the −3 dB cutoff points.
/// <para>
/// With <see cref="Bands"/> set, each band is a draggable handle. Editing never changes the bands directly: the graph
/// raises <see cref="BandChangeRequested"/> and executes <see cref="BandChangeCommand"/> with an <see cref="EqBandChangeRequest"/>.
/// The host applies, constrains or rejects it and sends back new <see cref="Bands"/> and <see cref="Response"/>, so the
/// graph always shows the acknowledged processor state.
/// </para>
/// <para>
/// Mouse: drag a handle (frequency and gain), wheel over it for Q, double-click to zero its gain, right-click to bypass it.
/// Keys: 1–9 select a band, ←/→ frequency (a semitone; Shift: fine), ↑/↓ gain 0.5 dB (Shift: 0.1), PgUp/PgDn Q,
/// Space toggles bypass, 0 zeroes gain.
/// </para>
/// </summary>
public class FrequencyResponseGraph : FrameworkElement
{
    private static readonly Color[] BandPalette =
    {
        Color.FromRgb(0xFF, 0x6B, 0x5A), Color.FromRgb(0xFF, 0xC0, 0x3A), Color.FromRgb(0xC8, 0xF0, 0x5A), Color.FromRgb(0x5A, 0xD2, 0xFF),
        Color.FromRgb(0xB5, 0x8C, 0xFF), Color.FromRgb(0xFF, 0x8C, 0xD2), Color.FromRgb(0x5A, 0xFF, 0xC0), Color.FromRgb(0xF0, 0xF0, 0xF0),
    };

    private readonly VisualCollection _children;
    private readonly DrawingVisual _static = new();
    private readonly DrawingVisual _curves = new();
    private readonly DrawingVisual _handles = new();
    private readonly DrawingVisual _glass = new();

    private Size _size;
    private Rect _screen, _plot;
    private FrequencyAxis _axis = new(20, 20000);
    private int _dragIndex = -1;

    static FrequencyResponseGraph()
    {
        FocusableProperty.OverrideMetadata(typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(true));
        FocusVisualStyleProperty.OverrideMetadata(typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(null));
    }

    public FrequencyResponseGraph()
    {
        _children = new VisualCollection(this) { _static, _curves, _handles, _glass };
    }

    #region Dependency properties and events

    /// <summary>The main response (usually the theoretical one, from the processor's coefficients).</summary>
    public ResponseSnapshot? Response { get => (ResponseSnapshot?)GetValue(ResponseProperty); set => SetValue(ResponseProperty, value); }
    public static readonly DependencyProperty ResponseProperty = DependencyProperty.Register(
        nameof(Response), typeof(ResponseSnapshot), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(null, OnDataChanged));

    /// <summary>An optional second response drawn over the first, e.g. a measurement to compare with theory.</summary>
    public ResponseSnapshot? OverlayResponse { get => (ResponseSnapshot?)GetValue(OverlayResponseProperty); set => SetValue(OverlayResponseProperty, value); }
    public static readonly DependencyProperty OverlayResponseProperty = DependencyProperty.Register(
        nameof(OverlayResponse), typeof(ResponseSnapshot), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(null, OnDataChanged));

    /// <summary>Editable bands, one handle each. Replace the list to update; it is treated as immutable.</summary>
    public IReadOnlyList<EqBand>? Bands { get => (IReadOnlyList<EqBand>?)GetValue(BandsProperty); set => SetValue(BandsProperty, value); }
    public static readonly DependencyProperty BandsProperty = DependencyProperty.Register(
        nameof(Bands), typeof(IReadOnlyList<EqBand>), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(null, (d, _) => ((FrequencyResponseGraph)d).DrawHandles()));

    public int SelectedBandIndex { get => (int)GetValue(SelectedBandIndexProperty); set => SetValue(SelectedBandIndexProperty, value); }
    public static readonly DependencyProperty SelectedBandIndexProperty = DependencyProperty.Register(
        nameof(SelectedBandIndex), typeof(int), typeof(FrequencyResponseGraph),
        new FrameworkPropertyMetadata(-1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((FrequencyResponseGraph)d).DrawHandles()));

    /// <summary>When false the handles are shown but cannot be edited.</summary>
    public bool IsEditable { get => (bool)GetValue(IsEditableProperty); set => SetValue(IsEditableProperty, value); }
    public static readonly DependencyProperty IsEditableProperty = DependencyProperty.Register(
        nameof(IsEditable), typeof(bool), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(true));

    /// <summary>Executed with an <see cref="EqBandChangeRequest"/> for every edit not handled by a <see cref="BandChangeRequested"/> handler.</summary>
    public ICommand? BandChangeCommand { get => (ICommand?)GetValue(BandChangeCommandProperty); set => SetValue(BandChangeCommandProperty, value); }
    public static readonly DependencyProperty BandChangeCommandProperty = DependencyProperty.Register(
        nameof(BandChangeCommand), typeof(ICommand), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(null));

    public static readonly RoutedEvent BandChangeRequestedEvent = EventManager.RegisterRoutedEvent(
        nameof(BandChangeRequested), RoutingStrategy.Bubble, typeof(EventHandler<EqBandChangeRequestedEventArgs>), typeof(FrequencyResponseGraph));

    /// <summary>Raised when the user edits a band. Set Handled to stop <see cref="BandChangeCommand"/> from running.</summary>
    public event EventHandler<EqBandChangeRequestedEventArgs> BandChangeRequested
    {
        add => AddHandler(BandChangeRequestedEvent, value);
        remove => RemoveHandler(BandChangeRequestedEvent, value);
    }

    public bool ShowPhase { get => (bool)GetValue(ShowPhaseProperty); set => SetValue(ShowPhaseProperty, value); }
    public static readonly DependencyProperty ShowPhaseProperty = DependencyProperty.Register(
        nameof(ShowPhase), typeof(bool), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(false, OnAxisChanged));

    public bool ShowCutoffMarkers { get => (bool)GetValue(ShowCutoffMarkersProperty); set => SetValue(ShowCutoffMarkersProperty, value); }
    public static readonly DependencyProperty ShowCutoffMarkersProperty = DependencyProperty.Register(
        nameof(ShowCutoffMarkers), typeof(bool), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(true, OnDataChanged));

    /// <summary>Level marked as the cutoff on the main response. Default −3 dB.</summary>
    public double CutoffLevelDb { get => (double)GetValue(CutoffLevelDbProperty); set => SetValue(CutoffLevelDbProperty, value); }
    public static readonly DependencyProperty CutoffLevelDbProperty = DependencyProperty.Register(
        nameof(CutoffLevelDb), typeof(double), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(-3.0, OnDataChanged));

    public double MinimumFrequency { get => (double)GetValue(MinimumFrequencyProperty); set => SetValue(MinimumFrequencyProperty, value); }
    public static readonly DependencyProperty MinimumFrequencyProperty = DependencyProperty.Register(
        nameof(MinimumFrequency), typeof(double), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(20.0, OnAxisChanged), v => (double)v > 0);

    public double MaximumFrequency { get => (double)GetValue(MaximumFrequencyProperty); set => SetValue(MaximumFrequencyProperty, value); }
    public static readonly DependencyProperty MaximumFrequencyProperty = DependencyProperty.Register(
        nameof(MaximumFrequency), typeof(double), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(20000.0, OnAxisChanged), v => (double)v > 0);

    public double MinimumDb { get => (double)GetValue(MinimumDbProperty); set => SetValue(MinimumDbProperty, value); }
    public static readonly DependencyProperty MinimumDbProperty = DependencyProperty.Register(
        nameof(MinimumDb), typeof(double), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(-24.0, OnAxisChanged));

    public double MaximumDb { get => (double)GetValue(MaximumDbProperty); set => SetValue(MaximumDbProperty, value); }
    public static readonly DependencyProperty MaximumDbProperty = DependencyProperty.Register(
        nameof(MaximumDb), typeof(double), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(24.0, OnAxisChanged));

    public Color TraceColor { get => (Color)GetValue(TraceColorProperty); set => SetValue(TraceColorProperty, value); }
    public static readonly DependencyProperty TraceColorProperty = DependencyProperty.Register(
        nameof(TraceColor), typeof(Color), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(Color.FromRgb(0x7C, 0xFF, 0x5A), OnDataChanged));

    public Color OverlayColor { get => (Color)GetValue(OverlayColorProperty); set => SetValue(OverlayColorProperty, value); }
    public static readonly DependencyProperty OverlayColorProperty = DependencyProperty.Register(
        nameof(OverlayColor), typeof(Color), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(Color.FromRgb(0x7C, 0xC8, 0xF0), OnDataChanged));

    public Color PhaseColor { get => (Color)GetValue(PhaseColorProperty); set => SetValue(PhaseColorProperty, value); }
    public static readonly DependencyProperty PhaseColorProperty = DependencyProperty.Register(
        nameof(PhaseColor), typeof(Color), typeof(FrequencyResponseGraph), new FrameworkPropertyMetadata(Color.FromRgb(0xE8, 0xA2, 0x3A), OnDataChanged));

    #endregion

    public FrequencyAxis Axis => _axis;

    /// <summary>The plot rectangle in control coordinates (for hit testing and tests).</summary>
    public Rect PlotBounds => _plot;

    /// <summary>Position of a point (frequency, gain) in control coordinates.</summary>
    public Point ToPoint(double hz, double db) => new(X(hz), Y(db));

    /// <summary>
    /// Publishes a change request for band <paramref name="index"/>, exactly as an interactive edit would: raises
    /// <see cref="BandChangeRequested"/>, then runs <see cref="BandChangeCommand"/> unless a handler marked it handled.
    /// </summary>
    public void RequestBandChange(int index, EqBand requested)
    {
        var request = new EqBandChangeRequest(index, requested);
        var args = new EqBandChangeRequestedEventArgs(BandChangeRequestedEvent, this, request);
        RaiseEvent(args);
        if (args.Handled) return;
        var cmd = BandChangeCommand;
        if (cmd != null && cmd.CanExecute(request)) cmd.Execute(request);
    }

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var g = (FrequencyResponseGraph)d;
        g.DrawCurves();
        g.DrawHandles(); // pass-filter handles sit on the curve
    }

    private static void OnAxisChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((FrequencyResponseGraph)d).Rebuild();

    // ---------------------------------------------------------------- layout

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 640 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 300 : availableSize.Height);

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
    private double HandleRadius => Math.Clamp(FontSize * 0.85, 7, 11);

    private void Rebuild()
    {
        double min = MinimumFrequency, max = Math.Max(MaximumFrequency, min * 2);
        _axis = new FrequencyAxis(min, max);
        double b = PlotArt.BezelWidth(_size), fs = FontSize;
        _screen = new Rect(b, b, Math.Max(1, _size.Width - 2 * b), Math.Max(1, _size.Height - 2 * b));
        double left = fs * 3.3, right = ShowPhase ? fs * 3.6 : fs * 1.4, top = fs * 2.1, bottom = fs * 2.0;
        _plot = new Rect(_screen.Left + left, _screen.Top + top, Math.Max(1, _screen.Width - left - right), Math.Max(1, _screen.Height - top - bottom));
        DrawStatic();
        DrawCurves();
        DrawHandles();
    }

    private double X(double hz) => _plot.Left + _axis.ToNormalized(hz) * _plot.Width;

    private double Y(double db)
    {
        double range = MaximumDb - MinimumDb;
        if (!(range > 0)) return _plot.Top + _plot.Height / 2;
        if (double.IsNegativeInfinity(db)) return _plot.Bottom + 4;
        if (double.IsPositiveInfinity(db)) return _plot.Top - 4;
        return Math.Clamp(_plot.Bottom - (db - MinimumDb) / range * _plot.Height, _plot.Top - 4, _plot.Bottom + 4);
    }

    private double PhaseY(double degrees) => _plot.Top + (180 - degrees) / 360 * _plot.Height;

    private double FrequencyAt(double x) => _axis.FromNormalized(Math.Clamp((x - _plot.Left) / _plot.Width, 0, 1));

    private double GainAt(double y) => MinimumDb + (MaximumDb - MinimumDb) * Math.Clamp((_plot.Bottom - y) / _plot.Height, 0, 1);

    private double DbStep()
    {
        double range = MaximumDb - MinimumDb;
        int maxMajor = Math.Max(3, (int)(_plot.Height / (FontSize * 2.0)));
        foreach (var s in new[] { 1.0, 2, 3, 6, 12, 24, 48, 96 })
            if (range / s <= maxMajor - 1) return s;
        return AxisTicks.NiceStep(range / (maxMajor - 1));
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
        var ticks = _axis.Ticks();
        var dbTicks = AxisTicks.Linear(MinimumDb, MaximumDb, 100, DbStep()).Where(t => t.IsMajor).ToArray();

        dc.PushClip(new RectangleGeometry(_plot));
        foreach (var t in ticks)
            dc.DrawLine(PlotArt.GridPen(t.IsMajor ? (byte)55 : (byte)24), new Point(X(t.Value), _plot.Top), new Point(X(t.Value), _plot.Bottom));
        foreach (var t in dbTicks)
        {
            bool zero = Math.Abs(t.Value) < 1e-9;
            dc.DrawLine(zero ? PlotArt.GridPen(150, 1.6) : PlotArt.GridPen(45), new Point(_plot.Left, Y(t.Value)), new Point(_plot.Right, Y(t.Value)));
        }
        dc.Pop();
        dc.DrawRectangle(null, PlotArt.GridPen(80), _plot);

        double lastRight = double.NegativeInfinity;
        foreach (var t in ticks)
        {
            if (t.Label == null) continue;
            var ft = VintageArt.Text(t.Label, fs, label, FontWeights.SemiBold, ppd);
            double x = X(t.Value) - ft.Width / 2;
            if (x < lastRight + fs * 0.5 || x + ft.Width > _screen.Right - 2) continue;
            dc.DrawText(ft, new Point(x, _plot.Bottom + fs * 0.35));
            lastRight = x + ft.Width;
        }
        foreach (var t in dbTicks)
        {
            string text = t.Value > 0 ? "+" + t.Label : t.Label!.Replace('-', '−');
            PlotArt.DrawRightAligned(dc, VintageArt.Text(text, fs, label, FontWeights.SemiBold, ppd), _plot.Left - fs * 0.4, Y(t.Value));
        }
        var unit = VintageArt.Text("dB", fs, label, FontWeights.Bold, ppd);
        dc.DrawText(unit, new Point(_plot.Left - fs * 0.4 - unit.Width, _screen.Top + (_plot.Top - _screen.Top - unit.Height) / 2));

        if (ShowPhase)
        {
            var phaseBrush = VintageArt.Frozen(VintageArt.WithAlpha(PhaseColor, 0.85));
            foreach (var deg in new[] { -180, -90, 0, 90, 180 })
            {
                string text = deg > 0 ? $"+{deg}°" : deg < 0 ? $"−{-deg}°" : "0°";
                var ft = VintageArt.Text(text, fs, phaseBrush, FontWeights.SemiBold, ppd);
                dc.DrawText(ft, new Point(_plot.Right + fs * 0.4, PhaseY(deg) - ft.Height / 2));
            }
        }

        using (var g = _glass.RenderOpen()) PlotArt.DrawGlass(g, _screen);
    }

    private void DrawCurves()
    {
        using var dc = _curves.RenderOpen();
        if (_plot.Width < 2 || _plot.Height < 2) return;
        double fs = FontSize, ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var main = Response;
        var overlay = OverlayResponse;

        dc.PushClip(new RectangleGeometry(_plot));
        if (ShowPhase)
        {
            var phaseSource = main?.PhaseDegrees != null && main.IsValid ? main : overlay?.PhaseDegrees != null && overlay.IsValid ? overlay : null;
            if (phaseSource != null)
                dc.DrawGeometry(null, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(PhaseColor, 0.75)), 1.2), PhaseGeometry(phaseSource));
        }
        if (main != null && main.IsValid) DrawResponse(dc, main, TraceColor);
        if (overlay != null && overlay.IsValid) DrawResponse(dc, overlay, OverlayColor);
        dc.Pop();

        if (main != null && main.IsValid && ShowCutoffMarkers) DrawCutoffs(dc, main, fs, ppd);

        if ((main == null || !main.IsValid) && (overlay == null || !overlay.IsValid))
        {
            var ft = VintageArt.Text(main == null && overlay == null ? "NO RESPONSE" : "RESPONSE UNAVAILABLE", fs * 1.3, PlotArt.LabelBrush(150), FontWeights.Bold, ppd);
            dc.DrawText(ft, new Point(_plot.Left + (_plot.Width - ft.Width) / 2, _plot.Top + _plot.Height * 0.3));
        }

        // Legend
        double x = _plot.Left, y = _screen.Top + (_plot.Top - _screen.Top) / 2;
        foreach (var (snap, color) in new[] { (main, TraceColor), (overlay, OverlayColor) })
        {
            if (snap == null || !snap.IsValid) continue;
            var pen = LinePen(color, snap.IsMeasured, 1.8);
            dc.DrawLine(pen, new Point(x, y), new Point(x + fs * 2, y));
            var ft = VintageArt.Text(snap.IsMeasured ? "MEASURED" : "THEORETICAL", fs, VintageArt.Frozen(color), FontWeights.SemiBold, ppd);
            dc.DrawText(ft, new Point(x + fs * 2.4, y - ft.Height / 2));
            x += fs * 2.4 + ft.Width + fs * 1.4;
        }
        if (ShowPhase)
        {
            dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(PhaseColor), 1.4), new Point(x, y), new Point(x + fs * 2, y));
            var ft = VintageArt.Text("PHASE", fs, VintageArt.Frozen(PhaseColor), FontWeights.SemiBold, ppd);
            dc.DrawText(ft, new Point(x + fs * 2.4, y - ft.Height / 2));
        }
    }

    private static Pen LinePen(Color color, bool measured, double thickness)
    {
        var pen = new Pen(VintageArt.Frozen(color), thickness) { LineJoin = PenLineJoin.Round };
        if (measured) pen.DashStyle = new DashStyle(new[] { 3.0, 2.0 }, 0);
        pen.Freeze();
        return pen;
    }

    private void DrawResponse(DrawingContext dc, ResponseSnapshot r, Color color)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            bool open = false;
            int n = Math.Min(r.Frequencies.Length, r.GainDb.Length);
            for (int i = 0; i < n; i++)
            {
                double f = r.Frequencies[i], db = r.GainDb[i];
                if (!(f > 0) || double.IsNaN(db)) { open = false; continue; }
                var p = new Point(X(f), Y(db));
                if (!open) { ctx.BeginFigure(p, false, false); open = true; }
                else ctx.LineTo(p, true, true);
            }
        }
        g.Freeze();
        if (r.IsMeasured)
        {
            dc.DrawGeometry(null, LinePen(color, true, 1.6), g);
        }
        else
        {
            dc.DrawGeometry(null, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(color, 0.2)), 6, PenLineCap.Round), g);
            dc.DrawGeometry(null, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.Blend(color, Colors.White, 0.3)), 1.8, PenLineCap.Round), g);
        }
    }

    private Geometry PhaseGeometry(ResponseSnapshot r)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            bool open = false;
            double prev = double.NaN;
            int n = Math.Min(r.Frequencies.Length, r.PhaseDegrees!.Length);
            for (int i = 0; i < n; i++)
            {
                double f = r.Frequencies[i], ph = ResponseMath.WrapDegrees(r.PhaseDegrees[i]);
                if (!(f > 0) || double.IsNaN(ph)) { open = false; continue; }
                var p = new Point(X(f), PhaseY(ph));
                // Break the line at a ±180° wrap instead of drawing a vertical jump.
                if (!open || Math.Abs(ph - prev) > 180) { ctx.BeginFigure(p, false, false); open = true; }
                else ctx.LineTo(p, true, true);
                prev = ph;
            }
        }
        g.Freeze();
        return g;
    }

    private void DrawCutoffs(DrawingContext dc, ResponseSnapshot r, double fs, double ppd)
    {
        var crossings = ResponseMath.FindCrossings(r.Frequencies, r.GainDb, CutoffLevelDb);
        if (crossings.Count == 0 || crossings.Count > 6) return;
        var brush = VintageArt.Frozen(Colors.White);
        var text = VintageArt.Frozen(Color.FromArgb(230, 255, 255, 255));
        double y = Y(CutoffLevelDb);
        foreach (var f in crossings)
        {
            if (f < _axis.Minimum || f > _axis.Maximum) continue;
            double x = X(f), d = fs * 0.45;
            var diamond = new StreamGeometry();
            using (var ctx = diamond.Open())
            {
                ctx.BeginFigure(new Point(x, y - d), true, true);
                ctx.LineTo(new Point(x + d, y), true, false);
                ctx.LineTo(new Point(x, y + d), true, false);
                ctx.LineTo(new Point(x - d, y), true, false);
            }
            diamond.Freeze();
            dc.DrawGeometry(brush, VintageArt.FrozenPen(VintageArt.Frozen(Colors.Black), 0.8), diamond);
            string level = CutoffLevelDb.ToString("0.#;−0.#;0", CultureInfo.InvariantCulture);
            var ft = VintageArt.Text($"{level} dB · {FrequencyAxis.FormatHzWithUnit(f)}", fs * 0.9, text, FontWeights.SemiBold, ppd);
            double tx = Math.Clamp(x + d + 3, _plot.Left + 2, _plot.Right - ft.Width - 2);
            dc.DrawText(ft, new Point(tx, y + d));
        }
    }

    private Point HandlePosition(int index, EqBand band)
    {
        double y;
        if (band.HasGain) y = Y(band.GainDb);
        else
        {
            var r = Response;
            double g = r != null && r.IsValid ? ResponseMath.Interpolate(r.Frequencies, r.GainDb, band.FrequencyHz) : double.NaN;
            y = Y(double.IsNaN(g) ? 0 : Math.Clamp(g, MinimumDb, MaximumDb));
        }
        return new Point(Math.Clamp(X(band.FrequencyHz), _plot.Left, _plot.Right), Math.Clamp(y, _plot.Top, _plot.Bottom));
    }

    private void DrawHandles()
    {
        using var dc = _handles.RenderOpen();
        var bands = Bands;
        if (bands == null || bands.Count == 0 || _plot.Width < 2) return;
        double fs = FontSize, ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip, r = HandleRadius;
        int selected = SelectedBandIndex;

        if (selected >= 0 && selected < bands.Count)
        {
            double x = Math.Clamp(X(bands[selected].FrequencyHz), _plot.Left, _plot.Right);
            var pen = new Pen(VintageArt.Frozen(VintageArt.WithAlpha(BandColor(selected), 0.45)), 1) { DashStyle = new DashStyle(new[] { 3.0, 3.0 }, 0) };
            pen.Freeze();
            dc.DrawLine(pen, new Point(x, _plot.Top), new Point(x, _plot.Bottom));
        }

        for (int i = 0; i < bands.Count; i++)
        {
            var band = bands[i];
            var p = HandlePosition(i, band);
            var c = BandColor(i);
            bool sel = i == selected;
            if (sel) dc.DrawEllipse(VintageArt.Frozen(VintageArt.WithAlpha(c, 0.25)), null, p, r * 1.9, r * 1.9);
            if (band.IsEnabled)
            {
                var fill = new RadialGradientBrush(VintageArt.Blend(c, Colors.White, 0.45), VintageArt.Blend(c, Colors.Black, 0.25))
                { GradientOrigin = new Point(0.35, 0.3) };
                fill.Freeze();
                dc.DrawEllipse(fill, VintageArt.FrozenPen(VintageArt.Frozen(sel ? Colors.White : Color.FromRgb(0x10, 0x10, 0x10)), sel ? 2 : 1.2), p, r, r);
            }
            else
            {
                dc.DrawEllipse(VintageArt.Frozen(Color.FromArgb(200, 0x08, 0x10, 0x0A)), VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(c, 0.6)), 1.6), p, r, r);
            }
            var num = VintageArt.Text((i + 1).ToString(CultureInfo.InvariantCulture), r * 1.25,
                VintageArt.Frozen(band.IsEnabled ? Color.FromRgb(0x10, 0x14, 0x10) : VintageArt.WithAlpha(c, 0.8)), FontWeights.Bold, ppd);
            VintageArt.DrawCenteredText(dc, num, p);
        }

        if (selected >= 0 && selected < bands.Count)
        {
            var b = bands[selected];
            var ci = CultureInfo.InvariantCulture;
            string type = b.Type switch
            {
                EqBandType.LowShelf => "LOW SHELF",
                EqBandType.HighShelf => "HIGH SHELF",
                EqBandType.HighPass => "HIGH PASS",
                EqBandType.LowPass => "LOW PASS",
                _ => "PEAK",
            };
            string text = $"{selected + 1} · {type}   {FrequencyAxis.FormatHzWithUnit(b.FrequencyHz)}"
                + (b.HasGain ? $"   {b.GainDb.ToString("+0.0;−0.0;0.0", ci)} dB" : "")
                + $"   Q {b.Q.ToString("0.00", ci)}" + (b.IsEnabled ? "" : "   BYPASSED");
            var ft = VintageArt.Text(text, fs, VintageArt.Frozen(BandColor(selected)), FontWeights.SemiBold, ppd);
            dc.DrawText(ft, new Point(_plot.Right - ft.Width, _screen.Top + (_plot.Top - _screen.Top - ft.Height) / 2));
        }
    }

    private static Color BandColor(int i) => BandPalette[i % BandPalette.Length];

    // ---------------------------------------------------------------- input

    private int HitHandle(Point p)
    {
        var bands = Bands;
        if (bands == null) return -1;
        double best = HandleRadius + 5, bestD = double.MaxValue;
        int hit = -1;
        // Topmost (last drawn) wins ties.
        for (int i = bands.Count - 1; i >= 0; i--)
        {
            var h = HandlePosition(i, bands[i]);
            double d = (h - p).Length;
            if (d <= best && d < bestD) { bestD = d; hit = i; }
        }
        return hit;
    }

    private EqBand? BandAt(int index) => Bands is { } b && index >= 0 && index < b.Count ? b[index] : null;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (!IsEditable) return;
        int i = HitHandle(e.GetPosition(this));
        if (i < 0) return;
        SelectedBandIndex = i;
        var band = BandAt(i)!;
        if (e.ClickCount == 2)
        {
            if (band.HasGain) RequestBandChange(i, band with { GainDb = 0 });
        }
        else
        {
            _dragIndex = i;
            CaptureMouse();
        }
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var p = e.GetPosition(this);
        if (_dragIndex >= 0 && BandAt(_dragIndex) is { } band)
        {
            var requested = band with { FrequencyHz = FrequencyAt(p.X) };
            if (band.HasGain) requested = requested with { GainDb = Math.Round(GainAt(p.Y), 1) };
            RequestBandChange(_dragIndex, requested);
            return;
        }
        Cursor = IsEditable && HitHandle(p) >= 0 ? Cursors.Hand : null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragIndex >= 0)
        {
            _dragIndex = -1;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragIndex = -1;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (!IsEditable) return;
        int i = HitHandle(e.GetPosition(this));
        if (i < 0) return;
        SelectedBandIndex = i;
        var band = BandAt(i)!;
        RequestBandChange(i, band with { IsEnabled = !band.IsEnabled });
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsEditable) return;
        int i = HitHandle(e.GetPosition(this));
        if (i < 0) i = SelectedBandIndex;
        if (BandAt(i) is not { } band) return;
        SelectedBandIndex = i;
        RequestBandChange(i, band with { Q = band.Q * Math.Pow(1.12, e.Delta / 120.0) });
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!IsEditable || Bands is not { Count: > 0 } bands) return;
        bool fine = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        if (e.Key is >= Key.D1 and <= Key.D9 || e.Key is >= Key.NumPad1 and <= Key.NumPad9)
        {
            int n = e.Key >= Key.NumPad1 ? e.Key - Key.NumPad1 : e.Key - Key.D1;
            if (n < bands.Count) SelectedBandIndex = n;
            e.Handled = true;
            return;
        }
        if (BandAt(SelectedBandIndex) is not { } band) return;
        double semis = fine ? 0.25 : 1;
        EqBand? requested = e.Key switch
        {
            Key.Left => band with { FrequencyHz = band.FrequencyHz * Math.Pow(2, -semis / 12) },
            Key.Right => band with { FrequencyHz = band.FrequencyHz * Math.Pow(2, semis / 12) },
            Key.Up when band.HasGain => band with { GainDb = Math.Round(band.GainDb + (fine ? 0.1 : 0.5), 2) },
            Key.Down when band.HasGain => band with { GainDb = Math.Round(band.GainDb - (fine ? 0.1 : 0.5), 2) },
            Key.PageUp => band with { Q = band.Q * 1.12 },
            Key.PageDown => band with { Q = band.Q / 1.12 },
            Key.Space => band with { IsEnabled = !band.IsEnabled },
            Key.D0 or Key.NumPad0 when band.HasGain => band with { GainDb = 0 },
            _ => null,
        };
        if (requested == null) return;
        RequestBandChange(SelectedBandIndex, requested);
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new FrequencyResponseGraphAutomationPeer(this);
}

public sealed class FrequencyResponseGraphAutomationPeer : FrameworkElementAutomationPeer
{
    public FrequencyResponseGraphAutomationPeer(FrequencyResponseGraph owner) : base(owner) { }
    protected override string GetClassNameCore() => nameof(FrequencyResponseGraph);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
    protected override bool IsKeyboardFocusableCore() => true;
    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        if (!string.IsNullOrEmpty(name)) return name;
        var g = (FrequencyResponseGraph)Owner;
        int bands = g.Bands?.Count ?? 0;
        string kind = g.Response == null ? "no response" : g.Response.IsMeasured ? "measured response" : "theoretical response";
        return $"Frequency response graph, {kind}, {bands} editable bands";
    }
}
