using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Media;
using AudioCentric.Controls.Measurements;
using AudioCentric.Controls.Plotting;
using AudioCentric.Controls.Wpf.Rendering;

namespace AudioCentric.Controls.Wpf.Controls;

/// <summary>
/// CRT-style oscilloscope. The host pushes recent history (<see cref="TraceSnapshot"/>, newest sample last) each frame.
/// The control owns the timebase, triggering (<see cref="ScopeTrigger"/> in Core: Free, Auto, Normal, Single, with
/// level, slope, position and holdoff), freeze, phosphor persistence and the graticule.
/// Channel 2 (<see cref="Trace2"/>) must be sample-aligned with channel 1. Set it before <see cref="Trace"/>, because a new
/// <see cref="Trace"/> triggers processing. History should cover at least two screens.
/// </summary>
public class Oscilloscope : FrameworkElement
{
    // Design space: 1000 × 820, screen 900 × 720 (10 × 8 divisions of 90 units) at (50, 50).
    private const double DesignW = 1000, DesignH = 820;
    private const double SX = 50, SY = 50, SW = 900, SH = 720;
    private const int MaxPersistence = 8;

    private readonly VisualCollection _children;
    private readonly ContainerVisual _root = new();
    private readonly DrawingVisual _static = new();
    private readonly DrawingVisual _traces = new();
    private readonly DrawingVisual _overlay = new();
    private readonly DrawingVisual _glass = new();
    private readonly MatrixTransform _rootTransform = new();

    private readonly ScopeTrigger _trigger = new();
    private readonly LinkedList<(Geometry Ch1, Geometry? Ch2)> _history = new();
    private float[] _lo = new float[(int)SW], _hi = new float[(int)SW];
    private ScopeStatus _drawnStatus = (ScopeStatus)(-1);

    static Oscilloscope()
    {
        CommandManager.RegisterClassCommandBinding(typeof(Oscilloscope),
            new CommandBinding(ArmCommand, (s, _) => ((Oscilloscope)s).Arm()));
    }

    public Oscilloscope()
    {
        _children = new VisualCollection(this) { _root };
        _root.Transform = _rootTransform;
        _root.Children.Add(_static);
        var screen = new ContainerVisual { Clip = new RectangleGeometry(new Rect(SX, SY, SW, SH), 6, 6) };
        screen.Children.Add(_traces);
        _root.Children.Add(screen);
        _root.Children.Add(_overlay);
        _root.Children.Add(_glass);
        DrawStatic();
        DrawGlass();
        DrawOverlay();
    }

    public static readonly RoutedCommand ArmCommand = new(nameof(ArmCommand), typeof(Oscilloscope));

    #region Dependency properties

    public TraceSnapshot? Trace { get => (TraceSnapshot?)GetValue(TraceProperty); set => SetValue(TraceProperty, value); }
    public static readonly DependencyProperty TraceProperty = DependencyProperty.Register(
        nameof(Trace), typeof(TraceSnapshot), typeof(Oscilloscope), new FrameworkPropertyMetadata(null, (d, _) => ((Oscilloscope)d).Process()));

    public TraceSnapshot? Trace2 { get => (TraceSnapshot?)GetValue(Trace2Property); set => SetValue(Trace2Property, value); }
    public static readonly DependencyProperty Trace2Property = DependencyProperty.Register(
        nameof(Trace2), typeof(TraceSnapshot), typeof(Oscilloscope), new FrameworkPropertyMetadata(null));

    public double TimePerDivision { get => (double)GetValue(TimePerDivisionProperty); set => SetValue(TimePerDivisionProperty, value); }
    public static readonly DependencyProperty TimePerDivisionProperty = DependencyProperty.Register(
        nameof(TimePerDivision), typeof(double), typeof(Oscilloscope), new FrameworkPropertyMetadata(0.001, OnSettingsChanged), v => (double)v > 0);

    /// <summary>Amplitude per vertical division in full-scale units (1.0 = digital full scale).</summary>
    public double AmplitudePerDivision { get => (double)GetValue(AmplitudePerDivisionProperty); set => SetValue(AmplitudePerDivisionProperty, value); }
    public static readonly DependencyProperty AmplitudePerDivisionProperty = DependencyProperty.Register(
        nameof(AmplitudePerDivision), typeof(double), typeof(Oscilloscope), new FrameworkPropertyMetadata(0.25, OnSettingsChanged), v => (double)v > 0);

    public TriggerMode TriggerMode { get => (TriggerMode)GetValue(TriggerModeProperty); set => SetValue(TriggerModeProperty, value); }
    public static readonly DependencyProperty TriggerModeProperty = DependencyProperty.Register(
        nameof(TriggerMode), typeof(TriggerMode), typeof(Oscilloscope), new FrameworkPropertyMetadata(TriggerMode.Auto, OnTriggerModeChanged));

    public TriggerSlope TriggerSlope { get => (TriggerSlope)GetValue(TriggerSlopeProperty); set => SetValue(TriggerSlopeProperty, value); }
    public static readonly DependencyProperty TriggerSlopeProperty = DependencyProperty.Register(
        nameof(TriggerSlope), typeof(TriggerSlope), typeof(Oscilloscope), new FrameworkPropertyMetadata(TriggerSlope.Rising, OnSettingsChanged));

    /// <summary>Trigger level in full-scale units.</summary>
    public double TriggerLevel { get => (double)GetValue(TriggerLevelProperty); set => SetValue(TriggerLevelProperty, value); }
    public static readonly DependencyProperty TriggerLevelProperty = DependencyProperty.Register(
        nameof(TriggerLevel), typeof(double), typeof(Oscilloscope), new FrameworkPropertyMetadata(0.0, OnSettingsChanged));

    /// <summary>Horizontal trigger position, 0 (left edge) … 1 (right edge). Default 0.5.</summary>
    public double TriggerPosition { get => (double)GetValue(TriggerPositionProperty); set => SetValue(TriggerPositionProperty, value); }
    public static readonly DependencyProperty TriggerPositionProperty = DependencyProperty.Register(
        nameof(TriggerPosition), typeof(double), typeof(Oscilloscope), new FrameworkPropertyMetadata(0.5, OnSettingsChanged));

    public double HoldoffSeconds { get => (double)GetValue(HoldoffSecondsProperty); set => SetValue(HoldoffSecondsProperty, value); }
    public static readonly DependencyProperty HoldoffSecondsProperty = DependencyProperty.Register(
        nameof(HoldoffSeconds), typeof(double), typeof(Oscilloscope), new FrameworkPropertyMetadata(0.0));

    /// <summary>Freeze the display (new data is ignored).</summary>
    public bool IsFrozen { get => (bool)GetValue(IsFrozenProperty); set => SetValue(IsFrozenProperty, value); }
    public static readonly DependencyProperty IsFrozenProperty = DependencyProperty.Register(
        nameof(IsFrozen), typeof(bool), typeof(Oscilloscope), new FrameworkPropertyMetadata(false, (d, _) => ((Oscilloscope)d).DrawOverlay()));

    /// <summary>Number of previous traces kept as fading phosphor afterglow (0–8).</summary>
    public int Persistence { get => (int)GetValue(PersistenceProperty); set => SetValue(PersistenceProperty, value); }
    public static readonly DependencyProperty PersistenceProperty = DependencyProperty.Register(
        nameof(Persistence), typeof(int), typeof(Oscilloscope), new FrameworkPropertyMetadata(3, null, (_, v) => Math.Clamp((int)v, 0, MaxPersistence)));

    public Color TraceColor { get => (Color)GetValue(TraceColorProperty); set => SetValue(TraceColorProperty, value); }
    public static readonly DependencyProperty TraceColorProperty = DependencyProperty.Register(
        nameof(TraceColor), typeof(Color), typeof(Oscilloscope), new FrameworkPropertyMetadata(Color.FromRgb(0x7C, 0xFF, 0x5A), OnAppearanceChanged));

    public Color Trace2Color { get => (Color)GetValue(Trace2ColorProperty); set => SetValue(Trace2ColorProperty, value); }
    public static readonly DependencyProperty Trace2ColorProperty = DependencyProperty.Register(
        nameof(Trace2Color), typeof(Color), typeof(Oscilloscope), new FrameworkPropertyMetadata(Color.FromRgb(0xFF, 0xC0, 0x3A), OnAppearanceChanged));

    public ScopeStatus Status => (ScopeStatus)GetValue(StatusProperty);
    private static readonly DependencyPropertyKey StatusPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Status), typeof(ScopeStatus), typeof(Oscilloscope), new FrameworkPropertyMetadata(ScopeStatus.Waiting));
    public static readonly DependencyProperty StatusProperty = StatusPropertyKey.DependencyProperty;

    #endregion

    /// <summary>Re-arm Single mode (also clears holdoff history).</summary>
    public void Arm()
    {
        _trigger.Arm();
        SetValue(StatusPropertyKey, _trigger.Status);
        DrawOverlay();
    }

    /// <summary>Clears the afterglow.</summary>
    public void ClearPersistence()
    {
        _history.Clear();
        DrawTraces();
    }

    private static void OnSettingsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var o = (Oscilloscope)d;
        // A new timebase or scale makes old afterglow meaningless.
        if (e.Property == TimePerDivisionProperty || e.Property == AmplitudePerDivisionProperty) o._history.Clear();
        o.DrawOverlay();
    }

    private static void OnTriggerModeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var o = (Oscilloscope)d;
        o._trigger.Mode = (TriggerMode)e.NewValue;
        o.Arm();
    }

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var o = (Oscilloscope)d;
        o.DrawTraces();
        o.DrawOverlay();
    }

    // ---------------------------------------------------------------- processing

    private void Process()
    {
        var t = Trace;
        if (t == null || IsFrozen || t.Samples.Length < 2) return;

        int visible = (int)Math.Round(TimePerDivision * 10 * t.SampleRate);
        visible = Math.Clamp(visible, 2, t.Samples.Length);
        _trigger.Mode = TriggerMode;
        _trigger.Slope = TriggerSlope;
        _trigger.Level = (float)TriggerLevel;
        _trigger.Hysteresis = (float)Math.Max(0.005, AmplitudePerDivision * 0.05);
        _trigger.TriggerPosition = TriggerPosition;
        _trigger.HoldoffSamples = (long)(HoldoffSeconds * t.SampleRate);

        int start = _trigger.Select(t.Samples, t.StartSample, visible);
        if (_trigger.Status != Status) SetValue(StatusPropertyKey, _trigger.Status);

        if (start >= 0)
        {
            var g1 = BuildTrace(t.Samples.AsSpan(start, visible));
            Geometry? g2 = null;
            var t2 = Trace2;
            if (t2 != null && t2.StartSample == t.StartSample && t2.Samples.Length == t.Samples.Length)
                g2 = BuildTrace(t2.Samples.AsSpan(start, visible));
            _history.AddFirst((g1, g2));
            while (_history.Count > Persistence + 1) _history.RemoveLast();
            DrawTraces();
        }
        if (_drawnStatus != Status) DrawOverlay();
    }

    private Geometry BuildTrace(ReadOnlySpan<float> s)
    {
        double ampScale = 90.0 / AmplitudePerDivision;
        double cy = SY + SH / 2;
        double Y(double v) => Math.Clamp(cy - v * ampScale, SY - 20, SY + SH + 20);

        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            int columns = (int)SW;
            if (s.Length > columns * 2)
            {
                // Dense trace: min/max per column drawn as a vertical zig-zag, so no transient is lost.
                MinMaxBucketer.Reduce(s, 0, s.Length, _lo, _hi);
                ctx.BeginFigure(new Point(SX, Y(_hi[0])), false, false);
                for (int i = 0; i < columns; i++)
                {
                    double x = SX + i + 0.5;
                    if (float.IsNaN(_hi[i])) continue;
                    ctx.LineTo(new Point(x, Y(_hi[i])), true, true);
                    ctx.LineTo(new Point(x, Y(_lo[i])), true, true);
                }
            }
            else
            {
                double dx = SW / (s.Length - 1);
                ctx.BeginFigure(new Point(SX, Y(s[0])), false, false);
                for (int i = 1; i < s.Length; i++) ctx.LineTo(new Point(SX + i * dx, Y(s[i])), true, true);
            }
        }
        g.Freeze();
        return g;
    }

    // ---------------------------------------------------------------- layout & drawing

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = availableSize.Width, h = availableSize.Height;
        if (double.IsInfinity(w) && double.IsInfinity(h)) return new Size(500, 410);
        double k = Math.Min(double.IsInfinity(w) ? double.MaxValue : w / DesignW, double.IsInfinity(h) ? double.MaxValue : h / DesignH);
        return new Size(DesignW * k, DesignH * k);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double k = Math.Min(finalSize.Width / DesignW, finalSize.Height / DesignH);
        _rootTransform.Matrix = new Matrix(k, 0, 0, k, (finalSize.Width - DesignW * k) / 2, (finalSize.Height - DesignH * k) / 2);
        DrawStatic();
        return finalSize;
    }

    private void DrawStatic()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double devicePerUnit = Math.Max(0.1, _rootTransform.Matrix.M11) * dpi.DpiScaleX;
        using var dc = _static.RenderOpen();

        // Bezel
        var bezel = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x3A, 0x3C, 0x3A), 0), new(Color.FromRgb(0x1A, 0x1B, 0x1A), 0.4), new(Color.FromRgb(0x0C, 0x0C, 0x0C), 1),
        }, new Point(0.3, 0), new Point(0.7, 1));
        bezel.Freeze();
        var outer = new Rect(4, 4, DesignW - 8, DesignH - 8);
        dc.DrawRoundedRectangle(bezel, VintageArt.FrozenPen(VintageArt.Frozen(Colors.Black), 3), outer, 34, 34);
        dc.DrawRoundedRectangle(VintageArt.GrainBrush(devicePerUnit, 0.45), null, outer, 34, 34);
        var rim = new LinearGradientBrush(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0.5, 0), new Point(0.5, 0.4));
        rim.Freeze();
        dc.DrawRoundedRectangle(null, new Pen(rim, 3), new Rect(7, 7, DesignW - 14, DesignH - 14), 31, 31);

        // Recess around the tube
        var recess = new Rect(SX - 18, SY - 18, SW + 36, SH + 36);
        var recessBrush = new LinearGradientBrush(Color.FromRgb(0x02, 0x02, 0x02), Color.FromRgb(0x24, 0x26, 0x24), 90);
        recessBrush.Freeze();
        dc.DrawRoundedRectangle(recessBrush, null, recess, 18, 18);

        // Phosphor screen with a slight vignette
        var screen = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x0C, 0x1E, 0x10), 0), new(Color.FromRgb(0x07, 0x12, 0x09), 0.65), new(Color.FromRgb(0x02, 0x05, 0x03), 1),
        })
        { RadiusX = 0.62, RadiusY = 0.62 };
        screen.Freeze();
        var screenRect = new Rect(SX, SY, SW, SH);
        dc.DrawRoundedRectangle(screen, null, screenRect, 6, 6);

        // Graticule
        var major = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(70, 0x9C, 0xE8, 0x8A)), 1.6);
        var axis = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(110, 0x9C, 0xE8, 0x8A)), 1.8);
        for (int i = 1; i < 10; i++) dc.DrawLine(i == 5 ? axis : major, new Point(SX + i * 90, SY), new Point(SX + i * 90, SY + SH));
        for (int j = 1; j < 8; j++) dc.DrawLine(j == 4 ? axis : major, new Point(SX, SY + j * 90), new Point(SX + SW, SY + j * 90));
        // Minor ticks on the centre axes (5 per division)
        for (int i = 0; i <= 50; i++)
        {
            double x = SX + i * 18;
            dc.DrawLine(axis, new Point(x, SY + SH / 2 - 7), new Point(x, SY + SH / 2 + 7));
        }
        for (int j = 0; j <= 40; j++)
        {
            double y = SY + j * 18;
            dc.DrawLine(axis, new Point(SX + SW / 2 - 7, y), new Point(SX + SW / 2 + 7, y));
        }
        dc.DrawRoundedRectangle(null, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(90, 0x9C, 0xE8, 0x8A)), 2), screenRect, 6, 6);

        foreach (var p in new[] { new Point(26, 26), new Point(DesignW - 26, 26), new Point(26, DesignH - 26), new Point(DesignW - 26, DesignH - 26) })
            VintageArt.DrawScrew(dc, p, 11, p.X * 0.37 + p.Y * 0.11);
    }

    private void DrawTraces()
    {
        using var dc = _traces.RenderOpen();
        int idx = 0;
        // Oldest first, so the newest trace is on top.
        foreach (var (g1, g2) in _history.Reverse())
        {
            int age = _history.Count - 1 - idx++;
            double fade = age == 0 ? 1.0 : 0.45 * Math.Pow(0.6, age - 1);
            if (g2 != null) DrawPhosphor(dc, g2, Trace2Color, fade, age == 0);
            DrawPhosphor(dc, g1, TraceColor, fade, age == 0);
        }
    }

    private static void DrawPhosphor(DrawingContext dc, Geometry g, Color c, double fade, bool newest)
    {
        if (newest)
        {
            // Bloom, beam and hot core: three passes give the glowing CRT look.
            dc.DrawGeometry(null, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(c, 0.14 * fade)), 11, PenLineCap.Round), g);
            dc.DrawGeometry(null, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(c, 0.45 * fade)), 4.5, PenLineCap.Round), g);
            dc.DrawGeometry(null, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(VintageArt.Blend(c, Colors.White, 0.55), fade)), 1.8, PenLineCap.Round), g);
        }
        else
        {
            dc.DrawGeometry(null, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(c, 0.5 * fade)), 3, PenLineCap.Round), g);
        }
    }

    private void DrawOverlay()
    {
        _drawnStatus = Status;
        using var dc = _overlay.RenderOpen();
        var ci = CultureInfo.InvariantCulture;
        var c1 = VintageArt.Frozen(VintageArt.Blend(TraceColor, Colors.White, 0.2));
        var c2 = VintageArt.Frozen(Trace2Color);
        var dim = VintageArt.Frozen(Color.FromArgb(200, 0x9C, 0xE8, 0x8A));
        const double size = 26;

        dc.DrawText(VintageArt.Text($"CH1  {AmplitudePerDivision.ToString("0.###", ci)} FS/div", size, c1, FontWeights.SemiBold, 1), new Point(SX + 14, SY + 10));
        if (Trace2 != null)
            dc.DrawText(VintageArt.Text($"CH2  {AmplitudePerDivision.ToString("0.###", ci)} FS/div", size, c2, FontWeights.SemiBold, 1), new Point(SX + 290, SY + 10));
        dc.DrawText(VintageArt.Text($"{Steps125.FormatSeconds(TimePerDivision)}/div", size, dim, FontWeights.SemiBold, 1), new Point(SX + 14, SY + SH - 40));

        string slope = TriggerSlope == TriggerSlope.Rising ? "↑" : "↓";
        var trig = VintageArt.Text($"{TriggerMode.ToString().ToUpperInvariant()}  {slope} {TriggerLevel.ToString("+0.00;−0.00;0.00", ci)}", size, dim, FontWeights.SemiBold, 1);
        dc.DrawText(trig, new Point(SX + SW - 14 - trig.Width, SY + SH - 40));

        (string text, Color color) status = IsFrozen
            ? ("FROZEN", Color.FromRgb(0x7C, 0xC8, 0xF0))
            : Status switch
            {
                ScopeStatus.Triggered => ("TRIG'D", Color.FromRgb(0x8C, 0xFF, 0x6A)),
                ScopeStatus.FreeRunning => (TriggerMode == TriggerMode.Free ? "FREE" : "AUTO", Color.FromRgb(0xFF, 0xC0, 0x3A)),
                ScopeStatus.Stopped => ("STOP", Color.FromRgb(0xFF, 0x50, 0x3A)),
                _ => ("WAIT", Color.FromRgb(0xFF, 0xC0, 0x3A)),
            };
        var st = VintageArt.Text(status.text, size, VintageArt.Frozen(status.color), FontWeights.Bold, 1);
        dc.DrawText(st, new Point(SX + SW - 14 - st.Width, SY + 10));

        // Trigger level arrow (right edge) and trigger position marker (top edge)
        double ly = Math.Clamp(SY + SH / 2 - TriggerLevel * 90.0 / AmplitudePerDivision, SY, SY + SH);
        var marker = VintageArt.Frozen(Color.FromRgb(0xFF, 0xC0, 0x3A));
        dc.DrawGeometry(marker, null, Triangle(new Point(SX + SW + 2, ly), new Point(SX + SW + 16, ly - 9), new Point(SX + SW + 16, ly + 9)));
        double tx = SX + Math.Clamp(TriggerPosition, 0, 1) * SW;
        dc.DrawGeometry(marker, null, Triangle(new Point(tx, SY - 2), new Point(tx - 9, SY - 16), new Point(tx + 9, SY - 16)));
    }

    private static Geometry Triangle(Point a, Point b, Point c)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(a, true, true);
            ctx.LineTo(b, true, false);
            ctx.LineTo(c, true, false);
        }
        g.Freeze();
        return g;
    }

    private void DrawGlass()
    {
        using var dc = _glass.RenderOpen();
        var glare = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(34, 255, 255, 255), 0), new(Color.FromArgb(8, 255, 255, 255), 0.35), new(Color.FromArgb(0, 255, 255, 255), 0.5),
        }, new Point(0, 0), new Point(0.6, 1));
        glare.Freeze();
        dc.DrawRoundedRectangle(glare, null, new Rect(SX, SY, SW, SH), 6, 6);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new OscilloscopeAutomationPeer(this);
}

public sealed class OscilloscopeAutomationPeer : FrameworkElementAutomationPeer
{
    public OscilloscopeAutomationPeer(Oscilloscope owner) : base(owner) { }
    protected override string GetClassNameCore() => nameof(Oscilloscope);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        if (!string.IsNullOrEmpty(name)) return name;
        var o = (Oscilloscope)Owner;
        return $"Oscilloscope, {Steps125.FormatSeconds(o.TimePerDivision)} per division, {o.TriggerMode} trigger, {o.Status}";
    }
}
