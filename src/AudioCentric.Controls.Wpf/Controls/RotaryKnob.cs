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
    private readonly DrawingVisual _static = new();
    private readonly DrawingVisual _valueArc = new();
    private readonly DrawingVisual _index = new();
    private readonly DrawingVisual _focus = new();
    private readonly RotateTransform _indexRotation = new(0, C.X, C.Y);
    private readonly MatrixTransform _rootTransform = new();

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
        _root.Children.Add(_static);
        _root.Children.Add(_valueArc);
        _root.Children.Add(_index);
        _root.Children.Add(_focus);
        _index.Transform = _indexRotation;
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
        DrawValueArc(u);
    }

    private void DrawStatic()
    {
        using var dc = _static.RenderOpen();
        var tickBrush = VintageArt.Frozen(Color.FromRgb(0xB8, 0xC2, 0xBA));
        int n = Math.Max(2, TickCount);
        for (int i = 0; i < n; i++)
        {
            double a = StartAngle + SweepAngle * i / (n - 1);
            bool major = i == 0 || i == n - 1 || i == (n - 1) / 2 && (n - 1) % 2 == 0;
            dc.DrawLine(VintageArt.FrozenPen(tickBrush, major ? 4 : 2.6, PenLineCap.Round), Polar(88, a), Polar(major ? 98 : 95, a));
        }

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

    private void DrawIndex()
    {
        var color = IndicatorColor;
        using var dc = _index.RenderOpen();
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
