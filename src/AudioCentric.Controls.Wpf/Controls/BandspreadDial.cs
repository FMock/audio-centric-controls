using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using AudioCentric.Controls.Scales;
using AudioCentric.Controls.Wpf.Rendering;

namespace AudioCentric.Controls.Wpf.Controls;

/// <summary>
/// Small bandspread (vernier) indicator: a backlit scale wheel turning behind a fixed red hairline in a deep, recessed
/// escutcheon. With <see cref="IsWrapping"/> the wheel is endless (…90, 95, 0, 5…), like the fine-tuning dial of a
/// receiver; without it the value stops at <see cref="Minimum"/>/<see cref="Maximum"/>. <see cref="Value"/> is two-way.
/// <para>Mouse: drag sideways (the wheel follows the pointer), wheel (Shift: fine). Keys: ←/→, PgUp/PgDn.</para>
/// </summary>
public class BandspreadDial : FrameworkElement
{
    private const double DesignW = 1000, DesignH = 420;
    private const double WheelR = 430, Cx = 500, Cy = 575;
    private static readonly Rect Frame = new(24, 24, 952, 372);
    private static readonly Rect Opening = new(64, 58, 872, 290);
    private static readonly Rect Window = new(262, 96, 476, 222);

    private readonly VisualCollection _children;
    private readonly ContainerVisual _root = new();
    private readonly DrawingVisual _escutcheon = new();
    private readonly DrawingVisual _face = new();
    private readonly ContainerVisual _window = new();
    private readonly DrawingVisual _wheel = new();
    private readonly DrawingVisual _hairline = new();
    private readonly DrawingVisual _glass = new();
    private readonly DrawingVisual _focus = new();
    private readonly RotateTransform _wheelRotation = new(0, Cx, Cy);
    private readonly MatrixTransform _rootTransform = new();
    private double _builtDevicePixelsPerUnit;
    private bool _dragging;
    private Point _dragStart;
    private double _dragStartValue;

    static BandspreadDial()
    {
        FocusableProperty.OverrideMetadata(typeof(BandspreadDial), new FrameworkPropertyMetadata(true));
        FocusVisualStyleProperty.OverrideMetadata(typeof(BandspreadDial), new FrameworkPropertyMetadata(null));
        CursorProperty.OverrideMetadata(typeof(BandspreadDial), new FrameworkPropertyMetadata(Cursors.SizeWE));
    }

    public BandspreadDial()
    {
        _children = new VisualCollection(this) { _root };
        _root.Transform = _rootTransform;
        _root.Children.Add(_escutcheon);
        _root.Children.Add(_face);
        _root.Children.Add(_window);
        _window.Clip = new RectangleGeometry(Window, 8, 8);
        _window.Children.Add(_wheel);
        _window.Children.Add(_hairline);
        _wheel.Transform = _wheelRotation;
        _root.Children.Add(_glass);
        _root.Children.Add(_focus);
        RebuildAll();
    }

    #region Dependency properties

    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(BandspreadDial),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValue));

    public double Minimum { get => (double)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(BandspreadDial), new FrameworkPropertyMetadata(0.0, OnRangeChanged));

    public double Maximum { get => (double)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(BandspreadDial), new FrameworkPropertyMetadata(100.0, OnRangeChanged));

    /// <summary>Endless wheel: values wrap from <see cref="Maximum"/> back to <see cref="Minimum"/>. Default true.</summary>
    public bool IsWrapping { get => (bool)GetValue(IsWrappingProperty); set => SetValue(IsWrappingProperty, value); }
    public static readonly DependencyProperty IsWrappingProperty = DependencyProperty.Register(
        nameof(IsWrapping), typeof(bool), typeof(BandspreadDial), new FrameworkPropertyMetadata(true, OnRangeChanged));

    /// <summary>
    /// Printed scale (ticks and labels in value units). Default <see cref="DialPresets.Bandspread"/> (0–100, numbered every 5).
    /// </summary>
    public ScaleDefinition Scale { get => (ScaleDefinition)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(ScaleDefinition), typeof(BandspreadDial), new FrameworkPropertyMetadata(DialPresets.Bandspread, OnLookChanged));

    /// <summary>
    /// Wheel rotation per value unit, in degrees. NaN (default) fits the range once round the wheel when wrapping
    /// (3.6° per unit for 0–100), or 3° per unit otherwise.
    /// </summary>
    public double DegreesPerUnit { get => (double)GetValue(DegreesPerUnitProperty); set => SetValue(DegreesPerUnitProperty, value); }
    public static readonly DependencyProperty DegreesPerUnitProperty = DependencyProperty.Register(
        nameof(DegreesPerUnit), typeof(double), typeof(BandspreadDial), new FrameworkPropertyMetadata(double.NaN, OnLookChanged));

    /// <summary>Small legend printed on the window glass under the wheel, e.g. "BAND SPREAD".</summary>
    public string Caption { get => (string)GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public static readonly DependencyProperty CaptionProperty = DependencyProperty.Register(
        nameof(Caption), typeof(string), typeof(BandspreadDial), new FrameworkPropertyMetadata("", OnLookChanged));

    public Color FaceColor { get => (Color)GetValue(FaceColorProperty); set => SetValue(FaceColorProperty, value); }
    public static readonly DependencyProperty FaceColorProperty = DependencyProperty.Register(
        nameof(FaceColor), typeof(Color), typeof(BandspreadDial), new FrameworkPropertyMetadata(Color.FromRgb(0xB4, 0xE6, 0xC8), OnLookChanged));

    public Color InkColor { get => (Color)GetValue(InkColorProperty); set => SetValue(InkColorProperty, value); }
    public static readonly DependencyProperty InkColorProperty = DependencyProperty.Register(
        nameof(InkColor), typeof(Color), typeof(BandspreadDial), new FrameworkPropertyMetadata(Color.FromRgb(0x14, 0x22, 0x1A), OnLookChanged));

    public Color PointerColor { get => (Color)GetValue(PointerColorProperty); set => SetValue(PointerColorProperty, value); }
    public static readonly DependencyProperty PointerColorProperty = DependencyProperty.Register(
        nameof(PointerColor), typeof(Color), typeof(BandspreadDial), new FrameworkPropertyMetadata(Color.FromRgb(0xD8, 0x22, 0x18), OnLookChanged));

    public Color PanelColor { get => (Color)GetValue(PanelColorProperty); set => SetValue(PanelColorProperty, value); }
    public static readonly DependencyProperty PanelColorProperty = DependencyProperty.Register(
        nameof(PanelColor), typeof(Color), typeof(BandspreadDial), new FrameworkPropertyMetadata(Color.FromRgb(0x26, 0x27, 0x28), OnLookChanged));

    public bool IsBacklightEnabled { get => (bool)GetValue(IsBacklightEnabledProperty); set => SetValue(IsBacklightEnabledProperty, value); }
    public static readonly DependencyProperty IsBacklightEnabledProperty = DependencyProperty.Register(
        nameof(IsBacklightEnabled), typeof(bool), typeof(BandspreadDial), new FrameworkPropertyMetadata(true, OnLookChanged));

    public double BacklightIntensity { get => (double)GetValue(BacklightIntensityProperty); set => SetValue(BacklightIntensityProperty, value); }
    public static readonly DependencyProperty BacklightIntensityProperty = DependencyProperty.Register(
        nameof(BacklightIntensity), typeof(double), typeof(BandspreadDial), new FrameworkPropertyMetadata(1.0, OnLookChanged));

    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(BandspreadDial), new FrameworkPropertyMetadata(false));

    #endregion

    /// <summary>Wheel rotation per unit actually used.</summary>
    public double EffectiveDegreesPerUnit
    {
        get
        {
            if (!double.IsNaN(DegreesPerUnit) && DegreesPerUnit > 0) return DegreesPerUnit;
            double range = Maximum - Minimum;
            return IsWrapping && range > 0 ? 360 / range : 3;
        }
    }

    /// <summary>Current wheel rotation in degrees, for tests.</summary>
    public double WheelAngle => _wheelRotation.Angle;

    private static object CoerceValue(DependencyObject d, object baseValue)
    {
        var b = (BandspreadDial)d;
        double v = (double)baseValue, min = b.Minimum, max = b.Maximum;
        if (double.IsNaN(v) || !(max > min)) return min;
        if (!b.IsWrapping) return Math.Clamp(v, min, max);
        double range = max - min;
        double w = (v - min) % range;
        if (w < 0) w += range;
        return min + w;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var b = (BandspreadDial)d;
        b.UpdateRotation();
        (UIElementAutomationPeer.FromElement(b) as BandspreadDialAutomationPeer)?.RaiseValueChanged((double)e.OldValue, (double)e.NewValue);
    }

    private static void OnRangeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var b = (BandspreadDial)d;
        b.CoerceValue(ValueProperty);
        b.RebuildAll();
    }

    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((BandspreadDial)d).RebuildAll();

    private void UpdateRotation() => _wheelRotation.Angle = -(Value - Minimum) * EffectiveDegreesPerUnit;

    // ---------------------------------------------------------------- input

    private void SetFromUser(double v)
    {
        if (!IsReadOnly) SetCurrentValue(ValueProperty, v);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (IsReadOnly) return;
        _dragging = CaptureMouse();
        _dragStart = e.GetPosition(this);
        _dragStartValue = Value;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        double k = Math.Max(1e-6, _rootTransform.Matrix.M11);
        double fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 1.0;
        var p = e.GetPosition(this);
        // Dragging right pulls the wheel right, bringing lower numbers under the hairline.
        double degrees = (p.X - _dragStart.X) / (WheelR * k) * 180 / Math.PI * fine;
        SetFromUser(_dragStartValue - degrees / EffectiveDegreesPerUnit);
        if (fine < 1) { _dragStart = p; _dragStartValue = Value; }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (IsReadOnly) return;
        double range = Maximum - Minimum;
        double step = range / (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1000 : 100);
        SetFromUser(Value + Math.Sign(e.Delta) * step);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsReadOnly) return;
        double range = Maximum - Minimum;
        double small = range / (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 1000 : 200);
        double? v = e.Key switch
        {
            Key.Right or Key.Up => Value + small,
            Key.Left or Key.Down => Value - small,
            Key.PageUp => Value + range / 20,
            Key.PageDown => Value - range / 20,
            Key.Home => Minimum,
            Key.End when !IsWrapping => Maximum,
            _ => null,
        };
        if (v == null) return;
        SetFromUser(v.Value);
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); DrawFocus(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); DrawFocus(); }

    // ---------------------------------------------------------------- layout

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = availableSize.Width, h = availableSize.Height;
        if (double.IsInfinity(w) && double.IsInfinity(h)) return new Size(250, 105);
        double k = Math.Min(double.IsInfinity(w) ? double.MaxValue : w / DesignW, double.IsInfinity(h) ? double.MaxValue : h / DesignH);
        return new Size(DesignW * k, DesignH * k);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double k = Math.Min(finalSize.Width / DesignW, finalSize.Height / DesignH);
        _rootTransform.Matrix = new Matrix(k, 0, 0, k, (finalSize.Width - DesignW * k) / 2, (finalSize.Height - DesignH * k) / 2);
        double dppu = k * VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (Math.Abs(dppu - _builtDevicePixelsPerUnit) > 1e-3) RebuildAll();
        return finalSize;
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => RebuildAll();

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;

    // ---------------------------------------------------------------- drawing

    /// <summary>Point on the wheel at radius r and angle φ (degrees clockwise from the top).</summary>
    private static Point P(double r, double phi)
    {
        double a = phi * Math.PI / 180;
        return new Point(Cx + r * Math.Sin(a), Cy - r * Math.Cos(a));
    }

    private void RebuildAll()
    {
        _builtDevicePixelsPerUnit = Math.Max(0.05, _rootTransform.Matrix.M11) * VisualTreeHelper.GetDpi(this).DpiScaleX;
        DrawEscutcheon();
        DrawFace();
        DrawWheel();
        DrawHairline();
        DrawGlass();
        DrawFocus();
        UpdateRotation();
    }

    private static Geometry Poly(params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(pts[0], true, true);
            for (int i = 1; i < pts.Length; i++) ctx.LineTo(pts[i], true, false);
        }
        g.Freeze();
        return g;
    }

    private void DrawEscutcheon()
    {
        using var dc = _escutcheon.RenderOpen();
        var panel = PanelColor;
        var shadow = VintageArt.Frozen(Color.FromArgb(110, 0, 0, 0));
        dc.DrawRoundedRectangle(shadow, null, new Rect(Frame.X + 5, Frame.Y + 9, Frame.Width, Frame.Height), 30, 30);

        // Raised rim
        var rim = new LinearGradientBrush(new GradientStopCollection
        {
            new(VintageArt.Blend(panel, Colors.White, 0.25), 0), new(panel, 0.35), new(VintageArt.Blend(panel, Colors.Black, 0.5), 1),
        }, new Point(0.3, 0), new Point(0.7, 1));
        rim.Freeze();
        dc.DrawRoundedRectangle(rim, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0x05, 0x05, 0x05)), 2.5), Frame, 30, 30);
        dc.DrawRoundedRectangle(VintageArt.GrainBrush(_builtDevicePixelsPerUnit, 0.35), null, Frame, 30, 30);

        // The deep tunnel: four walls slanting in from the opening to the window, each lit differently.
        var o = Opening;
        var w = Window;
        Color Wall(double t) => VintageArt.Blend(Colors.Black, panel, t);
        dc.DrawRoundedRectangle(VintageArt.Frozen(Wall(0.2)), null, o, 16, 16);
        dc.DrawGeometry(VintageArt.Frozen(Wall(0.15)), null, Poly(o.TopLeft, o.TopRight, w.TopRight, w.TopLeft));            // top: in shade
        dc.DrawGeometry(VintageArt.Frozen(VintageArt.Blend(panel, Colors.White, 0.12)), null, Poly(o.TopLeft, w.TopLeft, w.BottomLeft, o.BottomLeft)); // left: catches the light
        dc.DrawGeometry(VintageArt.Frozen(Wall(0.55)), null, Poly(o.TopRight, o.BottomRight, w.BottomRight, w.TopRight));     // right
        dc.DrawGeometry(VintageArt.Frozen(Wall(0.95)), null, Poly(o.BottomLeft, w.BottomLeft, w.BottomRight, o.BottomRight)); // bottom: lit
        var edge = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(60, 255, 255, 255)), 1.5);
        dc.DrawLine(edge, o.TopLeft, w.TopLeft);
        dc.DrawLine(edge, o.BottomLeft, w.BottomLeft);
        dc.DrawRoundedRectangle(null, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(70, 255, 255, 255)), 2), o, 16, 16);
        var inner = w;
        inner.Inflate(4, 4);
        dc.DrawRoundedRectangle(VintageArt.Frozen(Color.FromRgb(0x05, 0x05, 0x05)), null, inner, 10, 10);
    }

    private void DrawFace()
    {
        using var dc = _face.RenderOpen();
        double light = IsBacklightEnabled ? Math.Clamp(BacklightIntensity, 0, 1) : 0;
        var dim = Color.FromRgb(0x30, 0x3A, 0x34);
        Color Lit(Color col) => VintageArt.Blend(dim, col, 0.15 + 0.85 * light);
        var f = FaceColor;
        var face = new RadialGradientBrush(new GradientStopCollection
        {
            new(Lit(VintageArt.Blend(f, Colors.White, 0.4)), 0), new(Lit(f), 0.6), new(Lit(VintageArt.Blend(f, Colors.Black, 0.25)), 1),
        })
        { GradientOrigin = new Point(0.5, 0.45), RadiusX = 0.7, RadiusY = 0.8 };
        face.Freeze();
        dc.DrawRoundedRectangle(face, null, Window, 8, 8);
        dc.DrawRoundedRectangle(VintageArt.GrainBrush(_builtDevicePixelsPerUnit, 0.14), null, Window, 8, 8);
    }

    private void DrawWheel()
    {
        using var dc = _wheel.RenderOpen();
        var scale = Scale;
        double deg = EffectiveDegreesPerUnit, min = Minimum;
        var ink = VintageArt.Frozen(InkColor);
        var minor = VintageArt.FrozenPen(ink, 3);
        var major = VintageArt.FrozenPen(ink, 4.5);
        double Angle(double v) => (v - min) * deg;

        // The scale baseline: the whole printed range, or the full circle for an endless wheel.
        double first = 0, last = IsWrapping ? 360 : Angle(Maximum);
        var arc = new StreamGeometry();
        using (var ctx = arc.Open())
        {
            if (IsWrapping)
            {
                ctx.BeginFigure(P(WheelR, 0), false, false);
                ctx.ArcTo(P(WheelR, 180), new Size(WheelR, WheelR), 0, false, SweepDirection.Clockwise, true, true);
                ctx.ArcTo(P(WheelR, 359.9), new Size(WheelR, WheelR), 0, false, SweepDirection.Clockwise, true, true);
            }
            else
            {
                ctx.BeginFigure(P(WheelR, first), false, false);
                ctx.ArcTo(P(WheelR, last), new Size(WheelR, WheelR), 0, last - first > 180, SweepDirection.Clockwise, true, true);
            }
        }
        arc.Freeze();
        dc.DrawGeometry(null, VintageArt.FrozenPen(ink, 3.5), arc);

        foreach (var v in scale.MinorTicks) dc.DrawLine(minor, P(WheelR, Angle(v)), P(WheelR + 22, Angle(v)));
        foreach (var t in scale.MajorTicks)
        {
            double a = Angle(t.Value);
            dc.DrawLine(major, P(WheelR, a), P(WheelR + 40, a));
            if (t.Label != null)
                VintageArt.DrawCenteredText(dc, VintageArt.Text(t.Label, 48, ink, FontWeights.Bold, 1), P(WheelR - 42, a), a);
        }
    }

    private void DrawHairline()
    {
        using var dc = _hairline.RenderOpen();
        var red = PointerColor;
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(50, 0, 20, 0)), 5), new Point(Cx + 6, Window.Top), new Point(Cx + 6, Window.Bottom));
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(red, 0.35)), 8), new Point(Cx, Window.Top), new Point(Cx, Window.Bottom));
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(red), 3.5), new Point(Cx, Window.Top), new Point(Cx, Window.Bottom));
    }

    private void DrawGlass()
    {
        using var dc = _glass.RenderOpen();
        if (!string.IsNullOrEmpty(Caption))
        {
            var ft = VintageArt.Text(Caption, 26, VintageArt.Frozen(VintageArt.WithAlpha(InkColor, 0.85)), FontWeights.Bold, 1);
            dc.DrawText(ft, new Point(Cx - ft.Width / 2, Window.Bottom - ft.Height - 8));
        }
        // Inner shadow at the top of the window and a faint reflection on the glass
        var shade = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(150, 0, 0, 0), 0), new(Color.FromArgb(0, 0, 0, 0), 0.22), new(Color.FromArgb(0, 0, 0, 0), 0.88), new(Color.FromArgb(70, 0, 0, 0), 1),
        }, new Point(0.5, 0), new Point(0.5, 1));
        shade.Freeze();
        dc.DrawRoundedRectangle(shade, null, Window, 8, 8);
        var glare = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, 255, 255, 255), 0), new(Color.FromArgb(40, 255, 255, 255), 0.45), new(Color.FromArgb(0, 255, 255, 255), 0.6),
        }, new Point(0, 0), new Point(1, 0.4));
        glare.Freeze();
        dc.DrawRoundedRectangle(glare, null, Window, 8, 8);
    }

    private void DrawFocus()
    {
        using var dc = _focus.RenderOpen();
        if (!IsKeyboardFocused) return;
        var pen = new Pen(VintageArt.Frozen(Color.FromArgb(200, 0x6C, 0xF2, 0x4A)), 4) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        var r = Frame;
        r.Inflate(-6, -6);
        dc.DrawRoundedRectangle(null, pen, r, 26, 26);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new BandspreadDialAutomationPeer(this);

    internal void SetValueFromAutomation(double v) => SetCurrentValue(ValueProperty, v);
}

public sealed class BandspreadDialAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    public BandspreadDialAutomationPeer(BandspreadDial owner) : base(owner) { }
    private BandspreadDial Dial => (BandspreadDial)Owner;
    protected override string GetClassNameCore() => nameof(BandspreadDial);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        return !string.IsNullOrEmpty(name) ? name : $"Bandspread dial, {Dial.Value.ToString("0.0", CultureInfo.InvariantCulture)}";
    }
    public override object GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface)!;

    bool IRangeValueProvider.IsReadOnly => Dial.IsReadOnly;
    double IRangeValueProvider.LargeChange => (Dial.Maximum - Dial.Minimum) / 20;
    double IRangeValueProvider.SmallChange => (Dial.Maximum - Dial.Minimum) / 200;
    double IRangeValueProvider.Maximum => Dial.Maximum;
    double IRangeValueProvider.Minimum => Dial.Minimum;
    double IRangeValueProvider.Value => Dial.Value;
    void IRangeValueProvider.SetValue(double value)
    {
        if (Dial.IsReadOnly) throw new ElementNotEnabledException();
        Dial.SetValueFromAutomation(value);
    }

    internal void RaiseValueChanged(double oldValue, double newValue) =>
        RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, oldValue, newValue);
}
