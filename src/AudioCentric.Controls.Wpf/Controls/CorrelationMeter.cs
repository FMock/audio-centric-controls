using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using AudioCentric.Controls.Dynamics;
using AudioCentric.Controls.Scales;
using AudioCentric.Controls.Wpf.Rendering;

namespace AudioCentric.Controls.Wpf.Controls;

/// <summary>
/// Stereo phase correlation meter: an edgewise-style backlit instrument with a −1…+1 scale, 0 in the centre. It has a
/// sliding pointer and a bar from the centre that takes the zone colour (green positive, amber and red negative).
/// The host computes the correlation from synchronized channels and supplies the value, validity, window duration and
/// convention. The control only maps it. When the reading is unavailable (<see cref="IsValid"/> false or NaN, e.g. a silent
/// channel), the pointer is hidden and an explicit "NO SIGNAL" flag is shown, so it is never confused with a 0 reading.
/// <para>
/// When <see cref="MonoCheckCommand"/> is set, a MONO push-button appears under the scale. It only executes the command;
/// mono audition happens in the host's audio engine, and <see cref="IsMonoActive"/> lights the button.
/// </para>
/// </summary>
public class CorrelationMeter : FrameworkElement, IRenderTickable
{
    // Design space 1000 × 300. Scale from x = 120 (−1) to 880 (+1) on the baseline y = 138.
    private const double DesignW = 1000, DesignH = 300;
    private const double ScaleLeft = 120, ScaleRight = 880, Baseline = 138;
    private static readonly Rect FaceRect = new(44, 34, 912, 178);
    private static readonly Rect ButtonRect = new(436, 228, 128, 48);

    private readonly VisualCollection _children;
    private readonly ContainerVisual _root = new();
    private readonly DrawingVisual _static = new();
    private readonly DrawingVisual _bar = new();
    private readonly DrawingVisual _pointer = new();
    private readonly DrawingVisual _overlay = new();
    private readonly DrawingVisual _glass = new();
    private readonly MatrixTransform _rootTransform = new();
    private readonly TranslateTransform _pointerOffset = new();
    private readonly RectangleGeometry _barClip = new(new Rect(500, 0, 0, 0));
    private readonly NeedleBallistics _ballistics = new() { Mode = NeedleDynamicsMode.None, NaturalFrequencyHz = 6, DampingRatio = 0.9 };
    private bool _registered;
    private double _builtDevicePixelsPerUnit;
    private bool? _drawnAvailable;

    static CorrelationMeter()
    {
        FocusVisualStyleProperty.OverrideMetadata(typeof(CorrelationMeter), new FrameworkPropertyMetadata(null));
    }

    public CorrelationMeter()
    {
        _children = new VisualCollection(this) { _root };
        _root.Transform = _rootTransform;
        _root.Children.Add(_static);
        _root.Children.Add(_bar);
        _root.Children.Add(_pointer);
        _root.Children.Add(_overlay);
        _root.Children.Add(_glass);
        _pointer.Transform = _pointerOffset;
        _bar.Clip = _barClip;
        _ballistics.Reset(0.5);
        Loaded += (_, _) => { if (!_registered) { _registered = true; RenderScheduler.Register(this); } };
        Unloaded += (_, _) => { if (_registered) { _registered = false; RenderScheduler.Unregister(this); } };
        RebuildAll();
    }

    #region Dependency properties

    /// <summary>Normalized correlation −1…+1 from the provider. NaN means unavailable.</summary>
    public double Correlation { get => (double)GetValue(CorrelationProperty); set => SetValue(CorrelationProperty, value); }
    public static readonly DependencyProperty CorrelationProperty = DependencyProperty.Register(
        nameof(Correlation), typeof(double), typeof(CorrelationMeter), new FrameworkPropertyMetadata(double.NaN, OnReadingChanged));

    /// <summary>False when the provider reports the reading as unavailable (e.g. negligible energy in a channel).</summary>
    public bool IsValid { get => (bool)GetValue(IsValidProperty); set => SetValue(IsValidProperty, value); }
    public static readonly DependencyProperty IsValidProperty = DependencyProperty.Register(
        nameof(IsValid), typeof(bool), typeof(CorrelationMeter), new FrameworkPropertyMetadata(true, OnReadingChanged));

    public ScaleDefinition Scale { get => (ScaleDefinition)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(ScaleDefinition), typeof(CorrelationMeter), new FrameworkPropertyMetadata(ScalePresets.Correlation, OnStaticChanged));

    /// <summary>Integration window of the provider in seconds, shown in the caption. 0 hides it.</summary>
    public double WindowDuration { get => (double)GetValue(WindowDurationProperty); set => SetValue(WindowDurationProperty, value); }
    public static readonly DependencyProperty WindowDurationProperty = DependencyProperty.Register(
        nameof(WindowDuration), typeof(double), typeof(CorrelationMeter), new FrameworkPropertyMetadata(0.3, OnStaticChanged));

    /// <summary>The provider's convention, e.g. "uncentered" (Σ LR / √(Σ L² Σ R²)) or "Pearson" (means removed).</summary>
    public string Convention { get => (string)GetValue(ConventionProperty); set => SetValue(ConventionProperty, value); }
    public static readonly DependencyProperty ConventionProperty = DependencyProperty.Register(
        nameof(Convention), typeof(string), typeof(CorrelationMeter), new FrameworkPropertyMetadata("uncentered", OnStaticChanged));

    /// <summary>Pointer inertia. Default None: the provider's window already smooths the reading.</summary>
    public NeedleDynamicsMode NeedleDynamics { get => (NeedleDynamicsMode)GetValue(NeedleDynamicsProperty); set => SetValue(NeedleDynamicsProperty, value); }
    public static readonly DependencyProperty NeedleDynamicsProperty = DependencyProperty.Register(
        nameof(NeedleDynamics), typeof(NeedleDynamicsMode), typeof(CorrelationMeter),
        new FrameworkPropertyMetadata(NeedleDynamicsMode.None, (d, e) => ((CorrelationMeter)d)._ballistics.Mode = (NeedleDynamicsMode)e.NewValue));

    public bool ShowBar { get => (bool)GetValue(ShowBarProperty); set => SetValue(ShowBarProperty, value); }
    public static readonly DependencyProperty ShowBarProperty = DependencyProperty.Register(
        nameof(ShowBar), typeof(bool), typeof(CorrelationMeter), new FrameworkPropertyMetadata(true, OnReadingChanged));

    public Color FaceColor { get => (Color)GetValue(FaceColorProperty); set => SetValue(FaceColorProperty, value); }
    public static readonly DependencyProperty FaceColorProperty = DependencyProperty.Register(
        nameof(FaceColor), typeof(Color), typeof(CorrelationMeter), new FrameworkPropertyMetadata(Color.FromRgb(0x3C, 0xE4, 0x34), OnStaticChanged));

    public Color NeedleColor { get => (Color)GetValue(NeedleColorProperty); set => SetValue(NeedleColorProperty, value); }
    public static readonly DependencyProperty NeedleColorProperty = DependencyProperty.Register(
        nameof(NeedleColor), typeof(Color), typeof(CorrelationMeter), new FrameworkPropertyMetadata(Color.FromRgb(0xE0, 0x1E, 0x14), (d, _) => ((CorrelationMeter)d).DrawPointer()));

    public bool IsBacklightEnabled { get => (bool)GetValue(IsBacklightEnabledProperty); set => SetValue(IsBacklightEnabledProperty, value); }
    public static readonly DependencyProperty IsBacklightEnabledProperty = DependencyProperty.Register(
        nameof(IsBacklightEnabled), typeof(bool), typeof(CorrelationMeter), new FrameworkPropertyMetadata(true, OnStaticChanged));

    public double BacklightIntensity { get => (double)GetValue(BacklightIntensityProperty); set => SetValue(BacklightIntensityProperty, value); }
    public static readonly DependencyProperty BacklightIntensityProperty = DependencyProperty.Register(
        nameof(BacklightIntensity), typeof(double), typeof(CorrelationMeter), new FrameworkPropertyMetadata(1.0, OnStaticChanged));

    /// <summary>Host command for mono audition. When set, a MONO button is drawn.</summary>
    public ICommand? MonoCheckCommand { get => (ICommand?)GetValue(MonoCheckCommandProperty); set => SetValue(MonoCheckCommandProperty, value); }
    public static readonly DependencyProperty MonoCheckCommandProperty = DependencyProperty.Register(
        nameof(MonoCheckCommand), typeof(ICommand), typeof(CorrelationMeter), new FrameworkPropertyMetadata(null, OnStaticChanged));

    /// <summary>Lights the MONO button; the host sets it when mono audition is actually active.</summary>
    public bool IsMonoActive { get => (bool)GetValue(IsMonoActiveProperty); set => SetValue(IsMonoActiveProperty, value); }
    public static readonly DependencyProperty IsMonoActiveProperty = DependencyProperty.Register(
        nameof(IsMonoActive), typeof(bool), typeof(CorrelationMeter), new FrameworkPropertyMetadata(false, (d, _) => ((CorrelationMeter)d).DrawOverlay()));

    #endregion

    /// <summary>True when a valid reading is shown.</summary>
    public bool IsAvailable => IsValid && !double.IsNaN(Correlation);

    /// <summary>Pointer position in design units (x of the scale), for tests.</summary>
    public double PointerX => ScaleLeft + _ballistics.Position * (ScaleRight - ScaleLeft);

    private static double XOf(double normalized) => ScaleLeft + normalized * (ScaleRight - ScaleLeft);

    private static void OnStaticChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((CorrelationMeter)d).RebuildAll();

    private static void OnReadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var m = (CorrelationMeter)d;
        m.UpdateTarget();
        if (!m._registered) m.Advance(0); // not animating (tests, off-screen rendering): apply at once
        if (m._drawnAvailable != m.IsAvailable) m.DrawOverlay();
    }

    private void UpdateTarget()
    {
        if (IsAvailable) _ballistics.Target = Scale.ToNormalized(Math.Clamp(Correlation, Scale.Minimum, Scale.Maximum));
    }

    void IRenderTickable.Tick(double elapsedSeconds) => Advance(elapsedSeconds);

    private void Advance(double dt)
    {
        bool available = IsAvailable;
        double pos = _ballistics.Update(dt);
        _pointerOffset.X = XOf(pos) - XOf(0.5);
        _pointer.Opacity = available ? 1 : 0;
        double zero = XOf(Scale.ToNormalized(Math.Clamp(0, Scale.Minimum, Scale.Maximum)));
        double x = XOf(pos);
        _barClip.Rect = available && ShowBar ? new Rect(Math.Min(zero, x), 0, Math.Abs(x - zero), DesignH) : Rect.Empty;
    }

    // ---------------------------------------------------------------- layout

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = availableSize.Width, h = availableSize.Height;
        if (double.IsInfinity(w) && double.IsInfinity(h)) return new Size(400, 120);
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

    private void RebuildAll()
    {
        _builtDevicePixelsPerUnit = Math.Max(0.05, _rootTransform.Matrix.M11) * VisualTreeHelper.GetDpi(this).DpiScaleX;
        DrawStatic();
        DrawBar();
        DrawPointer();
        DrawOverlay();
        DrawGlass();
        UpdateTarget();
        Advance(0);
    }

    // ---------------------------------------------------------------- drawing

    private double BacklightLevel => IsBacklightEnabled ? Math.Clamp(BacklightIntensity, 0, 1) : 0;

    private static readonly Color InkColor = Color.FromRgb(0x0E, 0x16, 0x0E);
    private static readonly Color RedInk = Color.FromRgb(0xC4, 0x12, 0x10);
    private static readonly Color AmberInk = Color.FromRgb(0xB0, 0x5A, 0x00);
    private static readonly Color GreenInk = Color.FromRgb(0x0A, 0x52, 0x16);

    private static Color ZoneInk(ZoneKind z) => z switch
    {
        ZoneKind.Danger => RedInk,
        ZoneKind.Warning => AmberInk,
        ZoneKind.Good => GreenInk,
        _ => InkColor,
    };

    private void DrawStatic()
    {
        using var dc = _static.RenderOpen();
        var scale = Scale;
        double ppd = 1.0;

        // Bezel
        var bezel = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x46, 0x46, 0x46), 0), new(Color.FromRgb(0x1C, 0x1C, 0x1C), 0.35), new(Color.FromRgb(0x0C, 0x0C, 0x0C), 1),
        }, new Point(0.3, 0), new Point(0.7, 1));
        bezel.Freeze();
        var outer = new Rect(4, 4, DesignW - 8, DesignH - 8);
        dc.DrawRoundedRectangle(bezel, VintageArt.FrozenPen(VintageArt.Frozen(Colors.Black), 3), outer, 30, 30);
        dc.DrawRoundedRectangle(VintageArt.GrainBrush(_builtDevicePixelsPerUnit, 0.45), null, outer, 30, 30);
        var rim = new LinearGradientBrush(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0.5, 0), new Point(0.5, 0.5));
        rim.Freeze();
        dc.DrawRoundedRectangle(null, new Pen(rim, 3), new Rect(7, 7, DesignW - 14, DesignH - 14), 27, 27);

        // Recessed window
        var recess = FaceRect;
        recess.Inflate(10, 10);
        var recessBrush = new LinearGradientBrush(Color.FromRgb(0x02, 0x02, 0x02), Color.FromRgb(0x2A, 0x2A, 0x2A), 90);
        recessBrush.Freeze();
        dc.DrawRoundedRectangle(recessBrush, null, recess, 20, 20);

        // Backlit face
        double light = BacklightLevel;
        var dim = Color.FromRgb(0x1E, 0x26, 0x1E);
        Color Lit(Color c) => VintageArt.Blend(dim, c, 0.12 + 0.88 * light);
        var baseColor = FaceColor;
        var face = new RadialGradientBrush(new GradientStopCollection
        {
            new(Lit(VintageArt.Blend(baseColor, Colors.White, 0.42)), 0.0),
            new(Lit(VintageArt.Blend(baseColor, Colors.White, 0.12)), 0.4),
            new(Lit(baseColor), 0.68),
            new(Lit(VintageArt.Blend(baseColor, Colors.Black, 0.45)), 1.0),
        })
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(500, 150),
            GradientOrigin = new Point(500, 170),
            RadiusX = 560,
            RadiusY = 190,
        };
        face.Freeze();
        dc.DrawRoundedRectangle(face, null, FaceRect, 12, 12);
        dc.DrawRoundedRectangle(VintageArt.GrainBrush(_builtDevicePixelsPerUnit, 0.18), null, FaceRect, 12, 12);
        var topShade = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(130, 0, 0, 0), 0.0), new(Color.FromArgb(0, 0, 0, 0), 0.3),
            new(Color.FromArgb(0, 0, 0, 0), 0.82), new(Color.FromArgb(90, 0, 0, 0), 1.0),
        }, new Point(0.5, 0), new Point(0.5, 1));
        topShade.Freeze();
        dc.DrawRoundedRectangle(topShade, null, FaceRect, 12, 12);

        // Zone strip just under the baseline
        foreach (var z in scale.Zones)
        {
            double x0 = XOf(scale.ToNormalized(z.Start)), x1 = XOf(scale.ToNormalized(z.End));
            dc.DrawRectangle(VintageArt.Frozen(VintageArt.WithAlpha(ZoneInk(z.Kind), 0.85)), null, new Rect(x0, Baseline + 3, x1 - x0, 8));
        }
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(InkColor), 3), new Point(ScaleLeft - 2, Baseline), new Point(ScaleRight + 2, Baseline));

        foreach (var v in scale.MinorTicks)
        {
            double x = XOf(scale.ToNormalized(v));
            dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(ZoneInk(scale.ZoneAt(v))), 3), new Point(x, Baseline), new Point(x, Baseline - 20));
        }
        foreach (var t in scale.MajorTicks)
        {
            double x = XOf(scale.ToNormalized(t.Value));
            var ink = ZoneInk(scale.ZoneAt(t.Value));
            dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(ink), 5), new Point(x, Baseline), new Point(x, Baseline - 40));
            if (t.Label != null)
                VintageArt.DrawCenteredText(dc, VintageArt.Text(t.Label, 40, VintageArt.Frozen(ink), FontWeights.Bold, ppd), new Point(x, Baseline - 66));
        }

        if (scale.LowEndGlyph != null)
            VintageArt.DrawCenteredText(dc, VintageArt.Text(scale.LowEndGlyph, 54, VintageArt.Frozen(ZoneInk(scale.ZoneAt(scale.Minimum))), FontWeights.Bold, ppd), new Point(78, Baseline - 22));
        if (scale.HighEndGlyph != null)
            VintageArt.DrawCenteredText(dc, VintageArt.Text(scale.HighEndGlyph, 54, VintageArt.Frozen(ZoneInk(scale.ZoneAt(scale.Maximum))), FontWeights.Bold, ppd), new Point(922, Baseline - 22));

        // Legend and caption along the bottom of the face
        var inkBrush = VintageArt.Frozen(VintageArt.WithAlpha(InkColor, 0.9));
        var title = VintageArt.Text(scale.Title, 26, inkBrush, FontWeights.Bold, ppd);
        dc.DrawText(title, new Point(ScaleLeft - 4, 175));
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Convention)) parts.Add(Convention);
        if (WindowDuration > 0) parts.Add((WindowDuration * 1000).ToString("0", CultureInfo.InvariantCulture) + " ms");
        if (parts.Count > 0)
        {
            var cap = VintageArt.Text(string.Join(" · ", parts), 23, inkBrush, FontWeights.SemiBold, ppd);
            dc.DrawText(cap, new Point(ScaleRight + 4 - cap.Width, 177));
        }

        // Bezel legends
        var legend = VintageArt.Frozen(Color.FromRgb(0x9A, 0xA8, 0x9E));
        dc.DrawText(VintageArt.Text("OUT OF PHASE", 21, legend, FontWeights.SemiBold, ppd), new Point(ScaleLeft - 4, 240));
        var inPhase = VintageArt.Text("IN PHASE", 21, legend, FontWeights.SemiBold, ppd);
        dc.DrawText(inPhase, new Point(ScaleRight + 4 - inPhase.Width, 240));

        foreach (var p in new[] { new Point(26, 26), new Point(DesignW - 26, 26), new Point(26, DesignH - 26), new Point(DesignW - 26, DesignH - 26) })
            VintageArt.DrawScrew(dc, p, 10, p.X * 0.29 + p.Y * 0.13);
    }

    private void DrawBar()
    {
        using var dc = _bar.RenderOpen();
        var scale = Scale;
        // One strip coloured by zone across the whole scale; the clip reveals only centre → reading.
        var stops = new GradientStopCollection();
        foreach (double v in new[] { -1.0, -0.75, -0.5, -0.3, -0.1, 0, 0.1, 0.5, 1 })
        {
            double u = scale.ToNormalized(v);
            var z = scale.ZoneAt(v);
            var c = z switch
            {
                ZoneKind.Danger => Color.FromRgb(0xE0, 0x24, 0x18),
                ZoneKind.Warning => Color.FromRgb(0xF0, 0x8C, 0x10),
                ZoneKind.Good => Color.FromRgb(0x18, 0x8C, 0x24),
                _ => Color.FromRgb(0x50, 0x60, 0x50),
            };
            stops.Add(new GradientStop(c, u));
        }
        var brush = new LinearGradientBrush(stops, new Point(ScaleLeft, 0), new Point(ScaleRight, 0)) { MappingMode = BrushMappingMode.Absolute };
        brush.Freeze();
        var r = new Rect(ScaleLeft, Baseline + 15, ScaleRight - ScaleLeft, 11);
        dc.DrawRectangle(VintageArt.Frozen(Color.FromArgb(70, 0, 0, 0)), null, new Rect(r.X, r.Y + 2, r.Width, r.Height));
        dc.DrawRectangle(brush, null, r);
        var sheen = new LinearGradientBrush(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90);
        sheen.Freeze();
        dc.DrawRectangle(sheen, null, new Rect(r.X, r.Y, r.Width, r.Height / 2));
    }

    private void DrawPointer()
    {
        using var dc = _pointer.RenderOpen();
        double x = XOf(0.5);
        var color = NeedleColor;
        // Drop shadow on the face, then a slim tapered blade.
        dc.DrawRectangle(VintageArt.Frozen(Color.FromArgb(60, 0, 20, 0)), null, new Rect(x - 2 + 5, 48 + 6, 5, 118));
        var body = new LinearGradientBrush(new GradientStopCollection
        {
            new(VintageArt.Blend(color, Colors.Black, 0.35), 0), new(VintageArt.Blend(color, Colors.White, 0.25), 0.45),
            new(color, 0.6), new(VintageArt.Blend(color, Colors.Black, 0.45), 1),
        }, new Point(0, 0.5), new Point(1, 0.5));
        body.Freeze();
        var blade = new StreamGeometry();
        using (var ctx = blade.Open())
        {
            ctx.BeginFigure(new Point(x - 1.3, 46), true, true);
            ctx.LineTo(new Point(x + 1.3, 46), true, true);
            ctx.LineTo(new Point(x + 3.0, 170), true, true);
            ctx.LineTo(new Point(x - 3.0, 170), true, true);
        }
        blade.Freeze();
        dc.DrawGeometry(body, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(VintageArt.Blend(color, Colors.Black, 0.6), 0.6)), 0.8), blade);
    }

    private void DrawOverlay()
    {
        _drawnAvailable = IsAvailable;
        using var dc = _overlay.RenderOpen();
        if (!IsAvailable)
        {
            var flag = new Rect(390, 96, 220, 50);
            dc.DrawRoundedRectangle(VintageArt.Frozen(Color.FromArgb(235, 0x14, 0x14, 0x12)), VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0xF0, 0x8C, 0x10)), 2.5), flag, 8, 8);
            VintageArt.DrawCenteredText(dc, VintageArt.Text("NO SIGNAL", 30, VintageArt.Frozen(Color.FromRgb(0xFF, 0xB0, 0x30)), FontWeights.Bold, 1), new Point(500, flag.Top + flag.Height / 2));
        }

        if (MonoCheckCommand != null)
        {
            var r = ButtonRect;
            var cap = new LinearGradientBrush(Color.FromRgb(0x3A, 0x3C, 0x3A), Color.FromRgb(0x16, 0x17, 0x16), 90);
            cap.Freeze();
            dc.DrawRoundedRectangle(VintageArt.Frozen(Color.FromRgb(0x05, 0x05, 0x05)), null, new Rect(r.X - 3, r.Y - 3, r.Width + 6, r.Height + 6), 9, 9);
            dc.DrawRoundedRectangle(cap, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(70, 255, 255, 255)), 1.2), r, 7, 7);
            VintageArt.DrawLamp(dc, new Point(r.Left + 24, r.Top + r.Height / 2), 9, Color.FromRgb(0xFF, 0xC0, 0x3A), IsMonoActive);
            var label = VintageArt.Text("MONO", 24, VintageArt.Frozen(IsMonoActive ? Color.FromRgb(0xFF, 0xD8, 0x8A) : Color.FromRgb(0xC8, 0xCE, 0xC8)), FontWeights.Bold, 1);
            dc.DrawText(label, new Point(r.Left + 42, r.Top + (r.Height - label.Height) / 2));
        }
    }

    private void DrawGlass()
    {
        using var dc = _glass.RenderOpen();
        var glare = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(110, 255, 255, 255), 0), new(Color.FromArgb(25, 255, 255, 255), 0.35), new(Color.FromArgb(0, 255, 255, 255), 0.55),
        }, new Point(0.5, 0), new Point(0.5, 1));
        glare.Freeze();
        dc.DrawRoundedRectangle(glare, null, new Rect(FaceRect.X + 10, FaceRect.Y + 4, FaceRect.Width - 20, 40), 10, 10);
    }

    // ---------------------------------------------------------------- input

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) =>
        new Rect(RenderSize).Contains(hitTestParameters.HitPoint) ? new PointHitTestResult(this, hitTestParameters.HitPoint) : null;

    private bool IsOverButton(Point p)
    {
        if (MonoCheckCommand == null || !_rootTransform.Matrix.HasInverse) return false;
        var m = _rootTransform.Matrix;
        m.Invert();
        return ButtonRect.Contains(m.Transform(p));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = IsOverButton(e.GetPosition(this)) ? Cursors.Hand : null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsOverButton(e.GetPosition(this))) return;
        var cmd = MonoCheckCommand;
        if (cmd != null && cmd.CanExecute(null)) cmd.Execute(null);
        e.Handled = true;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new CorrelationMeterAutomationPeer(this);
}

public sealed class CorrelationMeterAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    public CorrelationMeterAutomationPeer(CorrelationMeter owner) : base(owner) { }

    private CorrelationMeter Meter => (CorrelationMeter)Owner;

    protected override string GetClassNameCore() => nameof(CorrelationMeter);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;

    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        if (!string.IsNullOrEmpty(name)) return name;
        return Meter.IsAvailable ? "Phase correlation meter" : "Phase correlation meter, no signal";
    }

    public override object GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface)!;

    bool IRangeValueProvider.IsReadOnly => true;
    double IRangeValueProvider.LargeChange => 0;
    double IRangeValueProvider.SmallChange => 0;
    double IRangeValueProvider.Maximum => Meter.Scale.Maximum;
    double IRangeValueProvider.Minimum => Meter.Scale.Minimum;
    double IRangeValueProvider.Value => Meter.IsAvailable ? Math.Clamp(Meter.Correlation, Meter.Scale.Minimum, Meter.Scale.Maximum) : 0;
    void IRangeValueProvider.SetValue(double value) => throw new ElementNotEnabledException();
}
