using System.Globalization;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioCentric.Controls.Scales;
using AudioCentric.Controls.Wpf.Rendering;

namespace AudioCentric.Controls.Wpf.Controls;

/// <summary>
/// Mixing-console channel fader. It publishes a target gain in dB (<see cref="ValueDb"/>, two-way) through a
/// <see cref="FaderTaper"/>. The bottom of travel is −∞ (off). Mute is a separate state.
/// Interaction: absolute-position drag (click the track to jump), Shift = fine, wheel, arrows,
/// Home/End, double-click to reset to unity. The host converts dB to amplitude and smooths gain.
/// </summary>
public class ConsoleFader : FrameworkElement
{
    // Design frame: "along" = travel axis (0 at the bottom/left … _len), "across" = 0 … 260.
    // The across axis sets the scale; the length follows the control's aspect ratio, so a fader can be any length.
    private const double Wid = 260;
    private const double MinLen = 400;
    private const double TravelInset = 95;
    private double _len = 1000;
    private double TravelLow => TravelInset;
    private double TravelHigh => _len - TravelInset;
    private const double SlotCenter = 150;
    private const double CapLength = 118, CapAcross0 = 86, CapAcross1 = 214;

    private readonly VisualCollection _children;
    private readonly ContainerVisual _root = new();
    private readonly DrawingVisual _static = new();
    private readonly DrawingVisual _cap = new();
    private readonly TranslateTransform _capOffset = new();
    private readonly MatrixTransform _rootTransform = new();

    private bool _dragging;
    private double _dragGrabOffset;

    static ConsoleFader()
    {
        FocusableProperty.OverrideMetadata(typeof(ConsoleFader), new FrameworkPropertyMetadata(true));
        FocusVisualStyleProperty.OverrideMetadata(typeof(ConsoleFader), new FrameworkPropertyMetadata(null));
    }

    public ConsoleFader()
    {
        _children = new VisualCollection(this) { _root };
        _root.Transform = _rootTransform;
        _root.Children.Add(_static);
        _root.Children.Add(_cap);
        _cap.Transform = _capOffset;
        Redraw();
    }

    #region Dependency properties

    /// <summary>Target gain in dB. −∞ at the bottom of travel.</summary>
    public double ValueDb { get => (double)GetValue(ValueDbProperty); set => SetValue(ValueDbProperty, value); }
    public static readonly DependencyProperty ValueDbProperty = DependencyProperty.Register(
        nameof(ValueDb), typeof(double), typeof(ConsoleFader),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnValueChanged, CoerceValueDb));

    public bool IsMuted { get => (bool)GetValue(IsMutedProperty); set => SetValue(IsMutedProperty, value); }
    public static readonly DependencyProperty IsMutedProperty = DependencyProperty.Register(
        nameof(IsMuted), typeof(bool), typeof(ConsoleFader),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((ConsoleFader)d).DrawCap()));

    public FaderTaper Taper { get => (FaderTaper)GetValue(TaperProperty); set => SetValue(TaperProperty, value); }
    public static readonly DependencyProperty TaperProperty = DependencyProperty.Register(
        nameof(Taper), typeof(FaderTaper), typeof(ConsoleFader), new FrameworkPropertyMetadata(FaderTaper.Default, (d, _) => ((ConsoleFader)d).Redraw()));

    public double DefaultValueDb { get => (double)GetValue(DefaultValueDbProperty); set => SetValue(DefaultValueDbProperty, value); }
    public static readonly DependencyProperty DefaultValueDbProperty = DependencyProperty.Register(
        nameof(DefaultValueDb), typeof(double), typeof(ConsoleFader), new FrameworkPropertyMetadata(0.0));

    public Orientation Orientation { get => (Orientation)GetValue(OrientationProperty); set => SetValue(OrientationProperty, value); }
    public static readonly DependencyProperty OrientationProperty = DependencyProperty.Register(
        nameof(Orientation), typeof(Orientation), typeof(ConsoleFader),
        new FrameworkPropertyMetadata(Orientation.Vertical, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange, (d, _) => ((ConsoleFader)d).Redraw()));

    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(ConsoleFader), new FrameworkPropertyMetadata(false));

    public Color IndicatorColor { get => (Color)GetValue(IndicatorColorProperty); set => SetValue(IndicatorColorProperty, value); }
    public static readonly DependencyProperty IndicatorColorProperty = DependencyProperty.Register(
        nameof(IndicatorColor), typeof(Color), typeof(ConsoleFader),
        new FrameworkPropertyMetadata(Color.FromRgb(0x6C, 0xF2, 0x4A), (d, _) => ((ConsoleFader)d).DrawCap()));

    #endregion

    /// <summary>Normalized travel position 0..1 of <see cref="ValueDb"/>.</summary>
    public double Position => Taper.ToPosition(ValueDb);

    /// <summary>Linear gain the host should apply (0 when muted or at −∞).</summary>
    public double Gain => IsMuted ? 0 : Taper.ToGain(Position);

    private static object CoerceValueDb(DependencyObject d, object baseValue)
    {
        var f = (ConsoleFader)d;
        double v = (double)baseValue;
        if (double.IsNaN(v)) return f.ValueDb;
        if (double.IsNegativeInfinity(v)) return v;
        return Math.Min(v, f.Taper.MaximumDb);
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var f = (ConsoleFader)d;
        f.PlaceCap();
        (UIElementAutomationPeer.FromElement(f) as ConsoleFaderAutomationPeer)?.RaiseValueChanged((double)e.OldValue, (double)e.NewValue);
    }

    private void SetPosition(double position)
    {
        double p = Math.Clamp(position, 0, 1);
        double db = Taper.ToDb(p);
        // Round finite values to 0.1 dB for a stable readout; keep exact anchors such as unity.
        if (!double.IsNegativeInfinity(db)) db = Math.Round(db, 1);
        SetCurrentValue(ValueDbProperty, db);
    }

    // ---------------------------------------------------------------- geometry

    private bool Vertical => Orientation == Orientation.Vertical;

    /// <summary>Design-space point for (along, across).</summary>
    private Point P(double along, double across) => Vertical ? new Point(across, _len - along) : new Point(along, across);

    private Rect R(double along0, double along1, double across0, double across1)
    {
        var a = P(along0, across0);
        var b = P(along1, across1);
        return new Rect(a, b);
    }

    private double AlongFor(double position) => TravelLow + position * (TravelHigh - TravelLow);

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        // Take all the length offered along the travel axis; stay slim across it.
        double along = Vertical ? availableSize.Height : availableSize.Width;
        double across = Vertical ? availableSize.Width : availableSize.Height;
        along = double.IsInfinity(along) ? 250 : along;
        across = double.IsInfinity(across) ? 65 : Math.Min(across, 80);
        return Vertical ? new Size(across, along) : new Size(along, across);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double along = Vertical ? finalSize.Height : finalSize.Width;
        double across = Vertical ? finalSize.Width : finalSize.Height;
        double k = Math.Min(across / Wid, along / MinLen);
        if (k <= 0 || double.IsNaN(k)) return finalSize;
        double len = along / k;
        if (Math.Abs(len - _len) > 0.5)
        {
            _len = len;
            Redraw();
        }
        double dw = Vertical ? Wid : _len, dh = Vertical ? _len : Wid;
        _rootTransform.Matrix = new Matrix(k, 0, 0, k, (finalSize.Width - dw * k) / 2, (finalSize.Height - dh * k) / 2);
        return finalSize;
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;

    private void Redraw()
    {
        DrawStatic();
        DrawCap();
    }

    private static readonly (double Db, string Label)[] Marks =
    {
        (10, "+10"), (5, "+5"), (0, "0"), (-5, "5"), (-10, "10"), (-20, "20"), (-30, "30"), (-40, "40"), (-60, "60"), (double.NegativeInfinity, "∞"),
    };

    private void DrawStatic()
    {
        var taper = Taper;
        var ppd = 1.0;
        using var dc = _static.RenderOpen();

        // Panel strip
        var panel = new LinearGradientBrush(Color.FromRgb(0x1E, 0x20, 0x1E), Color.FromRgb(0x14, 0x15, 0x14), Vertical ? 0 : 90);
        panel.Freeze();
        dc.DrawRoundedRectangle(panel, null, R(0, _len, 0, Wid), 14, 14);

        // Slot
        var slotRect = R(TravelLow - 55, TravelHigh + 55, SlotCenter - 11, SlotCenter + 11);
        dc.DrawRoundedRectangle(VintageArt.Frozen(Color.FromRgb(0x02, 0x02, 0x02)), VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0x3A, 0x3C, 0x3A)), 2), slotRect, 11, 11);
        var inner = R(TravelLow - 50, TravelHigh + 50, SlotCenter - 4, SlotCenter + 4);
        dc.DrawRoundedRectangle(VintageArt.Frozen(Color.FromRgb(0x0C, 0x0D, 0x0C)), null, inner, 4, 4);

        var ink = VintageArt.Frozen(Color.FromRgb(0xB0, 0xBA, 0xB2));
        var dimInk = VintageArt.Frozen(Color.FromRgb(0x7A, 0x84, 0x7C));
        foreach (var (db, label) in Marks)
        {
            double along = AlongFor(taper.ToPosition(db));
            bool unity = db == 0;
            var pen = VintageArt.FrozenPen(unity ? ink : dimInk, unity ? 6 : 3.5);
            dc.DrawLine(pen, P(along, unity ? 66 : 80), P(along, 118));
            dc.DrawLine(pen, P(along, 182), P(along, unity ? 234 : 220));
            var ft = VintageArt.Text(label, unity ? 40 : 34, unity ? ink : dimInk, unity ? FontWeights.Bold : FontWeights.SemiBold, ppd);
            var labelPos = Vertical ? P(along, 36) : P(along, 30);
            if (Vertical && (along > TravelHigh + 1 || along < TravelLow - 1)) continue;
            VintageArt.DrawCenteredText(dc, ft, labelPos);
        }
        // Minor marks every 2.5 dB between -10 and +10
        var minor = VintageArt.FrozenPen(dimInk, 2);
        for (double db = -7.5; db <= 7.5; db += 5)
        {
            double along = AlongFor(taper.ToPosition(db));
            dc.DrawLine(minor, P(along, 96), P(along, 118));
            dc.DrawLine(minor, P(along, 182), P(along, 204));
        }
    }

    private void DrawCap()
    {
        using var dc = _cap.RenderOpen();
        double half = CapLength / 2;
        // The cap is drawn centred at along = 0 and moved by _capOffset.
        var rect = R(-half, half, CapAcross0, CapAcross1);
        var shadowRect = rect;
        shadowRect.Offset(Vertical ? 6 : 6, 12);
        dc.DrawRoundedRectangle(VintageArt.Frozen(Color.FromArgb(120, 0, 0, 0)), null, shadowRect, 14, 14);

        var body = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x50, 0x50, 0x50), 0.0),
            new(Color.FromRgb(0x24, 0x24, 0x24), 0.18),
            new(Color.FromRgb(0x12, 0x12, 0x12), 0.5),
            new(Color.FromRgb(0x2A, 0x2A, 0x2A), 0.82),
            new(Color.FromRgb(0x08, 0x08, 0x08), 1.0),
        }, Vertical ? new Point(0.5, 0) : new Point(0, 0.5), Vertical ? new Point(0.5, 1) : new Point(1, 0.5));
        body.Freeze();
        dc.DrawRoundedRectangle(body, VintageArt.FrozenPen(VintageArt.Frozen(Colors.Black), 2), rect, 12, 12);
        dc.DrawRoundedRectangle(VintageArt.GrainBrush(1.0, 0.35), null, rect, 12, 12);

        // Finger ridges
        var ridgeDark = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(170, 0, 0, 0)), 3);
        var ridgeLight = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(55, 255, 255, 255)), 2);
        for (int i = -3; i <= 3; i++)
        {
            if (i == 0) continue;
            double a = i * 14;
            dc.DrawLine(ridgeDark, P(a, CapAcross0 + 10), P(a, CapAcross1 - 10));
            dc.DrawLine(ridgeLight, P(a + 3, CapAcross0 + 10), P(a + 3, CapAcross1 - 10));
        }

        // Indicator line
        var c = IsMuted ? Color.FromRgb(0xFF, 0x3A, 0x26) : IndicatorColor;
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(c, 0.35)), 12, PenLineCap.Round), P(0, CapAcross0 + 8), P(0, CapAcross1 - 8));
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(c), 5, PenLineCap.Round), P(0, CapAcross0 + 8), P(0, CapAcross1 - 8));

        if (IsKeyboardFocused)
        {
            var focus = new Pen(VintageArt.Frozen(VintageArt.WithAlpha(IndicatorColor, 0.8)), 3) { DashStyle = DashStyles.Dash };
            focus.Freeze();
            var fr = rect;
            fr.Inflate(8, 8);
            dc.DrawRoundedRectangle(null, focus, fr, 16, 16);
        }
        PlaceCap();
    }

    private void PlaceCap()
    {
        double along = AlongFor(Position);
        if (Vertical) { _capOffset.X = 0; _capOffset.Y = -along; }
        else { _capOffset.X = along; _capOffset.Y = 0; }
    }

    // ---------------------------------------------------------------- input

    private double AlongFromMouse(Point p)
    {
        var m = _rootTransform.Matrix;
        if (!m.HasInverse) return 0;
        m.Invert();
        var d = m.Transform(p);
        return Vertical ? _len - d.Y : d.X;
    }

    private double PositionFromAlong(double along) => (along - TravelLow) / (TravelHigh - TravelLow);

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (IsReadOnly) return;
        if (e.ClickCount == 2)
        {
            SetCurrentValue(ValueDbProperty, DefaultValueDb);
            e.Handled = true;
            return;
        }
        double along = AlongFromMouse(e.GetPosition(this));
        double capAlong = AlongFor(Position);
        // Grab the cap where it was clicked; clicking the track jumps the cap there (absolute position).
        _dragGrabOffset = Math.Abs(along - capAlong) <= CapLength / 2 ? along - capAlong : 0;
        if (_dragGrabOffset == 0) SetPosition(PositionFromAlong(along));
        _dragging = CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        double along = AlongFromMouse(e.GetPosition(this)) - _dragGrabOffset;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            double current = AlongFor(Position);
            along = current + (along - current) * 0.1;
            _dragGrabOffset = AlongFromMouse(e.GetPosition(this)) - along;
        }
        SetPosition(PositionFromAlong(along));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragging) { _dragging = false; ReleaseMouseCapture(); }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (IsReadOnly) return;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.002 : 0.015;
        SetPosition(Position + Math.Sign(e.Delta) * step);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsReadOnly) return;
        double fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 1.0;
        switch (e.Key)
        {
            case Key.Up: case Key.Right: SetPosition(Position + 0.01 * fine); break;
            case Key.Down: case Key.Left: SetPosition(Position - 0.01 * fine); break;
            case Key.PageUp: SetPosition(Position + 0.1); break;
            case Key.PageDown: SetPosition(Position - 0.1); break;
            case Key.Home: SetPosition(0); break;
            case Key.End: SetPosition(1); break;
            case Key.Delete: case Key.Back: SetCurrentValue(ValueDbProperty, DefaultValueDb); break;
            default: return;
        }
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); DrawCap(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); DrawCap(); }

    protected override AutomationPeer OnCreateAutomationPeer() => new ConsoleFaderAutomationPeer(this);

    /// <summary>Formats a dB value for readouts: "+3.5 dB", "0.0 dB", "−∞ dB".</summary>
    public static string FormatDb(double db) =>
        double.IsNegativeInfinity(db) ? "−∞ dB" : (db > 0.05 ? "+" : "") + db.ToString("0.0", CultureInfo.InvariantCulture) + " dB";
}

public sealed class ConsoleFaderAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    public ConsoleFaderAutomationPeer(ConsoleFader owner) : base(owner) { }
    private ConsoleFader Fader => (ConsoleFader)Owner;
    protected override string GetClassNameCore() => nameof(ConsoleFader);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    public override object GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface)!;

    bool IRangeValueProvider.IsReadOnly => Fader.IsReadOnly;
    double IRangeValueProvider.LargeChange => 6;
    double IRangeValueProvider.SmallChange => 0.5;
    // UI Automation cannot represent −∞; report the lowest finite anchor at the bottom of travel.
    double IRangeValueProvider.Minimum => Fader.Taper.MinimumFiniteDb;
    double IRangeValueProvider.Maximum => Fader.Taper.MaximumDb;
    double IRangeValueProvider.Value => double.IsNegativeInfinity(Fader.ValueDb) ? Fader.Taper.MinimumFiniteDb : Fader.ValueDb;
    void IRangeValueProvider.SetValue(double value)
    {
        if (Fader.IsReadOnly) throw new System.Windows.Automation.ElementNotEnabledException();
        Fader.SetCurrentValue(ConsoleFader.ValueDbProperty, Math.Clamp(value, Fader.Taper.MinimumFiniteDb, Fader.Taper.MaximumDb));
    }

    internal void RaiseValueChanged(double oldValue, double newValue) =>
        RaisePropertyChangedEvent(System.Windows.Automation.RangeValuePatternIdentifiers.ValueProperty, oldValue, newValue);
}
