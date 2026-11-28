using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using AudioCentric.Controls.Input;
using AudioCentric.Controls.Scales;
using AudioCentric.Controls.Wpf.Rendering;

namespace AudioCentric.Controls.Wpf.Controls;

public enum KnobMapping
{
    Linear,
    Logarithmic,
}

public enum KnobStyle
{
    /// <summary>Satin-black cap on a knurled skirt with a luminous indicator line (console style).</summary>
    Knurled,
    /// <summary>Glossy bakelite pointer knob with an ivory inlay line, as on vintage test equipment and radios.</summary>
    ChickenHead,
}

/// <summary>
/// Rotary parameter knob. It publishes a target <see cref="Value"/> (two-way bindable) and never processes audio.
/// Interaction: vertical drag (Shift = fine), mouse wheel, arrow keys / PageUp / PageDown / Home / End,
/// double-click to reset to <see cref="DefaultValue"/>, optional centre detent.
/// The mapping and its inverse live in Core (<see cref="ParameterModel"/>).
/// </summary>
public class RotaryKnob : FrameworkElement
{
    private const double DesignSize = 200;
    private static readonly Point C = new(100, 100);

    private readonly VisualCollection _children;
    private readonly ContainerVisual _root = new();
    private readonly DrawingVisual _scale = new();      // ticks and position labels (not scaled with the knob)
    private readonly ContainerVisual _knob = new();     // the knob itself; shrinks to make room for labels
    private readonly DrawingVisual _shadow = new();     // rotating drop shadow (chicken head)
    private readonly DrawingVisual _static = new();     // knurled skirt and cap
    private readonly DrawingVisual _body = new();       // rotating pointer body (chicken head)
    private readonly DrawingVisual _hub = new();        // fixed-lighting hub dome (chicken head)
    private readonly DrawingVisual _valueArc = new();
    private readonly DrawingVisual _index = new();
    private readonly DrawingVisual _focus = new();
    private readonly RotateTransform _indexRotation = new(0, C.X, C.Y);
    private readonly RotateTransform _shadowRotation = new(0, C.X + ShadowDx, C.Y + ShadowDy);
    private readonly ScaleTransform _knobScale = new(1, 1, C.X, C.Y);
    private readonly MatrixTransform _rootTransform = new();
    private const double ShadowDx = 4, ShadowDy = 7;

    private ParameterModel _model;
    private bool _dragging;
    private Point _dragStart;
    private double _dragStartNormalized;
    private bool _syncing;

    static RotaryKnob()
    {
        FocusableProperty.OverrideMetadata(typeof(RotaryKnob), new FrameworkPropertyMetadata(true));
        FocusVisualStyleProperty.OverrideMetadata(typeof(RotaryKnob), new FrameworkPropertyMetadata(null));
        CursorProperty.OverrideMetadata(typeof(RotaryKnob), new FrameworkPropertyMetadata(Cursors.SizeNS));
    }

    public RotaryKnob()
    {
        _children = new VisualCollection(this) { _root };
        _root.Transform = _rootTransform;
        _root.Children.Add(_scale);
        _root.Children.Add(_knob);
        _root.Children.Add(_focus);
        _knob.Transform = _knobScale;
        _knob.Children.Add(_shadow);
        _knob.Children.Add(_static);
        _knob.Children.Add(_body);
        _knob.Children.Add(_hub);
        _knob.Children.Add(_valueArc);
        _knob.Children.Add(_index);
        _index.Transform = _indexRotation;
        _body.Transform = _indexRotation;
        _shadow.Transform = _shadowRotation;
        _model = CreateModel();
        DrawStatic();
        DrawIndex();
        UpdateRotation();
    }

    #region Dependency properties

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(RotaryKnob),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(RotaryKnob), new FrameworkPropertyMetadata(0.0, OnModelChanged));

    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(RotaryKnob), new FrameworkPropertyMetadata(1.0, OnModelChanged));

    public double DefaultValue { get => (double)GetValue(DefaultValueProperty); set => SetValue(DefaultValueProperty, value); }
    public static readonly DependencyProperty DefaultValueProperty = DependencyProperty.Register(
        nameof(DefaultValue), typeof(double), typeof(RotaryKnob), new FrameworkPropertyMetadata(0.0, OnModelChanged));

    /// <summary>Quantization step in engineering units; 0 = continuous.</summary>
    public double Step { get => (double)GetValue(StepProperty); set => SetValue(StepProperty, value); }
    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(RotaryKnob), new FrameworkPropertyMetadata(0.0, OnModelChanged));

    public KnobMapping Mapping { get => (KnobMapping)GetValue(MappingProperty); set => SetValue(MappingProperty, value); }
    public static readonly DependencyProperty MappingProperty = DependencyProperty.Register(
        nameof(Mapping), typeof(KnobMapping), typeof(RotaryKnob), new FrameworkPropertyMetadata(KnobMapping.Linear, OnModelChanged));

    /// <summary>Optional detent (e.g. 0 dB or centre pan). NaN = none.</summary>
    public double Detent { get => (double)GetValue(DetentProperty); set => SetValue(DetentProperty, value); }
    public static readonly DependencyProperty DetentProperty = DependencyProperty.Register(
        nameof(Detent), typeof(double), typeof(RotaryKnob), new FrameworkPropertyMetadata(double.NaN, OnModelChanged));

    /// <summary>Total rotation in degrees, centred on 12 o'clock. Default 270.</summary>
    public double SweepAngle { get => (double)GetValue(SweepAngleProperty); set => SetValue(SweepAngleProperty, value); }
    public static readonly DependencyProperty SweepAngleProperty = DependencyProperty.Register(
        nameof(SweepAngle), typeof(double), typeof(RotaryKnob), new FrameworkPropertyMetadata(270.0, OnAppearanceChanged));

    /// <summary>Unit for display and automation, e.g. "dB" or "Hz".</summary>
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(RotaryKnob), new FrameworkPropertyMetadata(""));

    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(RotaryKnob), new FrameworkPropertyMetadata(false));

    public Color IndicatorColor { get => (Color)GetValue(IndicatorColorProperty); set => SetValue(IndicatorColorProperty, value); }
    public static readonly DependencyProperty IndicatorColorProperty = DependencyProperty.Register(
        nameof(IndicatorColor), typeof(Color), typeof(RotaryKnob),
        new FrameworkPropertyMetadata(Color.FromRgb(0x6C, 0xF2, 0x4A), OnAppearanceChanged));

    /// <summary>Show a glowing arc from the start (or the detent, for bipolar knobs) to the current value.</summary>
    public bool ShowValueArc { get => (bool)GetValue(ShowValueArcProperty); set => SetValue(ShowValueArcProperty, value); }
    public static readonly DependencyProperty ShowValueArcProperty = DependencyProperty.Register(
        nameof(ShowValueArc), typeof(bool), typeof(RotaryKnob), new FrameworkPropertyMetadata(true, OnAppearanceChanged));

    /// <summary>Number of scale ticks around the knob (including both ends).</summary>
    public int TickCount { get => (int)GetValue(TickCountProperty); set => SetValue(TickCountProperty, value); }
    public static readonly DependencyProperty TickCountProperty = DependencyProperty.Register(
        nameof(TickCount), typeof(int), typeof(RotaryKnob), new FrameworkPropertyMetadata(11, OnAppearanceChanged));

    /// <summary>Knob body: the console <see cref="Controls.KnobStyle.Knurled"/> cap or a vintage <see cref="Controls.KnobStyle.ChickenHead"/>.</summary>
    public KnobStyle KnobStyle { get => (KnobStyle)GetValue(KnobStyleProperty); set => SetValue(KnobStyleProperty, value); }
    public static readonly DependencyProperty KnobStyleProperty = DependencyProperty.Register(
        nameof(KnobStyle), typeof(KnobStyle), typeof(RotaryKnob), new FrameworkPropertyMetadata(KnobStyle.Knurled, OnAppearanceChanged));

    /// <summary>Body colour of a chicken-head knob. Default black bakelite; try dark brown (#FF3A2216) or ivory.</summary>
    public Color KnobColor { get => (Color)GetValue(KnobColorProperty); set => SetValue(KnobColorProperty, value); }
    public static readonly DependencyProperty KnobColorProperty = DependencyProperty.Register(
        nameof(KnobColor), typeof(Color), typeof(RotaryKnob), new FrameworkPropertyMetadata(Color.FromRgb(0x1A, 0x18, 0x16), OnAppearanceChanged));

    /// <summary>
    /// Labels printed round the knob, spread evenly over the sweep (first label at the minimum, last at the maximum).
    /// With <see cref="Step"/> = 1, <see cref="Minimum"/> = 0 and <see cref="Maximum"/> = count − 1 the knob becomes a
    /// rotary selector switch. In XAML: <c>ScaleLabels="EXT,+INT,−INT,LINE"</c>. The knob shrinks to leave room for them.
    /// </summary>
    [System.ComponentModel.TypeConverter(typeof(StringListConverter))]
    public IReadOnlyList<string>? ScaleLabels { get => (IReadOnlyList<string>?)GetValue(ScaleLabelsProperty); set => SetValue(ScaleLabelsProperty, value); }
    public static readonly DependencyProperty ScaleLabelsProperty = DependencyProperty.Register(
        nameof(ScaleLabels), typeof(IReadOnlyList<string>), typeof(RotaryKnob), new FrameworkPropertyMetadata(null, OnAppearanceChanged));

    /// <summary>Colour of the ticks and labels printed on the panel round the knob.</summary>
    public Color ScaleColor { get => (Color)GetValue(ScaleColorProperty); set => SetValue(ScaleColorProperty, value); }
    public static readonly DependencyProperty ScaleColorProperty = DependencyProperty.Register(
        nameof(ScaleColor), typeof(Color), typeof(RotaryKnob), new FrameworkPropertyMetadata(Color.FromRgb(0xB8, 0xC2, 0xBA), OnAppearanceChanged));

    #endregion

    /// <summary>Normalized position 0..1 of the current value.</summary>
    public double NormalizedValue => _model.Mapping.ToNormalized(Value);

    /// <summary>Indicator angle in degrees (0 = 12 o'clock).</summary>
    public double IndicatorAngle => _indexRotation.Angle;

    private ParameterModel CreateModel()
    {
        double min = Minimum, max = Maximum;
        if (!(max > min)) max = min + 1;
        IValueMapping mapping = Mapping == KnobMapping.Logarithmic && min > 0 ? new LogMapping(min, max) : new LinearMapping(min, max);
        return new ParameterModel(min, max, Math.Clamp(DefaultValue, min, max), mapping)
        {
            Step = Step,
            Detents = double.IsNaN(Detent) ? Array.Empty<double>() : new[] { Detent },
        };
    }

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var k = (RotaryKnob)d;
        double v = (double)baseValue;
        if (double.IsNaN(v)) return k.Value;
        return Math.Clamp(v, Math.Min(k.Minimum, k.Maximum), Math.Max(k.Minimum, k.Maximum));
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var k = (RotaryKnob)d;
        if (!k._syncing) k._model.Value = (double)e.NewValue;
        k.UpdateRotation();
        (UIElementAutomationPeer.FromElement(k) as RotaryKnobAutomationPeer)?.RaiseValueChanged((double)e.OldValue, (double)e.NewValue);
    }

    private static void OnModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var k = (RotaryKnob)d;
        k._model = k.CreateModel();
        k._model.Value = k.Value;
        k.CoerceValue(ValueProperty);
        k.UpdateRotation();
    }

    private static void OnAppearanceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var k = (RotaryKnob)d;
        k.DrawStatic();
        k.DrawIndex();
        k.UpdateRotation();
    }

    private void SetFromModel()
    {
        _syncing = true;
        try { SetCurrentValue(ValueProperty, _model.Value); }
        finally { _syncing = false; }
        UpdateRotation();
    }

    // ---------------------------------------------------------------- input

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (IsReadOnly) return;
        if (e.ClickCount == 2)
        {
            _model.ResetToDefault();
            SetFromModel();
            e.Handled = true;
            return;
        }
        _dragging = CaptureMouse();
        _dragStart = e.GetPosition(this);
        _dragStartNormalized = _model.Normalized;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        var p = e.GetPosition(this);
        double travel = Math.Max(120, ActualHeight * 2.0);
        double fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 1.0;
        double dy = _dragStart.Y - p.Y;
        _model.Normalized = Math.Clamp(_dragStartNormalized + dy / travel * fine, 0, 1);
        // Re-anchor in fine mode so switching modifiers does not jump.
        if (fine < 1) { _dragStart = p; _dragStartNormalized = _model.Normalized; }
        SetFromModel();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (IsReadOnly) return;
        double fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.002 : 0.02;
        _model.Nudge(Math.Sign(e.Delta) * fine);
        SetFromModel();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsReadOnly) return;
        double fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 1.0;
        switch (e.Key)
        {
            case Key.Up: case Key.Right: _model.Nudge(0.01 * fine); break;
            case Key.Down: case Key.Left: _model.Nudge(-0.01 * fine); break;
            case Key.PageUp: _model.Nudge(0.1); break;
            case Key.PageDown: _model.Nudge(-0.1); break;
            case Key.Home: _model.Normalized = 0; break;
            case Key.End: _model.Normalized = 1; break;
            case Key.Delete: case Key.Back: _model.ResetToDefault(); break;
            default: return;
        }
        SetFromModel();
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); DrawFocus(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); DrawFocus(); }

    // ---------------------------------------------------------------- layout & drawing

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        double s = Math.Min(availableSize.Width, availableSize.Height);
        if (double.IsInfinity(s)) s = double.IsInfinity(availableSize.Width) && double.IsInfinity(availableSize.Height) ? 80 : Math.Min(availableSize.Width, availableSize.Height);
        if (double.IsInfinity(s)) s = 80;
        return new Size(s, s);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double s = Math.Min(finalSize.Width, finalSize.Height);
        double k = s / DesignSize;
        _rootTransform.Matrix = new Matrix(k, 0, 0, k, (finalSize.Width - s) / 2, (finalSize.Height - s) / 2);
        return finalSize;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;

    private double StartAngle => -SweepAngle / 2;

    private static Point Polar(double r, double angle)
    {
        var (x, y) = ArcGeometry.PointAt(C.X, C.Y, r, angle);
        return new Point(x, y);
    }

    private void UpdateRotation()
    {
        double u = _model.Mapping.ToNormalized(Value);
        _indexRotation.Angle = StartAngle + u * SweepAngle;
        _shadowRotation.Angle = _indexRotation.Angle;
        DrawValueArc(u);
    }

    private bool HasLabels => ScaleLabels is { Count: > 0 };

    private void DrawStatic()
    {
        bool labels = HasLabels;
        double k = labels ? 0.7 : 1.0;
        _knobScale.ScaleX = _knobScale.ScaleY = k;
        DrawScale(labels);

        using (var dc = _static.RenderOpen())
        {
            if (KnobStyle == KnobStyle.Knurled) DrawKnurled(dc);
            else DrawSkirt(dc);
        }
        DrawChickenHead();
    }

    private void DrawScale(bool labels)
    {
        using var dc = _scale.RenderOpen();
        var ink = VintageArt.Frozen(ScaleColor);
        if (!labels)
        {
            int n = Math.Max(2, TickCount);
            for (int i = 0; i < n; i++)
            {
                double a = StartAngle + SweepAngle * i / (n - 1);
                bool major = i == 0 || i == n - 1 || i == (n - 1) / 2 && (n - 1) % 2 == 0;
                dc.DrawLine(VintageArt.FrozenPen(ink, major ? 4 : 2.6, PenLineCap.Round), Polar(88, a), Polar(major ? 98 : 95, a));
            }
            return;
        }

        // Selector-style panel printing: a small arrowhead at each position pointing at the knob, and its label outside.
        var list = ScaleLabels!;
        int count = list.Count;
        for (int i = 0; i < count; i++)
        {
            double a = count == 1 ? 0 : StartAngle + SweepAngle * i / (count - 1);
            var tip = Polar(66, a);
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(tip, true, true);
                ctx.LineTo(Polar(76, a - 4.5), true, false);
                ctx.LineTo(Polar(76, a + 4.5), true, false);
            }
            g.Freeze();
            dc.DrawGeometry(ink, null, g);
            if (string.IsNullOrEmpty(list[i])) continue;
            var ft = VintageArt.Text(list[i], 17, ink, FontWeights.Bold, 1);
            // Push wide labels out a little more at the sides so they clear the arrowheads.
            double side = Math.Abs(Math.Sin(a * Math.PI / 180));
            VintageArt.DrawCenteredText(dc, ft, Polar(88 + side * ft.Width * 0.3, a));
        }
    }

    private void DrawKnurled(DrawingContext dc)
    {
        // Drop shadow
        var shadow = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(200, 0, 0, 0), 0.7),
            new(Color.FromArgb(0, 0, 0, 0), 1.0),
        });
        shadow.Freeze();
        dc.DrawEllipse(shadow, null, new Point(C.X + 3, C.Y + 7), 80, 80);

        // Knurled skirt
        var skirt = new LinearGradientBrush(Color.FromRgb(0x4A, 0x4A, 0x4A), Color.FromRgb(0x08, 0x08, 0x08), new Point(0.2, 0), new Point(0.8, 1));
        skirt.Freeze();
        dc.DrawEllipse(skirt, VintageArt.FrozenPen(VintageArt.Frozen(Colors.Black), 1.5), C, 72, 72);
        var knurl = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(150, 0, 0, 0)), 1.6);
        var knurlHi = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(45, 255, 255, 255)), 1.0);
        for (int i = 0; i < 72; i++)
        {
            double a = i * 5;
            dc.DrawLine(knurl, Polar(61, a), Polar(71, a));
            dc.DrawLine(knurlHi, Polar(61, a + 1.6), Polar(71, a + 1.6));
        }

        // Cap: slightly domed, satin black
        var cap = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x50, 0x50, 0x50), 0.0),
            new(Color.FromRgb(0x24, 0x24, 0x24), 0.55),
            new(Color.FromRgb(0x0C, 0x0C, 0x0C), 1.0),
        })
        { GradientOrigin = new Point(0.38, 0.3), Center = new Point(0.45, 0.4), RadiusX = 0.7, RadiusY = 0.7 };
        cap.Freeze();
        dc.DrawEllipse(cap, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0x02, 0x02, 0x02)), 2), C, 59, 59);
        // Cap edge highlight (chamfer catching the light)
        var chamfer = new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0.3, 0), new Point(0.6, 0.7));
        chamfer.Freeze();
        dc.DrawEllipse(null, new Pen(chamfer, 2.2), C, 57, 57);
        // Satin grain
        dc.DrawEllipse(VintageArt.GrainBrush(2.0, 0.35), null, C, 58, 58);
    }

    /// <summary>The round moulded skirt under a chicken-head pointer, lit from the top left.</summary>
    private void DrawSkirt(DrawingContext dc)
    {
        var c = KnobColor;
        var shadow = new RadialGradientBrush(new GradientStopCollection { new(Color.FromArgb(170, 0, 0, 0), 0.75), new(Color.FromArgb(0, 0, 0, 0), 1) });
        shadow.Freeze();
        dc.DrawEllipse(shadow, null, new Point(C.X + ShadowDx * 0.7, C.Y + ShadowDy * 0.7), 50, 50);
        var skirt = new LinearGradientBrush(new GradientStopCollection
        {
            new(VintageArt.Blend(c, Colors.White, 0.22), 0), new(c, 0.45), new(VintageArt.Blend(c, Colors.Black, 0.6), 1),
        }, new Point(0.2, 0.1), new Point(0.8, 0.95));
        skirt.Freeze();
        dc.DrawEllipse(skirt, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.Blend(c, Colors.Black, 0.85)), 1.5), C, 42, 42);
        var step = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, 0, 0, 0), 0.82), new(Color.FromArgb(110, 0, 0, 0), 0.9), new(Color.FromArgb(0, 0, 0, 0), 0.97),
        });
        step.Freeze();
        dc.DrawEllipse(step, null, C, 42, 42);
        var rimLight = new LinearGradientBrush(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0.25, 0), new Point(0.6, 0.6));
        rimLight.Freeze();
        dc.DrawEllipse(null, new Pen(rimLight, 1.6), C, 40.5, 40.5);
    }

    /// <summary>Outline of a chicken-head pointer pointing up (to 12 o'clock): a long tapered beak, broad shoulders and a short tail.</summary>
    private static Geometry ChickenHeadOutline(double dx = 0, double dy = 0)
    {
        Point Q(double x, double y) => new(C.X + x + dx, C.Y + y + dy);
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(Q(-6, -87), true, true);
            ctx.ArcTo(Q(6, -87), new Size(6, 6), 0, false, SweepDirection.Clockwise, true, true);       // rounded beak tip
            ctx.BezierTo(Q(11, -62), Q(28, -36), Q(30, -8), true, true);                                    // right side to the shoulder
            ctx.BezierTo(Q(31, 14), Q(22, 30), Q(0, 34), true, true);                                       // round the back of the head
            ctx.BezierTo(Q(-22, 30), Q(-31, 14), Q(-30, -8), true, true);
            ctx.BezierTo(Q(-28, -36), Q(-11, -62), Q(-6, -87), true, true);
        }
        g.Freeze();
        return g;
    }

    private void DrawChickenHead()
    {
        bool on = KnobStyle == KnobStyle.ChickenHead;
        using var shadowDc = _shadow.RenderOpen();
        using var bodyDc = _body.RenderOpen();
        using var hubDc = _hub.RenderOpen();
        if (!on) return;
        var c = KnobColor;

        // Two-step soft shadow on the panel, rotating with the knob.
        var outline = ChickenHeadOutline(ShadowDx, ShadowDy);
        shadowDc.PushTransform(new ScaleTransform(1.07, 1.07, C.X + ShadowDx, C.Y + ShadowDy));
        shadowDc.DrawGeometry(VintageArt.Frozen(Color.FromArgb(55, 0, 0, 0)), null, outline);
        shadowDc.Pop();
        shadowDc.DrawGeometry(VintageArt.Frozen(Color.FromArgb(120, 0, 0, 0)), null, outline);

        // Body: moulded so the centre line is a raised ridge; the shading travels with it as the knob turns.
        var across = new LinearGradientBrush(new GradientStopCollection
        {
            new(VintageArt.Blend(c, Colors.Black, 0.6), 0.0),
            new(VintageArt.Blend(c, Colors.White, 0.10), 0.30),
            new(VintageArt.Blend(c, Colors.White, 0.30), 0.46),
            new(VintageArt.Blend(c, Colors.White, 0.06), 0.56),
            new(VintageArt.Blend(c, Colors.Black, 0.7), 1.0),
        }, new Point(C.X - 32, C.Y), new Point(C.X + 32, C.Y)) { MappingMode = BrushMappingMode.Absolute };
        across.Freeze();
        var body = ChickenHeadOutline();
        bodyDc.DrawGeometry(across, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.Blend(c, Colors.Black, 0.8)), 1.6), body);
        var along = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, 0, 0, 0), 0.0), new(Color.FromArgb(0, 0, 0, 0), 0.55), new(Color.FromArgb(120, 0, 0, 0), 1.0),
        }, new Point(C.X, C.Y - 84), new Point(C.X, C.Y + 40)) { MappingMode = BrushMappingMode.Absolute };
        along.Freeze();
        bodyDc.DrawGeometry(along, null, body);

        // Raised spine running out to the beak
        var spine = new StreamGeometry();
        using (var ctx = spine.Open())
        {
            ctx.BeginFigure(new Point(C.X - 7, C.Y - 18), true, true);
            ctx.LineTo(new Point(C.X - 2.5, C.Y - 82), true, true);
            ctx.LineTo(new Point(C.X + 2.5, C.Y - 82), true, true);
            ctx.LineTo(new Point(C.X + 7, C.Y - 18), true, true);
        }
        spine.Freeze();
        var spineBrush = new LinearGradientBrush(VintageArt.Blend(c, Colors.White, 0.32), VintageArt.Blend(c, Colors.Black, 0.25), new Point(0, 0.5), new Point(1, 0.5));
        spineBrush.Freeze();
        bodyDc.DrawGeometry(spineBrush, null, spine);

        // Ivory inlay line from the hub to the beak (drawn in the index layer so it sits on top of the hub)
        // Gloss: a soft reflection along the left flank
        var gloss = new LinearGradientBrush(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(1, 1));
        gloss.Freeze();
        var glossGeo = new StreamGeometry();
        using (var ctx = glossGeo.Open())
        {
            ctx.BeginFigure(new Point(C.X - 6, C.Y - 78), true, true);
            ctx.BezierTo(new Point(C.X - 14, C.Y - 54), new Point(C.X - 24, C.Y - 32), new Point(C.X - 25, C.Y - 8), true, true);
            ctx.LineTo(new Point(C.X - 20, C.Y - 8), true, true);
            ctx.BezierTo(new Point(C.X - 19, C.Y - 32), new Point(C.X - 10, C.Y - 54), new Point(C.X - 4, C.Y - 76), true, true);
        }
        glossGeo.Freeze();
        bodyDc.DrawGeometry(gloss, null, glossGeo);

        // Hub: a domed boss lit from the top left; it does not rotate, so its highlight stays put.
        var hub = new RadialGradientBrush(new GradientStopCollection
        {
            new(VintageArt.Blend(c, Colors.White, 0.42), 0.0),
            new(VintageArt.Blend(c, Colors.White, 0.12), 0.35),
            new(c, 0.7),
            new(VintageArt.Blend(c, Colors.Black, 0.65), 1.0),
        })
        { GradientOrigin = new Point(0.32, 0.28), Center = new Point(0.42, 0.38), RadiusX = 0.75, RadiusY = 0.75 };
        hub.Freeze();
        hubDc.DrawEllipse(hub, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.Blend(c, Colors.Black, 0.8)), 1.4), C, 23, 23);
        var hubSpec = new RadialGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
        hubSpec.Freeze();
        hubDc.DrawEllipse(hubSpec, null, new Point(C.X - 8, C.Y - 9), 8, 5);
    }

    private void DrawIndex()
    {
        var color = IndicatorColor;
        using var dc = _index.RenderOpen();
        if (KnobStyle == KnobStyle.ChickenHead)
        {
            // Ivory inlay along the top of the spine, slightly worn at the hub end.
            // Ivory on dark bakelite; on a light knob the inlay is dark instead.
            var k = KnobColor;
            bool light = 0.299 * k.R + 0.587 * k.G + 0.114 * k.B > 140;
            var ivory = light ? Color.FromRgb(0x2A, 0x1E, 0x14) : Color.FromRgb(0xEE, 0xE6, 0xCC);
            dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(150, 0, 0, 0)), 5, PenLineCap.Round), new Point(C.X, C.Y - 10), new Point(C.X, C.Y - 80));
            var inlay = new LinearGradientBrush(VintageArt.WithAlpha(ivory, 0.55), ivory, new Point(0.5, 1), new Point(0.5, 0));
            inlay.Freeze();
            dc.DrawLine(new Pen(inlay, 3) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, new Point(C.X, C.Y - 11), new Point(C.X, C.Y - 79));
            return;
        }
        // Recessed groove with a luminous green fill
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(200, 0, 0, 0)), 7, PenLineCap.Round), Polar(20, 0), Polar(53, 0));
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(color, 0.35)), 8, PenLineCap.Round), Polar(22, 0), Polar(52, 0));
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(color), 4.2, PenLineCap.Round), Polar(22, 0), Polar(52, 0));
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.Blend(color, Colors.White, 0.6)), 1.4, PenLineCap.Round), Polar(24, 0), Polar(50, 0));
    }

    private void DrawValueArc(double u)
    {
        using var dc = _valueArc.RenderOpen();
        if (!ShowValueArc) return;
        double origin = double.IsNaN(Detent) ? 0 : _model.Mapping.ToNormalized(Detent);
        double a0 = StartAngle + Math.Min(origin, u) * SweepAngle;
        double a1 = StartAngle + Math.Max(origin, u) * SweepAngle;
        // Unlit track
        DrawArc(dc, StartAngle, StartAngle + SweepAngle, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(150, 0x10, 0x18, 0x10)), 4, PenLineCap.Round));
        if (a1 - a0 < 0.2) return;
        var c = IndicatorColor;
        DrawArc(dc, a0, a1, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(c, 0.3)), 7, PenLineCap.Round));
        DrawArc(dc, a0, a1, VintageArt.FrozenPen(VintageArt.Frozen(c), 3, PenLineCap.Round));
    }

    private static void DrawArc(DrawingContext dc, double a0, double a1, Pen pen)
    {
        const double r = 80;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(Polar(r, a0), false, false);
            ctx.ArcTo(Polar(r, a1), new Size(r, r), 0, a1 - a0 > 180, SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }

    private void DrawFocus()
    {
        using var dc = _focus.RenderOpen();
        if (!IsKeyboardFocused) return;
        var pen = new Pen(VintageArt.Frozen(VintageArt.WithAlpha(IndicatorColor, 0.8)), 2) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        dc.DrawEllipse(null, pen, C, 99, 99);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new RotaryKnobAutomationPeer(this);

    internal void SetValueFromAutomation(double value)
    {
        _model.Value = value;
        SetFromModel();
    }
}

public sealed class RotaryKnobAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    public RotaryKnobAutomationPeer(RotaryKnob owner) : base(owner) { }
    private RotaryKnob Knob => (RotaryKnob)Owner;
    protected override string GetClassNameCore() => nameof(RotaryKnob);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    public override object GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface)!;

    bool IRangeValueProvider.IsReadOnly => Knob.IsReadOnly;
    double IRangeValueProvider.LargeChange => (Knob.Maximum - Knob.Minimum) / 10;
    double IRangeValueProvider.SmallChange => Knob.Step > 0 ? Knob.Step : (Knob.Maximum - Knob.Minimum) / 100;
    double IRangeValueProvider.Maximum => Knob.Maximum;
    double IRangeValueProvider.Minimum => Knob.Minimum;
    double IRangeValueProvider.Value => Knob.Value;
    void IRangeValueProvider.SetValue(double value)
    {
        if (Knob.IsReadOnly) throw new System.Windows.Automation.ElementNotEnabledException();
        Knob.SetValueFromAutomation(value);
    }

    internal void RaiseValueChanged(double oldValue, double newValue) =>
        RaisePropertyChangedEvent(System.Windows.Automation.RangeValuePatternIdentifiers.ValueProperty, oldValue, newValue);
}
