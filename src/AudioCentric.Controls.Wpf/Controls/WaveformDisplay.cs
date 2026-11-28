using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AudioCentric.Controls.Plotting;
using AudioCentric.Controls.Wpf.Rendering;

namespace AudioCentric.Controls.Wpf.Controls;

/// <summary>
/// Audio waveform display for navigation and editing. It has channel lanes, a time ruler, zoom, scroll, a playhead
/// and a selection. At broad zoom it draws a peak-preserving min/max envelope per device-pixel column. Zoomed in,
/// it draws the individual samples. Each lane is one compact geometry; there is never an element per sample.
/// The host supplies an <see cref="IWaveformSource"/> and keeps decoding and storage.
/// <para>
/// Mouse: click to set the playhead, drag to select, wheel to zoom around the pointer, Shift+wheel or right-drag to
/// scroll, Ctrl+wheel for amplitude zoom. Keys: ←/→ scroll, +/− zoom, Home/End, 0 = zoom to fit, Esc clears the selection.
/// </para>
/// </summary>
public class WaveformDisplay : FrameworkElement
{
    private const double RulerHeight = 20;
    private const double LaneGap = 3;

    private readonly VisualCollection _children;
    private readonly DrawingVisual _background = new();
    private readonly DrawingVisual _trace = new();
    private readonly DrawingVisual _overlay = new();

    private float[] _lo = Array.Empty<float>(), _hi = Array.Empty<float>(), _samples = Array.Empty<float>();
    private bool _traceDirty = true;
    private Size _size; // arranged size (RenderSize is not updated until ArrangeOverride returns)
    private IWaveformSource? _subscribed;

    private enum DragMode { None, Select, Pan }
    private DragMode _drag;
    private double _dragAnchorSample;
    private Point _dragStartPoint;
    private double _panStartView;

    static WaveformDisplay()
    {
        FocusableProperty.OverrideMetadata(typeof(WaveformDisplay), new FrameworkPropertyMetadata(true));
        FocusVisualStyleProperty.OverrideMetadata(typeof(WaveformDisplay), new FrameworkPropertyMetadata(null));
        CursorProperty.OverrideMetadata(typeof(WaveformDisplay), new FrameworkPropertyMetadata(Cursors.IBeam));
    }

    public WaveformDisplay()
    {
        _children = new VisualCollection(this) { _background, _trace, _overlay };
    }

    #region Dependency properties

    public IWaveformSource? Source { get => (IWaveformSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(IWaveformSource), typeof(WaveformDisplay), new FrameworkPropertyMetadata(null, OnSourceChanged));

    /// <summary>First visible sample (fractional allowed). With <see cref="VisibleEndSample"/> ≤ start, the whole source is shown.</summary>
    public double VisibleStartSample { get => (double)GetValue(VisibleStartSampleProperty); set => SetValue(VisibleStartSampleProperty, value); }
    public static readonly DependencyProperty VisibleStartSampleProperty = DependencyProperty.Register(
        nameof(VisibleStartSample), typeof(double), typeof(WaveformDisplay),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnViewChanged));

    public double VisibleEndSample { get => (double)GetValue(VisibleEndSampleProperty); set => SetValue(VisibleEndSampleProperty, value); }
    public static readonly DependencyProperty VisibleEndSampleProperty = DependencyProperty.Register(
        nameof(VisibleEndSample), typeof(double), typeof(WaveformDisplay),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnViewChanged));

    /// <summary>Playhead position in samples; NaN hides it.</summary>
    public double Playhead { get => (double)GetValue(PlayheadProperty); set => SetValue(PlayheadProperty, value); }
    public static readonly DependencyProperty PlayheadProperty = DependencyProperty.Register(
        nameof(Playhead), typeof(double), typeof(WaveformDisplay),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((WaveformDisplay)d).DrawOverlay()));

    public double SelectionStart { get => (double)GetValue(SelectionStartProperty); set => SetValue(SelectionStartProperty, value); }
    public static readonly DependencyProperty SelectionStartProperty = DependencyProperty.Register(
        nameof(SelectionStart), typeof(double), typeof(WaveformDisplay),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((WaveformDisplay)d).DrawOverlay()));

    public double SelectionEnd { get => (double)GetValue(SelectionEndProperty); set => SetValue(SelectionEndProperty, value); }
    public static readonly DependencyProperty SelectionEndProperty = DependencyProperty.Register(
        nameof(SelectionEnd), typeof(double), typeof(WaveformDisplay),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((WaveformDisplay)d).DrawOverlay()));

    /// <summary>Vertical zoom: 1 shows −1…+1 full scale.</summary>
    public double AmplitudeScale { get => (double)GetValue(AmplitudeScaleProperty); set => SetValue(AmplitudeScaleProperty, value); }
    public static readonly DependencyProperty AmplitudeScaleProperty = DependencyProperty.Register(
        nameof(AmplitudeScale), typeof(double), typeof(WaveformDisplay),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnViewChanged, (_, v) => Math.Clamp((double)v, 0.25, 64)));

    public Color TraceColor { get => (Color)GetValue(TraceColorProperty); set => SetValue(TraceColorProperty, value); }
    public static readonly DependencyProperty TraceColorProperty = DependencyProperty.Register(
        nameof(TraceColor), typeof(Color), typeof(WaveformDisplay), new FrameworkPropertyMetadata(Color.FromRgb(0x6C, 0xF2, 0x4A), OnViewChanged));

    public bool ShowRuler { get => (bool)GetValue(ShowRulerProperty); set => SetValue(ShowRulerProperty, value); }
    public static readonly DependencyProperty ShowRulerProperty = DependencyProperty.Register(
        nameof(ShowRuler), typeof(bool), typeof(WaveformDisplay), new FrameworkPropertyMetadata(true, OnLayoutChanged));

    #endregion

    /// <summary>The visible range actually in use, after defaults and clamping.</summary>
    public (double Start, double End) EffectiveView
    {
        get
        {
            long len = Source?.Length ?? 0;
            double s = VisibleStartSample, e = VisibleEndSample;
            if (len <= 0) return (0, 1);
            if (!(e > s)) { s = 0; e = len; }
            return (s, e);
        }
    }

    public void ZoomToFit()
    {
        SetCurrentValue(VisibleStartSampleProperty, 0.0);
        SetCurrentValue(VisibleEndSampleProperty, (double)(Source?.Length ?? 0));
    }

    /// <summary>Zooms by <paramref name="factor"/> (&lt;1 zooms in) keeping <paramref name="anchorSample"/> fixed on screen.</summary>
    public void Zoom(double factor, double anchorSample)
    {
        long len = Source?.Length ?? 0;
        if (len <= 0) return;
        var (s, e) = EffectiveView;
        double span = Math.Clamp((e - s) * factor, 8, len);
        double t = (anchorSample - s) / (e - s);
        double ns = anchorSample - t * span;
        SetView(ns, span);
    }

    /// <summary>Scrolls by a number of samples.</summary>
    public void Scroll(double samples)
    {
        var (s, e) = EffectiveView;
        SetView(s + samples, e - s);
    }

    private void SetView(double start, double span)
    {
        long len = Source?.Length ?? 0;
        span = Math.Min(span, len);
        start = Math.Clamp(start, 0, Math.Max(0, len - span));
        SetCurrentValue(VisibleStartSampleProperty, start);
        SetCurrentValue(VisibleEndSampleProperty, start + span);
    }

    private static void OnSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var w = (WaveformDisplay)d;
        if (w._subscribed != null) w._subscribed.Changed -= w.OnSourceDataChanged;
        w._subscribed = e.NewValue as IWaveformSource;
        if (w._subscribed != null) w._subscribed.Changed += w.OnSourceDataChanged;
        w.InvalidateArrange();
        w.QueueTrace();
    }

    private void OnSourceDataChanged(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(QueueTrace));

    private static void OnViewChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WaveformDisplay)d).QueueTrace();

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((WaveformDisplay)d).InvalidateArrange();

    /// <summary>Marks the trace dirty. WPF coalesces invalidations, so many changes (e.g. while dragging) cost one redraw per frame.</summary>
    private void QueueTrace()
    {
        _traceDirty = true;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        if (_traceDirty) DrawTrace();
        DrawOverlay();
    }

    // ---------------------------------------------------------------- layout

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 600 : availableSize.Width, double.IsInfinity(availableSize.Height) ? 200 : availableSize.Height);

    protected override Size ArrangeOverride(Size finalSize)
    {
        _size = finalSize;
        DrawBackground(finalSize);
        DrawTrace();
        DrawOverlay();
        return finalSize;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;

    private double Top => ShowRuler ? RulerHeight : 0;
    private int Channels => Math.Max(1, Source?.ChannelCount ?? 1);

    private Rect LaneRect(int ch, Size size)
    {
        double h = (size.Height - Top - LaneGap * (Channels - 1)) / Channels;
        return new Rect(0, Top + ch * (h + LaneGap), size.Width, Math.Max(1, h));
    }

    private double XOf(double sample, double s, double e, double width) => (sample - s) / (e - s) * width;
    private double SampleOf(double x)
    {
        var (s, e) = EffectiveView;
        return s + x / Math.Max(1, _size.Width) * (e - s);
    }

    // ---------------------------------------------------------------- drawing

    private void DrawBackground(Size size)
    {
        using var dc = _background.RenderOpen();
        var panel = new LinearGradientBrush(Color.FromRgb(0x0B, 0x12, 0x0C), Color.FromRgb(0x06, 0x09, 0x06), 90);
        panel.Freeze();
        dc.DrawRoundedRectangle(panel, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0x2C, 0x32, 0x2C)), 1), new Rect(size), 4, 4);
        if (ShowRuler)
            dc.DrawRectangle(VintageArt.Frozen(Color.FromRgb(0x12, 0x15, 0x12)), null, new Rect(0, 0, size.Width, RulerHeight));

        var grid = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(40, 0x6C, 0xF2, 0x4A)), 1);
        var zero = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(90, 0x6C, 0xF2, 0x4A)), 1);
        for (int ch = 0; ch < Channels; ch++)
        {
            var r = LaneRect(ch, size);
            double c = Math.Round(r.Y + r.Height / 2) + 0.5;
            dc.DrawLine(zero, new Point(0, c), new Point(size.Width, c));
            foreach (double f in new[] { 0.5, -0.5 })
            {
                double y = Math.Round(c - f * r.Height / 2) + 0.5;
                dc.DrawLine(grid, new Point(0, y), new Point(size.Width, y));
            }
            if (ch > 0) dc.DrawRectangle(VintageArt.Frozen(Color.FromRgb(0x1A, 0x1E, 0x1A)), null, new Rect(0, r.Y - LaneGap, size.Width, LaneGap));
        }
    }

    private void DrawTrace()
    {
        _traceDirty = false;
        var size = _size;
        using var dc = _trace.RenderOpen();
        var src = Source;
        if (src == null || src.Length == 0 || size.Width < 2 || size.Height < 2) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        int columns = Math.Max(1, (int)Math.Round(size.Width * dpi.DpiScaleX));
        double colWidth = size.Width / columns;
        var (s, e) = EffectiveView;
        double visible = e - s;
        var color = TraceColor;
        var fill = VintageArt.Frozen(VintageArt.WithAlpha(color, 0.85));
        var pen = VintageArt.FrozenPen(VintageArt.Frozen(color), 1.0 / dpi.DpiScaleX);
        double amp = AmplitudeScale;

        for (int ch = 0; ch < src.ChannelCount; ch++)
        {
            var lane = LaneRect(ch, size);
            double cy = lane.Y + lane.Height / 2, half = lane.Height / 2 * 0.94;
            double Y(double v) => Math.Clamp(cy - v * amp * half, lane.Top, lane.Bottom);
            dc.PushClip(new RectangleGeometry(lane));

            if (!MinMaxBucketer.ShouldDrawSamples((long)Math.Ceiling(visible), columns))
            {
                if (_lo.Length != columns) { _lo = new float[columns]; _hi = new float[columns]; }
                src.GetMinMax(ch, (long)Math.Floor(s), (long)Math.Ceiling(e), _lo, _hi);
                // Envelope polygon: top edge left→right along the maxima, bottom edge back along the minima.
                // NaN (empty) buckets split the figure so nothing is invented.
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    int i = 0;
                    while (i < columns)
                    {
                        while (i < columns && float.IsNaN(_hi[i])) i++;
                        int j = i;
                        while (j < columns && !float.IsNaN(_hi[j])) j++;
                        if (j > i)
                        {
                            ctx.BeginFigure(new Point((i + 0.5) * colWidth, Y(_hi[i])), true, true);
                            for (int k = i + 1; k < j; k++) ctx.LineTo(new Point((k + 0.5) * colWidth, Y(_hi[k])), true, false);
                            for (int k = j - 1; k >= i; k--) ctx.LineTo(new Point((k + 0.5) * colWidth, Y(_lo[k])), true, false);
                        }
                        i = j;
                    }
                }
                g.Freeze();
                dc.DrawGeometry(fill, pen, g);
            }
            else
            {
                long first = (long)Math.Floor(s), last = (long)Math.Ceiling(e);
                int n = (int)Math.Min(last - first + 1, src.Length - first);
                if (n > 0)
                {
                    if (_samples.Length < n) _samples = new float[n];
                    src.GetSamples(ch, first, _samples.AsSpan(0, n));
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        for (int k = 0; k < n; k++)
                        {
                            var p = new Point(XOf(first + k, s, e, size.Width), Y(_samples[k]));
                            if (k == 0) ctx.BeginFigure(p, false, false); else ctx.LineTo(p, true, true);
                        }
                    }
                    g.Freeze();
                    dc.DrawGeometry(null, VintageArt.FrozenPen(VintageArt.Frozen(color), 1.4), g);
                    double pxPerSample = size.Width / visible;
                    if (pxPerSample >= 6)
                    {
                        var dot = VintageArt.Frozen(VintageArt.Blend(color, Colors.White, 0.4));
                        for (int k = 0; k < n; k++)
                            dc.DrawEllipse(dot, null, new Point(XOf(first + k, s, e, size.Width), Y(_samples[k])), 2, 2);
                    }
                }
            }
            dc.Pop();
        }

        if (ShowRuler) DrawRuler(dc, size, s, e, src.SampleRate, dpi.PixelsPerDip);
    }

    private static readonly double[] NiceSteps = Steps125.Range(1e-5, 3600);

    private void DrawRuler(DrawingContext dc, Size size, double s, double e, int rate, double ppd)
    {
        double secondsPerPx = (e - s) / rate / size.Width;
        double target = secondsPerPx * 90;
        double step = NiceSteps.FirstOrDefault(v => v >= target, NiceSteps[^1]);
        double t0 = Math.Ceiling(s / rate / step) * step;
        var ink = VintageArt.Frozen(Color.FromRgb(0x9A, 0xA8, 0x9E));
        var tick = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0x5A, 0x64, 0x5A)), 1);
        int decimals = Math.Max(0, (int)Math.Ceiling(-Math.Log10(step) - 1e-9));
        for (double t = t0; t * rate <= e; t += step)
        {
            double x = Math.Round(XOf(t * rate, s, e, size.Width)) + 0.5;
            dc.DrawLine(tick, new Point(x, RulerHeight - 6), new Point(x, RulerHeight));
            string label = FormatTime(t, decimals);
            var ft = VintageArt.Text(label, 10.5, ink, FontWeights.Normal, ppd);
            dc.DrawText(ft, new Point(x + 3, 3));
        }
    }

    private static string FormatTime(double t, int decimals)
    {
        var ci = CultureInfo.InvariantCulture;
        int minutes = (int)(t / 60);
        double sec = t - minutes * 60;
        string secText = sec.ToString("F" + decimals, ci);
        return minutes > 0 ? $"{minutes}:{(sec < 10 ? "0" : "")}{secText}" : secText + "s";
    }

    private void DrawOverlay()
    {
        var size = _size;
        using var dc = _overlay.RenderOpen();
        if (Source == null || size.Width < 2) return;
        var (s, e) = EffectiveView;
        double top = Top;

        double a = SelectionStart, b = SelectionEnd;
        if (!double.IsNaN(a) && !double.IsNaN(b) && b != a)
        {
            double x0 = Math.Clamp(XOf(Math.Min(a, b), s, e, size.Width), 0, size.Width);
            double x1 = Math.Clamp(XOf(Math.Max(a, b), s, e, size.Width), 0, size.Width);
            if (x1 > x0)
            {
                dc.DrawRectangle(VintageArt.Frozen(Color.FromArgb(60, 0xB8, 0xFF, 0x9C)), null, new Rect(x0, top, x1 - x0, size.Height - top));
                var edge = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(160, 0xB8, 0xFF, 0x9C)), 1);
                dc.DrawLine(edge, new Point(x0, top), new Point(x0, size.Height));
                dc.DrawLine(edge, new Point(x1, top), new Point(x1, size.Height));
            }
        }

        double p = Playhead;
        if (!double.IsNaN(p))
        {
            double x = XOf(p, s, e, size.Width);
            if (x >= 0 && x <= size.Width)
            {
                var head = VintageArt.Frozen(Color.FromRgb(0xFF, 0xD2, 0x1E));
                dc.DrawLine(VintageArt.FrozenPen(head, 1.5), new Point(x, top), new Point(x, size.Height));
                var tri = new StreamGeometry();
                using (var ctx = tri.Open())
                {
                    ctx.BeginFigure(new Point(x - 6, Math.Max(0, top - 8)), true, true);
                    ctx.LineTo(new Point(x + 6, Math.Max(0, top - 8)), true, false);
                    ctx.LineTo(new Point(x, top), true, false);
                }
                tri.Freeze();
                dc.DrawGeometry(head, null, tri);
            }
        }

        if (IsKeyboardFocused)
        {
            var focus = new Pen(VintageArt.Frozen(Color.FromArgb(150, 0x6C, 0xF2, 0x4A)), 1) { DashStyle = DashStyles.Dash };
            focus.Freeze();
            dc.DrawRoundedRectangle(null, focus, new Rect(0.5, 0.5, size.Width - 1, size.Height - 1), 4, 4);
        }
    }

    // ---------------------------------------------------------------- input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (Source == null) return;
        _dragStartPoint = e.GetPosition(this);
        _dragAnchorSample = SampleOf(_dragStartPoint.X);
        _drag = DragMode.Select;
        CaptureMouse();
        SetCurrentValue(PlayheadProperty, Math.Clamp(_dragAnchorSample, 0, Source.Length));
        SetCurrentValue(SelectionStartProperty, double.NaN);
        SetCurrentValue(SelectionEndProperty, double.NaN);
        e.Handled = true;
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        Focus();
        _dragStartPoint = e.GetPosition(this);
        _panStartView = EffectiveView.Start;
        _drag = DragMode.Pan;
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_drag == DragMode.None || Source == null) return;
        var p = e.GetPosition(this);
        if (_drag == DragMode.Select && Math.Abs(p.X - _dragStartPoint.X) >= 3)
        {
            double cur = Math.Clamp(SampleOf(p.X), 0, Source.Length);
            SetCurrentValue(SelectionStartProperty, Math.Min(_dragAnchorSample, cur));
            SetCurrentValue(SelectionEndProperty, Math.Max(_dragAnchorSample, cur));
        }
        else if (_drag == DragMode.Pan)
        {
            var (s, end) = EffectiveView;
            double perPx = (end - s) / Math.Max(1, _size.Width);
            SetView(_panStartView - (p.X - _dragStartPoint.X) * perPx, end - s);
        }
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_drag != DragMode.None)
        {
            _drag = DragMode.None;
            ReleaseMouseCapture();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Source == null) return;
        var mods = Keyboard.Modifiers;
        int dir = Math.Sign(e.Delta);
        if (mods.HasFlag(ModifierKeys.Control))
            SetCurrentValue(AmplitudeScaleProperty, AmplitudeScale * (dir > 0 ? 1.25 : 0.8));
        else if (mods.HasFlag(ModifierKeys.Shift))
            Scroll(-dir * (EffectiveView.End - EffectiveView.Start) * 0.15);
        else
            Zoom(dir > 0 ? 0.8 : 1.25, SampleOf(e.GetPosition(this).X));
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Source == null) return;
        var (s, end) = EffectiveView;
        double span = end - s;
        double anchor = double.IsNaN(Playhead) ? (s + end) / 2 : Playhead;
        switch (e.Key)
        {
            case Key.Left: Scroll(-span * 0.1); break;
            case Key.Right: Scroll(span * 0.1); break;
            case Key.Add: case Key.OemPlus: Zoom(0.8, anchor); break;
            case Key.Subtract: case Key.OemMinus: Zoom(1.25, anchor); break;
            case Key.Home: SetView(0, span); break;
            case Key.End: SetView(Source.Length - span, span); break;
            case Key.D0: case Key.NumPad0: ZoomToFit(); break;
            case Key.Escape:
                SetCurrentValue(SelectionStartProperty, double.NaN);
                SetCurrentValue(SelectionEndProperty, double.NaN);
                break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); DrawOverlay(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); DrawOverlay(); }

    protected override AutomationPeer OnCreateAutomationPeer() => new WaveformDisplayAutomationPeer(this);
}

public sealed class WaveformDisplayAutomationPeer : FrameworkElementAutomationPeer
{
    public WaveformDisplayAutomationPeer(WaveformDisplay owner) : base(owner) { }
    protected override string GetClassNameCore() => nameof(WaveformDisplay);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;

    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        if (!string.IsNullOrEmpty(name)) return name;
        var w = (WaveformDisplay)Owner;
        var src = w.Source;
        if (src == null) return "Waveform (empty)";
        var (s, e) = w.EffectiveView;
        return string.Format(CultureInfo.InvariantCulture, "Waveform, {0} channel(s), {1:0.00} s, showing {2:0.000}–{3:0.000} s",
            src.ChannelCount, (double)src.Length / src.SampleRate, s / src.SampleRate, e / src.SampleRate);
    }
}
