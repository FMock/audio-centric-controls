using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioCentric.Controls.Dynamics;
using AudioCentric.Controls.Scales;
using AudioCentric.Controls.Wpf.Rendering;

namespace AudioCentric.Controls.Wpf.Controls;

/// <summary>What kind of reading an LED bar shows. It only labels the reading; the provider measures it.</summary>
public enum LedMeterMode
{
    Peak,
    TruePeak,
    Rms,
    Vu,
}

/// <summary>
/// Console-style LED bar meter. It shows any labelled level (peak, true peak, RMS or VU) against ordered
/// thresholds (segment i lights when L ≥ threshold[i]). It has display decay, an optional peak-hold segment,
/// one or two channels, bar or dot mode, and a separate clip LED with latch and reset.
/// Only segment state is redrawn per frame, and only when it changes.
/// </summary>
public class LedBarMeter : FrameworkElement, IRenderTickable
{
    private readonly VisualCollection _children;
    private readonly DrawingVisual _housing = new();
    private readonly DrawingVisual _segments = new();

    private readonly DecayFollower[] _decay = { new(), new() };
    private readonly PeakHoldTracker[] _hold = { new(), new() };
    private readonly OverloadLatch _clip = new();

    private readonly int[] _drawnLit = { -2, -2 };
    private readonly int[] _drawnHold = { -2, -2 };
    private bool _drawnClip;
    private bool _registered;
    private Rect[][] _segmentRects = { Array.Empty<Rect>(), Array.Empty<Rect>() };
    private Rect _clipRect;

    public static readonly RoutedCommand ResetClipCommand = new(nameof(ResetClipCommand), typeof(LedBarMeter));

    static LedBarMeter()
    {
        CommandManager.RegisterClassCommandBinding(typeof(LedBarMeter),
            new CommandBinding(ResetClipCommand, (s, _) => ((LedBarMeter)s).ResetClip()));
    }

    public LedBarMeter()
    {
        _children = new VisualCollection(this) { _housing, _segments };
        _clip.Mode = ClipLatchMode;
        Loaded += (_, _) => { if (!_registered) { _registered = true; RenderScheduler.Register(this); } };
        Unloaded += (_, _) => { if (_registered) { _registered = false; RenderScheduler.Unregister(this); } };
    }

    #region Dependency properties

    /// <summary>Level of channel 1 in the scale's units (e.g. dBFS). -∞ is silence; NaN is invalid.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(LedBarMeter), new FrameworkPropertyMetadata(double.NegativeInfinity, OnLevelChanged));

    /// <summary>Level of channel 2, used when <see cref="ChannelCount"/> is 2.</summary>
    public double Value2 { get => (double)GetValue(Value2Property); set => SetValue(Value2Property, value); }
    public static readonly DependencyProperty Value2Property = DependencyProperty.Register(
        nameof(Value2), typeof(double), typeof(LedBarMeter), new FrameworkPropertyMetadata(double.NegativeInfinity, OnLevelChanged));

    public int ChannelCount { get => (int)GetValue(ChannelCountProperty); set => SetValue(ChannelCountProperty, value); }
    public static readonly DependencyProperty ChannelCountProperty = DependencyProperty.Register(
        nameof(ChannelCount), typeof(int), typeof(LedBarMeter),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsArrange, OnLayoutChanged, (_, v) => Math.Clamp((int)v, 1, 2)));

    public LedScaleDefinition Scale { get => (LedScaleDefinition)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(LedScaleDefinition), typeof(LedBarMeter),
        new FrameworkPropertyMetadata(ScalePresets.LedPeakDense, FrameworkPropertyMetadataOptions.AffectsArrange, OnLayoutChanged));

    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(LedBarMeter),
        new FrameworkPropertyMetadata(Orientation.Horizontal, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange, OnLayoutChanged));

    public LedMeterMode MeterMode { get => (LedMeterMode)GetValue(MeterModeProperty); set => SetValue(MeterModeProperty, value); }
    public static readonly DependencyProperty MeterModeProperty = DependencyProperty.Register(
        nameof(MeterMode), typeof(LedMeterMode), typeof(LedBarMeter), new FrameworkPropertyMetadata(LedMeterMode.Peak, OnLayoutChanged));

    /// <summary>Light only the single highest segment instead of the whole bar.</summary>
    public bool IsDotMode { get => (bool)GetValue(IsDotModeProperty); set => SetValue(IsDotModeProperty, value); }
    public static readonly DependencyProperty IsDotModeProperty = DependencyProperty.Register(
        nameof(IsDotMode), typeof(bool), typeof(LedBarMeter), new FrameworkPropertyMetadata(false, OnLevelChanged));

    /// <summary>Display release in dB/s (immediate attack). NaN = automatic: 20 dB/s in peak modes, none otherwise.</summary>
    public double ReleaseDbPerSecond { get => (double)GetValue(ReleaseDbPerSecondProperty); set => SetValue(ReleaseDbPerSecondProperty, value); }
    public static readonly DependencyProperty ReleaseDbPerSecondProperty = DependencyProperty.Register(
        nameof(ReleaseDbPerSecond), typeof(double), typeof(LedBarMeter), new FrameworkPropertyMetadata(double.NaN));

    public bool IsPeakHoldEnabled { get => (bool)GetValue(IsPeakHoldEnabledProperty); set => SetValue(IsPeakHoldEnabledProperty, value); }
    public static readonly DependencyProperty IsPeakHoldEnabledProperty = DependencyProperty.Register(
        nameof(IsPeakHoldEnabled), typeof(bool), typeof(LedBarMeter), new FrameworkPropertyMetadata(true, OnLevelChanged));

    public double PeakHoldSeconds { get => (double)GetValue(PeakHoldSecondsProperty); set => SetValue(PeakHoldSecondsProperty, value); }
    public static readonly DependencyProperty PeakHoldSecondsProperty = DependencyProperty.Register(
        nameof(PeakHoldSeconds), typeof(double), typeof(LedBarMeter), new FrameworkPropertyMetadata(2.0));

    /// <summary>Host-reported clip/overload condition. Drives the separate clip LED, never the segments.</summary>
    public bool IsClipping { get => (bool)GetValue(IsClippingProperty); set => SetValue(IsClippingProperty, value); }
    public static readonly DependencyProperty IsClippingProperty = DependencyProperty.Register(
        nameof(IsClipping), typeof(bool), typeof(LedBarMeter), new FrameworkPropertyMetadata(false, OnClippingChanged));

    private static void OnClippingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Latch immediately: a host may raise the flag for a single frame only.
        var m = (LedBarMeter)d;
        if ((bool)e.NewValue)
        {
            m._clip.Update(true, 0);
            m.SetValue(IsClipLitPropertyKey, true);
            m.RedrawSegments(force: false);
        }
        else if (!m._registered)
        {
            m.Advance(0, snap: true);
        }
    }

    public OverloadLatchMode ClipLatchMode { get => (OverloadLatchMode)GetValue(ClipLatchModeProperty); set => SetValue(ClipLatchModeProperty, value); }
    public static readonly DependencyProperty ClipLatchModeProperty = DependencyProperty.Register(
        nameof(ClipLatchMode), typeof(OverloadLatchMode), typeof(LedBarMeter),
        new FrameworkPropertyMetadata(OverloadLatchMode.HoldTime, (d, e) => ((LedBarMeter)d)._clip.Mode = (OverloadLatchMode)e.NewValue));

    public bool ShowClipLed { get => (bool)GetValue(ShowClipLedProperty); set => SetValue(ShowClipLedProperty, value); }
    public static readonly DependencyProperty ShowClipLedProperty = DependencyProperty.Register(
        nameof(ShowClipLed), typeof(bool), typeof(LedBarMeter),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsArrange, OnLayoutChanged));

    /// <summary>Draw threshold labels along the bar so colour is not the only cue.</summary>
    public bool ShowScaleLabels { get => (bool)GetValue(ShowScaleLabelsProperty); set => SetValue(ShowScaleLabelsProperty, value); }
    public static readonly DependencyProperty ShowScaleLabelsProperty = DependencyProperty.Register(
        nameof(ShowScaleLabels), typeof(bool), typeof(LedBarMeter),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsArrange, OnLayoutChanged));

    public bool IsClipLit => (bool)GetValue(IsClipLitProperty);
    private static readonly DependencyPropertyKey IsClipLitPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsClipLit), typeof(bool), typeof(LedBarMeter), new FrameworkPropertyMetadata(false));
    public static readonly DependencyProperty IsClipLitProperty = IsClipLitPropertyKey.DependencyProperty;

    #endregion

    /// <summary>Lit segment count (bar mode) per channel after display decay, for tests and diagnostics.</summary>
    public int LitSegments(int channel) => Math.Max(0, _drawnLit[channel]);

    public void ResetClip()
    {
        _clip.Reset();
        SetValue(IsClipLitPropertyKey, false);
        RedrawSegments(force: true);
    }

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var m = (LedBarMeter)d;
        if (!m._registered) m.Advance(0, snap: true);
    }

    private static void OnLayoutChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var m = (LedBarMeter)d;
        m.InvalidateArrange();
        m.RedrawSegments(force: true);
    }

    void IRenderTickable.Tick(double elapsedSeconds) => Advance(elapsedSeconds, snap: false);

    private double AutoRelease => MeterMode is LedMeterMode.Peak or LedMeterMode.TruePeak ? 20.0 : 0.0;

    private void Advance(double dt, bool snap)
    {
        double release = double.IsNaN(ReleaseDbPerSecond) ? AutoRelease : ReleaseDbPerSecond;
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            double v = ch == 0 ? Value : Value2;
            if (double.IsNaN(v)) v = double.NegativeInfinity;
            _decay[ch].ReleaseDbPerSecond = release;
            if (snap || release <= 0) _decay[ch].Reset(v); else _decay[ch].Update(v, dt);
            _hold[ch].HoldSeconds = PeakHoldSeconds;
            _hold[ch].Update(v, dt);
        }
        bool lit = _clip.Update(IsClipping, dt);
        if (lit != IsClipLit) SetValue(IsClipLitPropertyKey, lit);
        RedrawSegments(force: false);
    }

    // ---------------------------------------------------------------- layout

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        bool horizontal = Orientation == Orientation.Horizontal;
        double thickness = 18 * ChannelCount + 8 + (ShowScaleLabels ? 14 : 0);
        double length = 240;
        var desired = horizontal ? new Size(length, thickness) : new Size(thickness, length);
        if (!double.IsInfinity(availableSize.Width) && horizontal) desired.Width = availableSize.Width;
        if (!double.IsInfinity(availableSize.Height) && !horizontal) desired.Height = availableSize.Height;
        // Never exceed the constraint, or WPF arranges at the larger size and clips (hiding the label row).
        return new Size(Math.Min(desired.Width, availableSize.Width), Math.Min(desired.Height, availableSize.Height));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        BuildLayout(finalSize);
        RedrawSegments(force: true);
        return finalSize;
    }

    private const double Pad = 4;
    private const double Gap = 3;

    private double LabelBand => ShowScaleLabels ? 14 : 0;

    /// <summary>Maps (along, across) in a horizontal frame to the actual orientation (vertical grows upward).</summary>
    private Rect Frame(Size size, double along, double across, double alongLen, double acrossLen)
    {
        if (Orientation == Orientation.Horizontal) return new Rect(along, across, alongLen, acrossLen);
        return new Rect(across, size.Height - along - alongLen, acrossLen, alongLen);
    }

    private void BuildLayout(Size size)
    {
        bool horizontal = Orientation == Orientation.Horizontal;
        double length = horizontal ? size.Width : size.Height;
        double thickness = (horizontal ? size.Height : size.Width) - LabelBand;
        int n = Scale.SegmentCount;
        double clipLen = ShowClipLed ? Math.Max(10, thickness * 0.9) : 0;
        double usable = length - 2 * Pad - (ShowClipLed ? clipLen + Gap * 3 : 0);
        double seg = Math.Max(1, (usable - (n - 1) * Gap) / n);
        int channels = ChannelCount;
        double lane = Math.Max(2, (thickness - 2 * Pad - (channels - 1) * Gap) / channels);

        var dpi = VisualTreeHelper.GetDpi(this);
        double Snap(double v) => Math.Round(v * dpi.DpiScaleX) / dpi.DpiScaleX;

        _segmentRects = new Rect[2][];
        for (int ch = 0; ch < 2; ch++)
        {
            _segmentRects[ch] = new Rect[ch < channels ? n : 0];
            for (int i = 0; i < _segmentRects[ch].Length; i++)
            {
                double a0 = Snap(Pad + i * (seg + Gap)), a1 = Snap(Pad + i * (seg + Gap) + seg);
                double c0 = Snap(Pad + ch * (lane + Gap)), c1 = Snap(Pad + ch * (lane + Gap) + lane);
                _segmentRects[ch][i] = Frame(size, a0, c0, a1 - a0, c1 - c0);
            }
        }
        _clipRect = ShowClipLed
            ? Frame(size, Snap(length - Pad - clipLen), Snap(Pad), Snap(clipLen), Snap(thickness - 2 * Pad))
            : Rect.Empty;

        using var dc = _housing.RenderOpen();
        var body = Frame(size, 0, 0, length, thickness);
        var housing = new LinearGradientBrush(Color.FromRgb(0x05, 0x06, 0x05), Color.FromRgb(0x16, 0x18, 0x16), horizontal ? 90 : 0);
        housing.Freeze();
        dc.DrawRoundedRectangle(housing, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0x2C, 0x2E, 0x2C)), 1), body, 4, 4);
        // inner shadow line along the top/left edge (recessed look)
        dc.DrawRoundedRectangle(null, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(160, 0, 0, 0)), 2), new Rect(body.X + 1, body.Y + 1, body.Width - 2, body.Height - 2), 3, 3);

        if (ShowScaleLabels) DrawLabels(dc, size, length, thickness, seg, dpi.PixelsPerDip);
    }

    private void DrawLabels(DrawingContext dc, Size size, double length, double thickness, double seg, double ppd)
    {
        var brush = VintageArt.Frozen(Color.FromRgb(0x9A, 0xA8, 0x9E));
        var t = Scale.Thresholds;
        double lastEdge = double.NegativeInfinity;
        for (int i = t.Count - 1; i >= 0; i--)
        {
            double v = t[i];
            if (Math.Abs(v - Math.Round(v)) > 1e-9) continue;
            string text = v.ToString("0", CultureInfo.InvariantCulture);
            var ft = VintageArt.Text(text, 9.5, brush, FontWeights.Normal, ppd);
            double along = Pad + i * (seg + Gap) + seg / 2;
            if (Orientation == Orientation.Horizontal)
            {
                double right = along + ft.Width / 2;
                if (lastEdge != double.NegativeInfinity && right > lastEdge - 3) continue;
                lastEdge = along - ft.Width / 2;
                dc.DrawText(ft, new Point(along - ft.Width / 2, thickness + 1));
            }
            else
            {
                double y = size.Height - along;
                double bottom = y + ft.Height / 2;
                if (lastEdge != double.NegativeInfinity && y - ft.Height / 2 < lastEdge + 1) continue;
                lastEdge = bottom;
                dc.DrawText(ft, new Point(thickness + 2, y - ft.Height / 2));
            }
        }
    }

    // ---------------------------------------------------------------- segments

    private static readonly Color Green = Color.FromRgb(0x58, 0xF0, 0x3E);
    private static readonly Color Amber = Color.FromRgb(0xFF, 0xB0, 0x1E);
    private static readonly Color Red = Color.FromRgb(0xFF, 0x2E, 0x1E);

    private static Color ColorOf(ZoneKind z) => z switch { ZoneKind.Danger => Red, ZoneKind.Warning => Amber, _ => Green };

    private static readonly Dictionary<(Color, bool), Brush> BrushCache = new();

    private static Brush SegmentBrush(Color c, bool lit)
    {
        if (BrushCache.TryGetValue((c, lit), out var b)) return b;
        GradientStopCollection stops = lit
            ? new() { new(VintageArt.Blend(c, Colors.White, 0.45), 0), new(c, 0.45), new(VintageArt.Blend(c, Colors.Black, 0.25), 1) }
            : new() { new(VintageArt.Blend(c, Colors.Black, 0.78), 0), new(VintageArt.Blend(c, Colors.Black, 0.86), 1) };
        var brush = new LinearGradientBrush(stops, new Point(0.5, 0), new Point(0.5, 1));
        brush.Freeze();
        BrushCache[(c, lit)] = brush;
        return brush;
    }

    private void RedrawSegments(bool force)
    {
        var scale = Scale;
        bool changed = force;
        var lits = new int[2];
        var holds = new int[2];
        for (int ch = 0; ch < ChannelCount; ch++)
        {
            lits[ch] = scale.LitCount(_decay[ch].Value);
            holds[ch] = IsPeakHoldEnabled ? scale.LitCount(_hold[ch].Value) - 1 : -1;
            if (lits[ch] != _drawnLit[ch] || holds[ch] != _drawnHold[ch]) changed = true;
        }
        if (IsClipLit != _drawnClip) changed = true;
        if (!changed) return;

        using var dc = _segments.RenderOpen();
        for (int ch = 0; ch < ChannelCount && ch < _segmentRects.Length; ch++)
        {
            _drawnLit[ch] = lits[ch];
            _drawnHold[ch] = holds[ch];
            var rects = _segmentRects[ch];
            for (int i = 0; i < rects.Length; i++)
            {
                bool on = IsDotMode ? i == lits[ch] - 1 : i < lits[ch];
                on |= i == holds[ch];
                var color = ColorOf(scale.ZoneOf(i));
                var r = rects[i];
                double radius = Math.Min(r.Width, r.Height) * 0.18;
                if (on)
                {
                    // Soft bloom around lit segments
                    var glow = new Rect(r.X - 1.5, r.Y - 1.5, r.Width + 3, r.Height + 3);
                    dc.DrawRoundedRectangle(VintageArt.Frozen(VintageArt.WithAlpha(color, 0.28)), null, glow, radius + 1.5, radius + 1.5);
                }
                dc.DrawRoundedRectangle(SegmentBrush(color, on), null, r, radius, radius);
            }
        }

        _drawnClip = IsClipLit;
        if (!_clipRect.IsEmpty)
        {
            var r = _clipRect;
            double radius = Math.Min(r.Width, r.Height) * 0.2;
            if (IsClipLit)
            {
                var glow = new Rect(r.X - 2, r.Y - 2, r.Width + 4, r.Height + 4);
                dc.DrawRoundedRectangle(VintageArt.Frozen(VintageArt.WithAlpha(Red, 0.35)), null, glow, radius + 2, radius + 2);
            }
            dc.DrawRoundedRectangle(SegmentBrush(Red, IsClipLit), VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(120, 0, 0, 0)), 1), r, radius, radius);
            double size = Math.Min(r.Width, r.Height) * 0.42;
            if (size >= 6)
            {
                var ft = VintageArt.Text("CLIP", size * 0.62, VintageArt.Frozen(IsClipLit ? Color.FromRgb(0x3A, 0x00, 0x00) : Color.FromRgb(0x7A, 0x40, 0x3C)), FontWeights.Bold, VisualTreeHelper.GetDpi(this).PixelsPerDip);
                if (ft.Width < r.Width - 2) VintageArt.DrawCenteredText(dc, ft, new Point(r.X + r.Width / 2, r.Y + r.Height / 2));
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!_clipRect.IsEmpty && _clipRect.Contains(e.GetPosition(this)))
        {
            ResetClip();
            e.Handled = true;
        }
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;

    protected override AutomationPeer OnCreateAutomationPeer() => new LedBarMeterAutomationPeer(this);
}

public sealed class LedBarMeterAutomationPeer : FrameworkElementAutomationPeer, System.Windows.Automation.Provider.IRangeValueProvider
{
    public LedBarMeterAutomationPeer(LedBarMeter owner) : base(owner) { }
    private LedBarMeter Meter => (LedBarMeter)Owner;
    protected override string GetClassNameCore() => nameof(LedBarMeter);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;
    public override object GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface)!;
    bool System.Windows.Automation.Provider.IRangeValueProvider.IsReadOnly => true;
    double System.Windows.Automation.Provider.IRangeValueProvider.LargeChange => 0;
    double System.Windows.Automation.Provider.IRangeValueProvider.SmallChange => 0;
    double System.Windows.Automation.Provider.IRangeValueProvider.Maximum => Meter.Scale.Maximum;
    double System.Windows.Automation.Provider.IRangeValueProvider.Minimum => Meter.Scale.Minimum;
    double System.Windows.Automation.Provider.IRangeValueProvider.Value =>
        double.IsNaN(Meter.Value) ? Meter.Scale.Minimum : Math.Clamp(Meter.Value, Meter.Scale.Minimum, Meter.Scale.Maximum);
    void System.Windows.Automation.Provider.IRangeValueProvider.SetValue(double value) => throw new System.Windows.Automation.ElementNotEnabledException();
}
