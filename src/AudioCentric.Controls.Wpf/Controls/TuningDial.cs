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
/// Large backlit tuning dial in the style of a vintage communications receiver. Several band scales and a logging scale
/// are printed on a disc that turns behind a fixed red hairline, seen through a half-moon window. Below the window sits
/// a cover plate, which <see cref="ShowCoverPlate"/> can hide.
/// <para>
/// <see cref="Position"/> (0…1 of the dial's travel) and <see cref="Frequency"/> (in the selected band's units) are both
/// two-way and kept in sync through the band's mapping. Switching <see cref="SelectedBandIndex"/> keeps the disc where it
/// is and reads the new band, like a real band switch. The dial publishes the tuning the user asked for; the host tunes.
/// </para>
/// <para>Mouse: drag the disc sideways (it follows the pointer), wheel to tune (Shift: fine). Keys: ←/→ (Shift: fine), PgUp/PgDn, Home/End.</para>
/// </summary>
public class TuningDial : FrameworkElement
{
    private const double DesignW = 1000, DesignFull = 1000, DesignCropped = 520;
    private const double Cx = 500, Cy = 505, WindowR = 440, WindowBottom = 485, RingR = 474;

    private readonly VisualCollection _children;
    private readonly ContainerVisual _root = new();
    private readonly DrawingVisual _bezel = new();
    private readonly DrawingVisual _face = new();
    private readonly ContainerVisual _window = new();
    private readonly DrawingVisual _disc = new();
    private readonly DrawingVisual _pointer = new();
    private readonly DrawingVisual _glass = new();
    private readonly DrawingVisual _cover = new();
    private readonly DrawingVisual _focus = new();
    private readonly RotateTransform _discRotation = new(0, Cx, Cy);
    private readonly MatrixTransform _rootTransform = new();
    private double _builtDevicePixelsPerUnit;
    private bool _syncing;
    private bool _dragging;
    private Point _dragStart;
    private double _dragStartPosition;

    static TuningDial()
    {
        FocusableProperty.OverrideMetadata(typeof(TuningDial), new FrameworkPropertyMetadata(true));
        FocusVisualStyleProperty.OverrideMetadata(typeof(TuningDial), new FrameworkPropertyMetadata(null));
        CursorProperty.OverrideMetadata(typeof(TuningDial), new FrameworkPropertyMetadata(Cursors.SizeWE));
    }

    public TuningDial()
    {
        _children = new VisualCollection(this) { _root };
        _root.Transform = _rootTransform;
        _root.Children.Add(_bezel);
        _root.Children.Add(_face);
        _root.Children.Add(_window);
        _window.Children.Add(_disc);
        _window.Children.Add(_pointer);
        _window.Clip = WindowGeometry();
        _disc.Transform = _discRotation;
        _root.Children.Add(_glass);
        _root.Children.Add(_cover);
        _root.Children.Add(_focus);
        RebuildAll();
        SyncFrequencyFromPosition();
    }

    #region Dependency properties

    /// <summary>The printed bands, innermost last. Default <see cref="DialPresets.ShortwaveReceiver"/>. Up to about five fit.</summary>
    public IReadOnlyList<ScaleDefinition> Bands { get => (IReadOnlyList<ScaleDefinition>)GetValue(BandsProperty); set => SetValue(BandsProperty, value); }
    public static readonly DependencyProperty BandsProperty = DependencyProperty.Register(
        nameof(Bands), typeof(IReadOnlyList<ScaleDefinition>), typeof(TuningDial),
        new FrameworkPropertyMetadata(DialPresets.ShortwaveReceiver, OnBandsChanged));

    /// <summary>Outer logging scale (default 0–100). Null hides it.</summary>
    public ScaleDefinition? LoggingScale { get => (ScaleDefinition?)GetValue(LoggingScaleProperty); set => SetValue(LoggingScaleProperty, value); }
    public static readonly DependencyProperty LoggingScaleProperty = DependencyProperty.Register(
        nameof(LoggingScale), typeof(ScaleDefinition), typeof(TuningDial), new FrameworkPropertyMetadata(DialPresets.LoggingScale, OnLookChanged));

    public int SelectedBandIndex { get => (int)GetValue(SelectedBandIndexProperty); set => SetValue(SelectedBandIndexProperty, value); }
    public static readonly DependencyProperty SelectedBandIndexProperty = DependencyProperty.Register(
        nameof(SelectedBandIndex), typeof(int), typeof(TuningDial),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBandsChanged, CoerceBandIndex));

    /// <summary>Dial position, 0 (start of travel) … 1 (end).</summary>
    public double Position { get => (double)GetValue(PositionProperty); set => SetValue(PositionProperty, value); }
    public static readonly DependencyProperty PositionProperty = DependencyProperty.Register(
        nameof(Position), typeof(double), typeof(TuningDial),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnPositionChanged, (_, v) => double.IsNaN((double)v) ? 0.0 : Math.Clamp((double)v, 0, 1)));

    /// <summary>Tuned frequency in the selected band's units (e.g. MHz or Hz).</summary>
    public double Frequency { get => (double)GetValue(FrequencyProperty); set => SetValue(FrequencyProperty, value); }
    public static readonly DependencyProperty FrequencyProperty = DependencyProperty.Register(
        nameof(Frequency), typeof(double), typeof(TuningDial),
        new FrameworkPropertyMetadata(double.NaN, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnFrequencyChanged));

    /// <summary>Disc rotation over the full travel, in degrees. Default 180.</summary>
    public double TravelAngle { get => (double)GetValue(TravelAngleProperty); set => SetValue(TravelAngleProperty, value); }
    public static readonly DependencyProperty TravelAngleProperty = DependencyProperty.Register(
        nameof(TravelAngle), typeof(double), typeof(TuningDial), new FrameworkPropertyMetadata(180.0, OnLookChanged), v => (double)v is > 10 and <= 300);

    public bool ShowCoverPlate { get => (bool)GetValue(ShowCoverPlateProperty); set => SetValue(ShowCoverPlateProperty, value); }
    public static readonly DependencyProperty ShowCoverPlateProperty = DependencyProperty.Register(
        nameof(ShowCoverPlate), typeof(bool), typeof(TuningDial), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsMeasure, OnLookChanged));

    /// <summary>Print the bands that are not selected in a lighter ink, so the active scale stands out.</summary>
    public bool DimInactiveBands { get => (bool)GetValue(DimInactiveBandsProperty); set => SetValue(DimInactiveBandsProperty, value); }
    public static readonly DependencyProperty DimInactiveBandsProperty = DependencyProperty.Register(
        nameof(DimInactiveBands), typeof(bool), typeof(TuningDial), new FrameworkPropertyMetadata(true, OnLookChanged));

    public Color FaceColor { get => (Color)GetValue(FaceColorProperty); set => SetValue(FaceColorProperty, value); }
    public static readonly DependencyProperty FaceColorProperty = DependencyProperty.Register(
        nameof(FaceColor), typeof(Color), typeof(TuningDial), new FrameworkPropertyMetadata(Color.FromRgb(0xB4, 0xE6, 0xC8), OnLookChanged));

    public Color InkColor { get => (Color)GetValue(InkColorProperty); set => SetValue(InkColorProperty, value); }
    public static readonly DependencyProperty InkColorProperty = DependencyProperty.Register(
        nameof(InkColor), typeof(Color), typeof(TuningDial), new FrameworkPropertyMetadata(Color.FromRgb(0x14, 0x22, 0x1A), OnLookChanged));

    /// <summary>Colour of the hairline and the band markers.</summary>
    public Color PointerColor { get => (Color)GetValue(PointerColorProperty); set => SetValue(PointerColorProperty, value); }
    public static readonly DependencyProperty PointerColorProperty = DependencyProperty.Register(
        nameof(PointerColor), typeof(Color), typeof(TuningDial), new FrameworkPropertyMetadata(Color.FromRgb(0xD8, 0x22, 0x18), OnLookChanged));

    /// <summary>Colour of the bezel and cover plate (bakelite).</summary>
    public Color PanelColor { get => (Color)GetValue(PanelColorProperty); set => SetValue(PanelColorProperty, value); }
    public static readonly DependencyProperty PanelColorProperty = DependencyProperty.Register(
        nameof(PanelColor), typeof(Color), typeof(TuningDial), new FrameworkPropertyMetadata(Color.FromRgb(0x26, 0x27, 0x28), OnLookChanged));

    public bool IsBacklightEnabled { get => (bool)GetValue(IsBacklightEnabledProperty); set => SetValue(IsBacklightEnabledProperty, value); }
    public static readonly DependencyProperty IsBacklightEnabledProperty = DependencyProperty.Register(
        nameof(IsBacklightEnabled), typeof(bool), typeof(TuningDial), new FrameworkPropertyMetadata(true, OnLookChanged));

    public double BacklightIntensity { get => (double)GetValue(BacklightIntensityProperty); set => SetValue(BacklightIntensityProperty, value); }
    public static readonly DependencyProperty BacklightIntensityProperty = DependencyProperty.Register(
        nameof(BacklightIntensity), typeof(double), typeof(TuningDial), new FrameworkPropertyMetadata(1.0, OnLookChanged));

    public bool IsReadOnly { get => (bool)GetValue(IsReadOnlyProperty); set => SetValue(IsReadOnlyProperty, value); }
    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(TuningDial), new FrameworkPropertyMetadata(false));

    #endregion

    /// <summary>The selected band, or null when there are no bands.</summary>
    public ScaleDefinition? SelectedBand => Bands is { Count: > 0 } b ? b[Math.Clamp(SelectedBandIndex, 0, b.Count - 1)] : null;

    /// <summary>Current disc rotation in degrees (clockwise), for tests.</summary>
    public double DiscAngle => _discRotation.Angle;

    private static object CoerceBandIndex(DependencyObject d, object v)
    {
        var bands = ((TuningDial)d).Bands;
        return bands is { Count: > 0 } ? Math.Clamp((int)v, 0, bands.Count - 1) : 0;
    }

    private static void OnBandsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var t = (TuningDial)d;
        if (e.Property == BandsProperty) t.CoerceValue(SelectedBandIndexProperty);
        t.DrawDisc();
        t.SyncFrequencyFromPosition();
    }

    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((TuningDial)d).RebuildAll();

    private static void OnPositionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var t = (TuningDial)d;
        t._discRotation.Angle = t.Position * t.TravelAngle;
        if (!t._syncing) t.SyncFrequencyFromPosition();
        (UIElementAutomationPeer.FromElement(t) as TuningDialAutomationPeer)?.RaiseValueChanged((double)e.OldValue, (double)e.NewValue);
    }

    private static void OnFrequencyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var t = (TuningDial)d;
        if (t._syncing || t.SelectedBand is not { } band) return;
        // NaN means "not set": the dial supplies the frequency for its current position (and a two-way binding receives it).
        // WPF ignores a value pushed back while it is applying the source's value, so do it once that has finished.
        if (double.IsNaN(t.Frequency))
        {
            t.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.DataBind, new Action(() =>
            {
                if (double.IsNaN(t.Frequency)) t.SyncFrequencyFromPosition();
            }));
            return;
        }
        t._syncing = true;
        try { t.SetCurrentValue(PositionProperty, band.ToNormalized(Math.Clamp(t.Frequency, band.Minimum, band.Maximum))); }
        finally { t._syncing = false; }
    }

    private void SyncFrequencyFromPosition()
    {
        _discRotation.Angle = Position * TravelAngle;
        var band = SelectedBand;
        _syncing = true;
        try { SetCurrentValue(FrequencyProperty, band == null ? double.NaN : band.FromNormalized(Position)); }
        finally { _syncing = false; }
    }

    // ---------------------------------------------------------------- input

    private void SetPositionFromUser(double p)
    {
        if (IsReadOnly) return;
        SetCurrentValue(PositionProperty, Math.Clamp(p, 0, 1));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (IsReadOnly) return;
        _dragging = CaptureMouse();
        _dragStart = e.GetPosition(this);
        _dragStartPosition = Position;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;
        // The disc follows the pointer along the top of the window: dx pixels = dx / (R·scale) radians of rotation.
        double k = Math.Max(1e-6, _rootTransform.Matrix.M11);
        double fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 1.0;
        var p = e.GetPosition(this);
        double degrees = (p.X - _dragStart.X) / (WindowR * 0.8 * k) * 180 / Math.PI * fine;
        SetPositionFromUser(_dragStartPosition + degrees / TravelAngle);
        if (fine < 1) { _dragStart = p; _dragStartPosition = Position; }
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
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.0005 : 0.005;
        SetPositionFromUser(Position + Math.Sign(e.Delta) * step);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (IsReadOnly) return;
        double fine = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 1.0;
        double? p = e.Key switch
        {
            Key.Right or Key.Up => Position + 0.002 * fine,
            Key.Left or Key.Down => Position - 0.002 * fine,
            Key.PageUp => Position + 0.05,
            Key.PageDown => Position - 0.05,
            Key.Home => 0,
            Key.End => 1,
            _ => null,
        };
        if (p == null) return;
        SetPositionFromUser(p.Value);
        e.Handled = true;
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); DrawFocus(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); DrawFocus(); }

    // ---------------------------------------------------------------- layout

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    private double DesignH => ShowCoverPlate ? DesignFull : DesignCropped;

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = availableSize.Width, h = availableSize.Height;
        if (double.IsInfinity(w) && double.IsInfinity(h)) return new Size(400, 400 * DesignH / DesignW);
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

    /// <summary>Point at radius r and printed angle θ (degrees, 0 = up, positive = to the left).</summary>
    private static Point P(double r, double theta)
    {
        double a = theta * Math.PI / 180;
        return new Point(Cx - r * Math.Sin(a), Cy - r * Math.Cos(a));
    }

    private static Geometry WindowGeometry()
    {
        var g = Geometry.Combine(new EllipseGeometry(new Point(Cx, Cy), WindowR, WindowR),
            new RectangleGeometry(new Rect(0, 0, DesignW, WindowBottom)), GeometryCombineMode.Intersect, null);
        g.Freeze();
        return g;
    }

    private void RebuildAll()
    {
        _builtDevicePixelsPerUnit = Math.Max(0.05, _rootTransform.Matrix.M11) * VisualTreeHelper.GetDpi(this).DpiScaleX;
        DrawBezel();
        DrawFace();
        DrawDisc();
        DrawPointer();
        DrawGlass();
        DrawCover();
        DrawFocus();
        _discRotation.Angle = Position * TravelAngle;
    }

    private double Light => IsBacklightEnabled ? Math.Clamp(BacklightIntensity, 0, 1) : 0;

    private void DrawBezel()
    {
        using var dc = _bezel.RenderOpen();
        var panel = PanelColor;
        var c = new Point(Cx, Cy);
        dc.PushClip(new RectangleGeometry(new Rect(0, 0, DesignW, DesignH)));
        var shadow = new RadialGradientBrush(new GradientStopCollection { new(Color.FromArgb(150, 0, 0, 0), 0.93), new(Color.FromArgb(0, 0, 0, 0), 1) });
        shadow.Freeze();
        dc.DrawEllipse(shadow, null, new Point(Cx + 4, Cy + 8), RingR + 22, RingR + 22);
        var ring = new LinearGradientBrush(new GradientStopCollection
        {
            new(VintageArt.Blend(panel, Colors.White, 0.22), 0), new(panel, 0.45), new(VintageArt.Blend(panel, Colors.Black, 0.55), 1),
        }, new Point(0.2, 0), new Point(0.8, 1));
        ring.Freeze();
        dc.DrawEllipse(ring, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0x05, 0x05, 0x05)), 2.5), c, RingR, RingR);
        dc.DrawEllipse(VintageArt.GrainBrush(_builtDevicePixelsPerUnit, 0.35), null, c, RingR, RingR);
        // Recess around the window, darker at the top where the bezel shades it
        var recess = new LinearGradientBrush(Color.FromRgb(0x04, 0x04, 0x04), VintageArt.Blend(panel, Colors.Black, 0.3), 90);
        recess.Freeze();
        dc.DrawEllipse(recess, null, c, WindowR + 12, WindowR + 12);
        dc.Pop();
    }

    private void DrawFace()
    {
        using var dc = _face.RenderOpen();
        double light = Light;
        var dim = Color.FromRgb(0x30, 0x3A, 0x34);
        Color Lit(Color col) => VintageArt.Blend(dim, col, 0.15 + 0.85 * light);
        var f = FaceColor;
        var face = new RadialGradientBrush(new GradientStopCollection
        {
            new(Lit(VintageArt.Blend(f, Colors.White, 0.45)), 0),
            new(Lit(VintageArt.Blend(f, Colors.White, 0.12)), 0.45),
            new(Lit(f), 0.75),
            new(Lit(VintageArt.Blend(f, Colors.Black, 0.28)), 1),
        })
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(Cx, Cy - 160),
            GradientOrigin = new Point(Cx, Cy - 120),
            RadiusX = 520,
            RadiusY = 420,
        };
        face.Freeze();
        var window = WindowGeometry();
        dc.DrawGeometry(face, null, window);
        dc.DrawGeometry(VintageArt.GrainBrush(_builtDevicePixelsPerUnit, 0.14), null, window);
    }

    private (double Outer, double Spacing) Layout(int bands)
    {
        const double outer = 322, inner = 128;
        return bands <= 1 ? (outer, 0) : (outer, (outer - inner) / (bands - 1));
    }

    private void DrawDisc()
    {
        using var dc = _disc.RenderOpen();
        var bands = Bands ?? Array.Empty<ScaleDefinition>();
        double travel = TravelAngle;
        var ink = InkColor;
        var red = PointerColor;
        int selected = Math.Clamp(SelectedBandIndex, 0, Math.Max(0, bands.Count - 1));

        // Logging scale round the edge
        if (LoggingScale is { } log)
        {
            const double r = 410;
            var brush = VintageArt.Frozen(VintageArt.WithAlpha(ink, 0.85));
            var minor = VintageArt.FrozenPen(brush, 2.2);
            var major = VintageArt.FrozenPen(brush, 3.4);
            foreach (var v in log.MinorTicks) { double a = log.ToNormalized(v) * travel; dc.DrawLine(minor, P(r, a), P(r + 11, a)); }
            foreach (var t in log.MajorTicks)
            {
                double a = log.ToNormalized(t.Value) * travel;
                dc.DrawLine(major, P(r, a), P(r + 20, a));
                if (t.Label != null) VintageArt.DrawCenteredText(dc, VintageArt.Text(t.Label, 19, brush, FontWeights.SemiBold, 1), P(r - 15, a), -a);
            }
            DrawArc(dc, r, 0, travel, VintageArt.FrozenPen(brush, 2));
            if (!string.IsNullOrEmpty(log.Caption)) DrawTextOnArc(dc, log.Caption, r - 2, -5, 18, brush, FontWeights.Bold);
        }

        if (bands.Count == 0) return;
        var (outer, spacing) = Layout(bands.Count);
        for (int b = 0; b < bands.Count; b++)
        {
            var band = bands[b];
            double r = outer - b * spacing;
            bool active = b == selected;
            double alpha = active || !DimInactiveBands ? 1.0 : 0.5;
            var brush = VintageArt.Frozen(VintageArt.WithAlpha(ink, alpha));
            var redBrush = VintageArt.Frozen(VintageArt.WithAlpha(red, active || !DimInactiveBands ? 1.0 : 0.6));

            if (active)
            {
                // A faint lit track behind the active scale.
                var glow = new Pen(VintageArt.Frozen(Color.FromArgb(46, 255, 255, 255)), 50);
                glow.Freeze();
                DrawArc(dc, r + 18, -8, travel + 2, glow);
            }

            foreach (var z in band.Zones)
            {
                double a0 = band.ToNormalized(z.Start) * travel, a1 = band.ToNormalized(z.End) * travel;
                var zone = VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(red, 0.55 * alpha)), 7);
                DrawArc(dc, r - 7, a0, Math.Max(a1, a0 + 0.8), zone);
                if (!string.IsNullOrEmpty(z.Label))
                    VintageArt.DrawCenteredText(dc, VintageArt.Text(z.Label, 13, redBrush, FontWeights.Bold, 1), P(r - 18, (a0 + a1) / 2), -(a0 + a1) / 2);
            }

            DrawArc(dc, r, 0, travel, VintageArt.FrozenPen(brush, 2.2));
            var minor = VintageArt.FrozenPen(brush, 2);
            var major = VintageArt.FrozenPen(brush, 3.2);
            foreach (var v in band.MinorTicks) { double a = band.ToNormalized(v) * travel; dc.DrawLine(minor, P(r, a), P(r + 9, a)); }
            foreach (var t in band.MajorTicks)
            {
                double a = band.ToNormalized(t.Value) * travel;
                dc.DrawLine(major, P(r, a), P(r + 17, a));
                if (t.Label != null) VintageArt.DrawCenteredText(dc, VintageArt.Text(t.Label, 23, brush, FontWeights.Bold, 1), P(r + 31, a), -a);
            }
            // To the right of the start: the band marker in red, then the caption along the arc.
            double start = -4 - 600 / r;
            if (!string.IsNullOrEmpty(band.Title))
            {
                var marker = VintageArt.Text(band.Title, 28, redBrush, FontWeights.Bold, 1);
                double half = marker.Width / 2 / (r + 18) * 180 / Math.PI;
                VintageArt.DrawCenteredText(dc, marker, P(r + 18, start - half), -(start - half));
                start -= 2 * half + 900 / r;
            }
            if (!string.IsNullOrEmpty(band.Caption)) DrawTextOnArc(dc, band.Caption, r + 12, start, 18, brush, FontWeights.Bold);
        }
    }

    /// <summary>Draws text along an arc, reading clockwise from <paramref name="start"/> (degrees). Returns the end angle.</summary>
    private static double DrawTextOnArc(DrawingContext dc, string text, double r, double start, double size, Brush brush, FontWeight weight)
    {
        double theta = start;
        foreach (char ch in text)
        {
            var ft = VintageArt.Text(ch.ToString(), size, brush, weight, 1);
            double w = ch == ' ' ? size * 0.35 : ft.WidthIncludingTrailingWhitespace;
            double mid = theta - w / 2 / r * 180 / Math.PI;
            if (ch != ' ') VintageArt.DrawCenteredText(dc, ft, P(r, mid), -mid);
            theta -= (w + size * 0.06) / r * 180 / Math.PI;
        }
        return theta;
    }

    private static void DrawArc(DrawingContext dc, double r, double a0, double a1, Pen pen)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(P(r, a0), false, false);
            ctx.ArcTo(P(r, a1), new Size(r, r), 0, Math.Abs(a1 - a0) > 180, a1 > a0 ? SweepDirection.Counterclockwise : SweepDirection.Clockwise, true, true);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }

    private void DrawPointer()
    {
        using var dc = _pointer.RenderOpen();
        var red = PointerColor;
        double top = Cy - WindowR;
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(50, 0, 20, 0)), 4), new Point(Cx + 5, top), new Point(Cx + 5, WindowBottom));
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(red, 0.35)), 7), new Point(Cx, top), new Point(Cx, WindowBottom));
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(red), 3), new Point(Cx, top), new Point(Cx, WindowBottom));
    }

    private void DrawGlass()
    {
        using var dc = _glass.RenderOpen();
        var window = WindowGeometry();
        dc.PushClip(window);
        // Shadow of the bezel lip on the dial, and a soft curved reflection near the top
        var lip = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, 0, 0, 0), 0.9), new(Color.FromArgb(110, 0, 0, 0), 0.985), new(Color.FromArgb(170, 0, 0, 0), 1),
        })
        { MappingMode = BrushMappingMode.Absolute, Center = new Point(Cx, Cy), GradientOrigin = new Point(Cx, Cy + 30), RadiusX = WindowR, RadiusY = WindowR };
        lip.Freeze();
        dc.DrawGeometry(lip, null, window);
        var glare = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(70, 255, 255, 255), 0), new(Color.FromArgb(14, 255, 255, 255), 0.4), new(Color.FromArgb(0, 255, 255, 255), 0.6),
        }, new Point(0.2, 0), new Point(0.55, 1));
        glare.Freeze();
        var crescent = Geometry.Combine(new EllipseGeometry(new Point(Cx, Cy), WindowR - 6, WindowR - 6),
            new EllipseGeometry(new Point(Cx - 40, Cy + 70), WindowR - 10, WindowR - 30), GeometryCombineMode.Exclude, null);
        dc.DrawGeometry(glare, null, crescent);
        dc.Pop();
    }

    private void DrawCover()
    {
        using var dc = _cover.RenderOpen();
        var panel = PanelColor;
        // The plate's top edge always closes the window; the rest of the plate only when shown.
        var plateTop = WindowBottom - 2;
        Geometry plate = ShowCoverPlate
            ? Geometry.Combine(new EllipseGeometry(new Point(Cx, Cy), RingR - 4, RingR - 4),
                new RectangleGeometry(new Rect(0, plateTop, DesignW, DesignFull)), GeometryCombineMode.Intersect, null)
            : Geometry.Combine(new EllipseGeometry(new Point(Cx, Cy), RingR - 4, RingR - 4),
                new RectangleGeometry(new Rect(0, plateTop, DesignW, DesignCropped - plateTop)), GeometryCombineMode.Intersect, null);
        var fill = new LinearGradientBrush(new GradientStopCollection
        {
            new(VintageArt.Blend(panel, Colors.White, 0.12), 0), new(panel, 0.2), new(VintageArt.Blend(panel, Colors.Black, 0.4), 1),
        }, new Point(0.3, 0), new Point(0.7, 1));
        fill.Freeze();
        // Cast shadow from the raised plate onto the dial just above it
        dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(120, 0, 0, 0), 90), null, new Rect(Cx - WindowR, plateTop - 14, WindowR * 2, 14));
        dc.DrawGeometry(fill, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0x06, 0x06, 0x06)), 2.5), plate);
        dc.DrawGeometry(VintageArt.GrainBrush(_builtDevicePixelsPerUnit, 0.35), null, plate);
        // Highlight along the plate's top edge
        double half = Math.Sqrt(Math.Max(0, (RingR - 6) * (RingR - 6) - (plateTop - Cy) * (plateTop - Cy)));
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(90, 255, 255, 255)), 2.5), new Point(Cx - half + 6, plateTop + 2), new Point(Cx + half - 6, plateTop + 2));
    }

    private void DrawFocus()
    {
        using var dc = _focus.RenderOpen();
        if (!IsKeyboardFocused) return;
        var pen = new Pen(VintageArt.Frozen(Color.FromArgb(200, 0x6C, 0xF2, 0x4A)), 4) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        dc.DrawEllipse(null, pen, new Point(Cx, Cy), RingR + 8, RingR + 8);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new TuningDialAutomationPeer(this);

    internal void SetPositionFromAutomation(double p) => SetCurrentValue(PositionProperty, Math.Clamp(p, 0, 1));
}

public sealed class TuningDialAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    public TuningDialAutomationPeer(TuningDial owner) : base(owner) { }
    private TuningDial Dial => (TuningDial)Owner;
    protected override string GetClassNameCore() => nameof(TuningDial);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;
    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        if (!string.IsNullOrEmpty(name)) return name;
        var band = Dial.SelectedBand;
        return band == null ? "Tuning dial" : $"Tuning dial, band {band.Title}, {Dial.Frequency.ToString("G4", CultureInfo.InvariantCulture)}";
    }
    public override object GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface)!;

    bool IRangeValueProvider.IsReadOnly => Dial.IsReadOnly;
    double IRangeValueProvider.LargeChange => 0.05;
    double IRangeValueProvider.SmallChange => 0.002;
    double IRangeValueProvider.Maximum => 1;
    double IRangeValueProvider.Minimum => 0;
    double IRangeValueProvider.Value => Dial.Position;
    void IRangeValueProvider.SetValue(double value)
    {
        if (Dial.IsReadOnly) throw new ElementNotEnabledException();
        Dial.SetPositionFromAutomation(value);
    }

    internal void RaiseValueChanged(double oldValue, double newValue) =>
        RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, oldValue, newValue);
}
