using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using AudioCentric.Controls.Dynamics;
using AudioCentric.Controls.Measurements;
using AudioCentric.Controls.Scales;
using AudioCentric.Controls.Wpf.Rendering;

namespace AudioCentric.Controls.Wpf.Controls;

/// <summary>Scale preset used when <see cref="AnalogMeter.Scale"/> is not set explicitly.</summary>
public enum AnalogMeterType
{
    Vu,
    Peak,
    PeakPcm,
    TruePeak,
    Rms,
}

/// <summary>
/// Vintage backlit moving-coil meter (VU, peak or RMS) with an optional latching overload lamp.
/// The host binds a ready-made reading to <see cref="Value"/> in the scale's units, plus <see cref="IsOverloaded"/>.
/// The control owns the dial, scale mapping, display-only needle inertia and the lamp's latch and reset.
/// <para>
/// Rendering is vector-first. All artwork is drawn once in a 1000×1000 design space and scaled uniformly,
/// so it stays sharp at any size or DPI. Only the needle's rotation changes per frame.
/// </para>
/// </summary>
public class AnalogMeter : FrameworkElement, IRenderTickable
{
    // ---- Design-space geometry (1000 × 1000) ----
    private const double DesignSize = 1000;
    private static readonly Point Center = new(500, 500);
    private const double BezelRadius = 482;
    private const double FaceRadius = 400;
    // Scale geometry follows the reference artwork: the pivot sits just under the pod hump, and the arc is
    // flatter and smaller than the window, so the outermost labels stay at ~85% of the window radius.
    // That keeps them clear of the bezel shadow and glass reflection.
    private static readonly Point Pivot = new(500, 665);
    private const double ArcRadius = 360;
    private static readonly ArcGeometry Arc = new(-54, 54);
    private const double MajorTickLength = 24;
    private const double MinorTickLength = 13;

    /// <summary>The value-to-angle transform shared by the needle and scale (degrees, 0 = vertical).</summary>
    public static ArcGeometry NeedleArc => Arc;
    private static readonly Point LampCenter = new(368, 748);
    private const double LampRadius = 17;

    private readonly VisualCollection _children;
    private readonly ContainerVisual _root = new();
    private readonly DrawingVisual _static = new();
    private readonly ContainerVisual _faceClip = new();
    private readonly DrawingVisual _peakMarker = new();
    private readonly DrawingVisual _needleShadow = new();
    private readonly DrawingVisual _needle = new();
    private readonly DrawingVisual _front = new();
    private readonly DrawingVisual _lamp = new();

    private readonly RotateTransform _needleRotation = new(0, Pivot.X, Pivot.Y);
    private readonly RotateTransform _shadowRotation = new(0, Pivot.X, Pivot.Y);
    private readonly RotateTransform _peakRotation = new(0, Pivot.X, Pivot.Y);
    private readonly MatrixTransform _rootTransform = new();

    private readonly NeedleBallistics _ballistics = new();
    private readonly OverloadLatch _latch = new();
    private readonly DecayFollower _release = new();
    private readonly PeakHoldTracker _hold = new();
    private bool _registered;
    private double _builtDevicePixelsPerUnit = -1;
    private bool _staticDirty = true;

    public static readonly RoutedCommand ResetOverloadCommand = new(nameof(ResetOverloadCommand), typeof(AnalogMeter));

    static AnalogMeter()
    {
        FocusableProperty.OverrideMetadata(typeof(AnalogMeter), new FrameworkPropertyMetadata(false));
        CommandManager.RegisterClassCommandBinding(typeof(AnalogMeter),
            new CommandBinding(ResetOverloadCommand, (s, _) => ((AnalogMeter)s).ResetOverload()));
    }

    public AnalogMeter()
    {
        _children = new VisualCollection(this);
        _root.Transform = _rootTransform;

        _needleShadow.Transform = new TransformGroup { Children = { _shadowRotation, new TranslateTransform(8, 10) } };
        _needle.Transform = _needleRotation;
        _peakMarker.Transform = _peakRotation;
        DrawPeakMarker();
        _faceClip.Clip = new EllipseGeometry(Center, FaceRadius, FaceRadius);
        _faceClip.Children.Add(_peakMarker);
        _faceClip.Children.Add(_needleShadow);
        _faceClip.Children.Add(_needle);

        _root.Children.Add(_static);
        _root.Children.Add(_faceClip);
        _root.Children.Add(_front);
        _root.Children.Add(_lamp);
        _children.Add(_root);

        _latch.Mode = OverloadLatchMode;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DrawNeedle();
        UpdateNeedleImmediately();
    }

    #region Dependency properties

    /// <summary>The reading in the scale's units (VU or dBFS). -∞ is silence.</summary>
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(AnalogMeter),
        new FrameworkPropertyMetadata(double.NegativeInfinity, OnValueChanged));

    /// <summary>False when the host has no valid reading; the needle returns to rest.</summary>
    public bool IsValueValid { get => (bool)GetValue(IsValueValidProperty); set => SetValue(IsValueValidProperty, value); }
    public static readonly DependencyProperty IsValueValidProperty = DependencyProperty.Register(
        nameof(IsValueValid), typeof(bool), typeof(AnalogMeter), new FrameworkPropertyMetadata(true, OnValueChanged));

    public AnalogMeterType MeterType { get => (AnalogMeterType)GetValue(MeterTypeProperty); set => SetValue(MeterTypeProperty, value); }
    public static readonly DependencyProperty MeterTypeProperty = DependencyProperty.Register(
        nameof(MeterType), typeof(AnalogMeterType), typeof(AnalogMeter),
        new FrameworkPropertyMetadata(AnalogMeterType.Vu, OnStaticChanged));

    /// <summary>Explicit scale; overrides <see cref="MeterType"/> when set.</summary>
    public ScaleDefinition? Scale { get => (ScaleDefinition?)GetValue(ScaleProperty); set => SetValue(ScaleProperty, value); }
    public static readonly DependencyProperty ScaleProperty = DependencyProperty.Register(
        nameof(Scale), typeof(ScaleDefinition), typeof(AnalogMeter), new FrameworkPropertyMetadata(null, OnStaticChanged));

    public NeedleDynamicsMode NeedleDynamics { get => (NeedleDynamicsMode)GetValue(NeedleDynamicsProperty); set => SetValue(NeedleDynamicsProperty, value); }
    public static readonly DependencyProperty NeedleDynamicsProperty = DependencyProperty.Register(
        nameof(NeedleDynamics), typeof(NeedleDynamicsMode), typeof(AnalogMeter),
        new FrameworkPropertyMetadata(NeedleDynamicsMode.Mechanical, (d, e) => ((AnalogMeter)d)._ballistics.Mode = (NeedleDynamicsMode)e.NewValue));

    public Color FaceColor { get => (Color)GetValue(FaceColorProperty); set => SetValue(FaceColorProperty, value); }
    public static readonly DependencyProperty FaceColorProperty = DependencyProperty.Register(
        nameof(FaceColor), typeof(Color), typeof(AnalogMeter),
        new FrameworkPropertyMetadata(Color.FromRgb(0x3C, 0xE4, 0x34), OnStaticChanged));

    public Color NeedleColor { get => (Color)GetValue(NeedleColorProperty); set => SetValue(NeedleColorProperty, value); }
    public static readonly DependencyProperty NeedleColorProperty = DependencyProperty.Register(
        nameof(NeedleColor), typeof(Color), typeof(AnalogMeter),
        new FrameworkPropertyMetadata(Color.FromRgb(0xE0, 0x1E, 0x14), (d, _) => ((AnalogMeter)d).DrawNeedle()));

    public bool IsBacklightEnabled { get => (bool)GetValue(IsBacklightEnabledProperty); set => SetValue(IsBacklightEnabledProperty, value); }
    public static readonly DependencyProperty IsBacklightEnabledProperty = DependencyProperty.Register(
        nameof(IsBacklightEnabled), typeof(bool), typeof(AnalogMeter), new FrameworkPropertyMetadata(true, OnStaticChanged));

    /// <summary>Backlight brightness, 0..1.</summary>
    public double BacklightIntensity { get => (double)GetValue(BacklightIntensityProperty); set => SetValue(BacklightIntensityProperty, value); }
    public static readonly DependencyProperty BacklightIntensityProperty = DependencyProperty.Register(
        nameof(BacklightIntensity), typeof(double), typeof(AnalogMeter), new FrameworkPropertyMetadata(1.0, OnStaticChanged));

    /// <summary>Optional held peak in the scale's units, drawn as a thin amber marker. NaN hides it.</summary>
    public double PeakHoldValue { get => (double)GetValue(PeakHoldValueProperty); set => SetValue(PeakHoldValueProperty, value); }
    public static readonly DependencyProperty PeakHoldValueProperty = DependencyProperty.Register(
        nameof(PeakHoldValue), typeof(double), typeof(AnalogMeter), new FrameworkPropertyMetadata(double.NaN, (d, _) => ((AnalogMeter)d).UpdatePeakMarker()));

    /// <summary>
    /// Display-only release rate in dB/s applied before the needle (immediate attack). NaN = automatic:
    /// 20 dB/s for peak scales, none for VU and RMS (their dynamics come calibrated from the provider).
    /// </summary>
    public double DisplayReleaseDbPerSecond { get => (double)GetValue(DisplayReleaseDbPerSecondProperty); set => SetValue(DisplayReleaseDbPerSecondProperty, value); }
    public static readonly DependencyProperty DisplayReleaseDbPerSecondProperty = DependencyProperty.Register(
        nameof(DisplayReleaseDbPerSecond), typeof(double), typeof(AnalogMeter), new FrameworkPropertyMetadata(double.NaN));

    /// <summary>When true and <see cref="PeakHoldValue"/> is NaN, the meter tracks its own held peak marker.</summary>
    public bool IsPeakHoldEnabled { get => (bool)GetValue(IsPeakHoldEnabledProperty); set => SetValue(IsPeakHoldEnabledProperty, value); }
    public static readonly DependencyProperty IsPeakHoldEnabledProperty = DependencyProperty.Register(
        nameof(IsPeakHoldEnabled), typeof(bool), typeof(AnalogMeter), new FrameworkPropertyMetadata(false, (d, _) => ((AnalogMeter)d).UpdatePeakMarker()));

    public double PeakHoldSeconds { get => (double)GetValue(PeakHoldSecondsProperty); set => SetValue(PeakHoldSecondsProperty, value); }
    public static readonly DependencyProperty PeakHoldSecondsProperty = DependencyProperty.Register(
        nameof(PeakHoldSeconds), typeof(double), typeof(AnalogMeter),
        new FrameworkPropertyMetadata(2.0, (d, e) => ((AnalogMeter)d)._hold.HoldSeconds = (double)e.NewValue));

    /// <summary>Host-reported overload condition (e.g. sample peak at or above full scale).</summary>
    public bool IsOverloaded { get => (bool)GetValue(IsOverloadedProperty); set => SetValue(IsOverloadedProperty, value); }
    public static readonly DependencyProperty IsOverloadedProperty = DependencyProperty.Register(
        nameof(IsOverloaded), typeof(bool), typeof(AnalogMeter), new FrameworkPropertyMetadata(false, OnOverloadInputChanged));

    public OverloadLatchMode OverloadLatchMode { get => (OverloadLatchMode)GetValue(OverloadLatchModeProperty); set => SetValue(OverloadLatchModeProperty, value); }
    public static readonly DependencyProperty OverloadLatchModeProperty = DependencyProperty.Register(
        nameof(OverloadLatchMode), typeof(OverloadLatchMode), typeof(AnalogMeter),
        new FrameworkPropertyMetadata(OverloadLatchMode.Latched, (d, e) => ((AnalogMeter)d)._latch.Mode = (OverloadLatchMode)e.NewValue));

    public double OverloadHoldSeconds { get => (double)GetValue(OverloadHoldSecondsProperty); set => SetValue(OverloadHoldSecondsProperty, value); }
    public static readonly DependencyProperty OverloadHoldSecondsProperty = DependencyProperty.Register(
        nameof(OverloadHoldSeconds), typeof(double), typeof(AnalogMeter),
        new FrameworkPropertyMetadata(2.0, (d, e) => ((AnalogMeter)d)._latch.HoldSeconds = (double)e.NewValue));

    public bool ShowOverloadLamp { get => (bool)GetValue(ShowOverloadLampProperty); set => SetValue(ShowOverloadLampProperty, value); }
    public static readonly DependencyProperty ShowOverloadLampProperty = DependencyProperty.Register(
        nameof(ShowOverloadLamp), typeof(bool), typeof(AnalogMeter), new FrameworkPropertyMetadata(true, (d, _) => ((AnalogMeter)d).DrawLamp()));

    public string OverloadLabel { get => (string)GetValue(OverloadLabelProperty); set => SetValue(OverloadLabelProperty, value); }
    public static readonly DependencyProperty OverloadLabelProperty = DependencyProperty.Register(
        nameof(OverloadLabel), typeof(string), typeof(AnalogMeter), new FrameworkPropertyMetadata("OVL", (d, _) => ((AnalogMeter)d).DrawLamp()));

    /// <summary>Read-only: whether the overload lamp is currently lit (after latch/hold logic).</summary>
    public bool IsOverloadLit => (bool)GetValue(IsOverloadLitProperty);
    private static readonly DependencyPropertyKey IsOverloadLitPropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(IsOverloadLit), typeof(bool), typeof(AnalogMeter), new FrameworkPropertyMetadata(false, (d, _) => ((AnalogMeter)d).DrawLamp()));
    public static readonly DependencyProperty IsOverloadLitProperty = IsOverloadLitPropertyKey.DependencyProperty;

    public static readonly RoutedEvent OverloadResetEvent = EventManager.RegisterRoutedEvent(
        nameof(OverloadReset), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AnalogMeter));

    /// <summary>Raised when the lamp is reset (lamp clicked, <see cref="ResetOverloadCommand"/> or <see cref="ResetOverload"/>).</summary>
    public event RoutedEventHandler OverloadReset
    {
        add => AddHandler(OverloadResetEvent, value);
        remove => RemoveHandler(OverloadResetEvent, value);
    }

    #endregion

    /// <summary>The scale in effect: <see cref="Scale"/> if set, otherwise the preset for <see cref="MeterType"/>.</summary>
    public ScaleDefinition EffectiveScale => Scale ?? MeterType switch
    {
        AnalogMeterType.Peak => ScalePresets.PeakDbfs,
        AnalogMeterType.PeakPcm => ScalePresets.PeakDbfsPcm,
        AnalogMeterType.TruePeak => ScalePresets.TruePeakDbtp,
        AnalogMeterType.Rms => ScalePresets.RmsDbfs,
        _ => ScalePresets.Vu,
    };

    /// <summary>Current needle angle in degrees (0 = vertical), for tests and diagnostics.</summary>
    public double NeedleAngle => _needleRotation.Angle;

    /// <summary>Turns the overload lamp off (the engineer pressed reset).</summary>
    public void ResetOverload()
    {
        _latch.Reset();
        SetValue(IsOverloadLitPropertyKey, false);
        RaiseEvent(new RoutedEventArgs(OverloadResetEvent, this));
    }

    // ---------------------------------------------------------------- lifecycle

    private void Attach()
    {
        if (_registered) return;
        _registered = true;
        RenderScheduler.Register(this);
    }

    private void Detach()
    {
        if (!_registered) return;
        _registered = false;
        RenderScheduler.Unregister(this);
    }

    void IRenderTickable.Tick(double elapsedSeconds)
    {
        var scale = EffectiveScale;
        double reading = CurrentReading();
        double release = double.IsNaN(DisplayReleaseDbPerSecond) ? AutoRelease(scale) : DisplayReleaseDbPerSecond;
        double shown = reading;
        if (release > 0)
        {
            _release.ReleaseDbPerSecond = release;
            shown = _release.Update(reading, elapsedSeconds);
        }
        else
        {
            _release.Reset(reading);
        }

        _ballistics.Target = double.IsNaN(shown) ? 0 : scale.ToNormalized(shown);
        double pos = _ballistics.Update(elapsedSeconds);
        double angle = Arc.StartAngle + pos * Arc.Sweep;
        if (Math.Abs(angle - _needleRotation.Angle) > 1e-4)
        {
            _needleRotation.Angle = angle;
            _shadowRotation.Angle = angle;
        }

        bool lit = _latch.Update(IsOverloaded, elapsedSeconds);
        if (lit != IsOverloadLit) SetValue(IsOverloadLitPropertyKey, lit);

        if (IsPeakHoldEnabled && double.IsNaN(PeakHoldValue))
        {
            _hold.Update(reading, elapsedSeconds);
            PlacePeakMarker(_hold.Value);
        }
    }

    private static double AutoRelease(ScaleDefinition scale) =>
        scale.Unit is MeasurementUnit.DbFs or MeasurementUnit.DbTp && scale.Title.StartsWith("PEAK", StringComparison.OrdinalIgnoreCase) ? 20.0 : 0.0;

    private double CurrentReading() => !IsValueValid || double.IsNaN(Value) ? double.NegativeInfinity : Value;

    private double TargetPosition() => EffectiveScale.ToNormalized(CurrentReading());

    private void UpdateNeedleImmediately()
    {
        _ballistics.Reset(TargetPosition());
        _release.Reset(CurrentReading());
        double angle = Arc.AngleFor(_ballistics.Position);
        _needleRotation.Angle = angle;
        _shadowRotation.Angle = angle;
    }

    private static void OnValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var m = (AnalogMeter)d;
        if (!m._registered) m.UpdateNeedleImmediately();
    }

    private static void OnOverloadInputChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        // Latch immediately: a host may raise the flag for a single frame only.
        var m = (AnalogMeter)d;
        if ((bool)e.NewValue || !m._registered)
        {
            bool lit = m._latch.Update((bool)e.NewValue, 0);
            if (lit != m.IsOverloadLit) m.SetValue(IsOverloadLitPropertyKey, lit);
        }
    }

    private static void OnStaticChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var m = (AnalogMeter)d;
        m._staticDirty = true;
        m.InvalidateVisual();
        m.UpdatePeakMarker();
        if (!m._registered) m.UpdateNeedleImmediately();
        if (e.Property == IsBacklightEnabledProperty || e.Property == BacklightIntensityProperty) m.DrawLamp();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        _staticDirty = true;
        InvalidateVisual();
    }

    // ---------------------------------------------------------------- layout

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = availableSize.Width, h = availableSize.Height;
        double s = double.IsInfinity(w) && double.IsInfinity(h) ? 240 : Math.Min(w, h);
        if (double.IsInfinity(s)) s = double.IsInfinity(w) ? h : w;
        return new Size(s, s);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double s = Math.Min(finalSize.Width, finalSize.Height);
        double k = s / DesignSize;
        var m = new Matrix(k, 0, 0, k, (finalSize.Width - s) / 2, (finalSize.Height - s) / 2);
        _rootTransform.Matrix = m;

        double dppu = k * VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (Math.Abs(dppu - _builtDevicePixelsPerUnit) > 1e-3) _staticDirty = true;
        if (_staticDirty) RebuildStatic();
        return finalSize;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // Static layers are rebuilt lazily here, so property changes are coalesced.
        if (_staticDirty) RebuildStatic();
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        var p = hitTestParameters.HitPoint;
        return new Rect(RenderSize).Contains(p) ? new PointHitTestResult(this, p) : null;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!ShowOverloadLamp) return;
        var inv = _rootTransform.Matrix;
        if (!inv.HasInverse) return;
        inv.Invert();
        var p = inv.Transform(e.GetPosition(this));
        if ((p - LampCenter).Length <= LampRadius * 2.2)
        {
            ResetOverload();
            e.Handled = true;
        }
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new AnalogMeterAutomationPeer(this);

    // ---------------------------------------------------------------- drawing

    private static Point Polar(double radius, double angle)
    {
        var (x, y) = ArcGeometry.PointAt(Pivot.X, Pivot.Y, radius, angle);
        return new Point(x, y);
    }

    private static StreamGeometry ArcBand(double a0, double a1, double rInner0, double rInner1, double rOuter)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            const int steps = 24;
            ctx.BeginFigure(Polar(rOuter, a0), true, true);
            for (int i = 1; i <= steps; i++) ctx.LineTo(Polar(rOuter, a0 + (a1 - a0) * i / steps), true, true);
            for (int i = steps; i >= 0; i--)
            {
                double t = (double)i / steps;
                ctx.LineTo(Polar(rInner0 + (rInner1 - rInner0) * t, a0 + (a1 - a0) * t), true, true);
            }
        }
        g.Freeze();
        return g;
    }

    private static StreamGeometry ArcLine(double radius, double a0, double a1)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(Polar(radius, a0), false, false);
            ctx.ArcTo(Polar(radius, a1), new Size(radius, radius), 0, Math.Abs(a1 - a0) > 180,
                a1 > a0 ? SweepDirection.Clockwise : SweepDirection.Counterclockwise, true, true);
        }
        g.Freeze();
        return g;
    }

    private double BacklightLevel => IsBacklightEnabled ? Math.Clamp(BacklightIntensity, 0, 1) : 0;

    private void RebuildStatic()
    {
        _staticDirty = false;
        var dpi = VisualTreeHelper.GetDpi(this);
        double k = _rootTransform.Matrix.M11 > 0 ? _rootTransform.Matrix.M11 : 0.3;
        _builtDevicePixelsPerUnit = k * dpi.DpiScaleX;
        double ppd = dpi.PixelsPerDip;

        using (var dc = _static.RenderOpen())
        {
            DrawBezel(dc);
            DrawFace(dc);
            DrawScale(dc, EffectiveScale, ppd);
        }
        using (var dc = _front.RenderOpen())
        {
            DrawPod(dc);
            DrawGlass(dc);
            DrawBezelScrews(dc);
        }
        DrawLamp();
        UpdatePeakMarker();
    }

    private void DrawBezel(DrawingContext dc)
    {
        // Soft shadow cast on the panel
        var shadow = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(170, 0, 0, 0), 0.92),
            new(Color.FromArgb(0, 0, 0, 0), 1.0),
        });
        shadow.Freeze();
        dc.DrawEllipse(shadow, null, new Point(Center.X + 6, Center.Y + 14), 512, 512);

        // Outer ring: black textured bakelite, lit from above
        var ring = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x46, 0x46, 0x46), 0.0),
            new(Color.FromRgb(0x1C, 0x1C, 0x1C), 0.35),
            new(Color.FromRgb(0x0C, 0x0C, 0x0C), 0.75),
            new(Color.FromRgb(0x1A, 0x1A, 0x1A), 1.0),
        }, new Point(0.3, 0), new Point(0.7, 1));
        ring.Freeze();
        dc.DrawEllipse(ring, VintageArt.FrozenPen(VintageArt.Frozen(Colors.Black), 3), Center, BezelRadius, BezelRadius);
        dc.DrawEllipse(VintageArt.GrainBrush(_builtDevicePixelsPerUnit, 0.5), null, Center, BezelRadius, BezelRadius);

        // Rim highlight along the top edge
        var rim = new LinearGradientBrush(Color.FromArgb(110, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0.5, 0), new Point(0.5, 0.6));
        rim.Freeze();
        dc.DrawEllipse(null, new Pen(rim, 4), Center, BezelRadius - 4, BezelRadius - 4);

        // Stepped inner bevel (lit from below, i.e. it slopes inward)
        var step = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x08, 0x08, 0x08), 0.0),
            new(Color.FromRgb(0x16, 0x16, 0x16), 0.55),
            new(Color.FromRgb(0x30, 0x30, 0x30), 1.0),
        }, new Point(0.5, 0), new Point(0.5, 1));
        step.Freeze();
        dc.DrawEllipse(step, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0x05, 0x05, 0x05)), 3), Center, 446, 446);
        dc.DrawEllipse(VintageArt.GrainBrush(_builtDevicePixelsPerUnit, 0.35), null, Center, 446, 446);

        var stepHi = new LinearGradientBrush(Color.FromArgb(0, 255, 255, 255), Color.FromArgb(70, 255, 255, 255), new Point(0.5, 0.4), new Point(0.5, 1));
        stepHi.Freeze();
        dc.DrawEllipse(null, new Pen(stepHi, 2.5), Center, 444, 444);

        // Inner lip around the window
        dc.DrawEllipse(VintageArt.Frozen(Color.FromRgb(0x04, 0x04, 0x04)), null, Center, FaceRadius + 12, FaceRadius + 12);
    }

    private void DrawFace(DrawingContext dc)
    {
        double light = BacklightLevel;
        var dim = Color.FromRgb(0x1E, 0x26, 0x1E);
        Color baseColor = FaceColor;
        Color Lit(Color c) => VintageArt.Blend(dim, c, 0.12 + 0.88 * light);

        var face = new RadialGradientBrush(new GradientStopCollection
        {
            new(Lit(VintageArt.Blend(baseColor, Colors.White, 0.42)), 0.0),
            new(Lit(VintageArt.Blend(baseColor, Colors.White, 0.12)), 0.38),
            new(Lit(baseColor), 0.62),
            new(Lit(VintageArt.Blend(baseColor, Colors.Black, 0.35)), 0.86),
            new(Lit(VintageArt.Blend(baseColor, Colors.Black, 0.7)), 1.0),
        })
        {
            MappingMode = BrushMappingMode.Absolute,
            Center = new Point(500, 560),
            GradientOrigin = new Point(500, 640),
            RadiusX = 470,
            RadiusY = 470,
        };
        face.Freeze();
        dc.DrawEllipse(face, null, Center, FaceRadius, FaceRadius);

        // Paper grain of the printed dial
        dc.DrawEllipse(VintageArt.GrainBrush(_builtDevicePixelsPerUnit, 0.18), null, Center, FaceRadius, FaceRadius);

        // Shadow of the bezel falling onto the recessed dial
        var inner = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, 0, 0, 0), 0.84),
            new(Color.FromArgb(90, 0, 0, 0), 0.95),
            new(Color.FromArgb(190, 0, 0, 0), 1.0),
        })
        { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.58) };
        inner.Freeze();
        dc.DrawEllipse(inner, null, Center, FaceRadius, FaceRadius);

        var topShade = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(120, 0, 0, 0), 0.0),
            new(Color.FromArgb(0, 0, 0, 0), 0.28),
        }, new Point(0.5, 0), new Point(0.5, 1));
        topShade.Freeze();
        dc.DrawEllipse(topShade, null, Center, FaceRadius, FaceRadius);
    }

    private static readonly Color InkColor = Color.FromRgb(0x0E, 0x16, 0x0E);
    private static readonly Color RedInkColor = Color.FromRgb(0xC4, 0x12, 0x10);
    private static readonly Color AmberInkColor = Color.FromRgb(0xB0, 0x5A, 0x00);

    private static Color ZoneInk(ZoneKind z) => z switch
    {
        ZoneKind.Danger => RedInkColor,
        ZoneKind.Warning => AmberInkColor,
        ZoneKind.Good => Color.FromRgb(0x0A, 0x52, 0x16),
        _ => InkColor,
    };

    private static void DrawScale(DrawingContext dc, ScaleDefinition scale, double ppd)
    {
        var ink = VintageArt.Frozen(VintageArt.WithAlpha(InkColor, 0.92));
        double AngleOf(double v) => Arc.AngleFor(scale.ToNormalized(v));

        double aMin = AngleOf(scale.Minimum), aMax = AngleOf(scale.Maximum);

        // Main arc and coloured zones
        dc.DrawGeometry(null, VintageArt.FrozenPen(ink, 6, PenLineCap.Flat), ArcLine(ArcRadius, aMin, aMax));
        foreach (var z in scale.Zones)
        {
            double a0 = AngleOf(Math.Max(z.Start, scale.Minimum)), a1 = AngleOf(Math.Min(z.End, scale.Maximum));
            if (a1 <= a0) continue;
            var brush = VintageArt.Frozen(ZoneInk(z.Kind));
            // Band widens toward the top of the zone, like the printed red sector on classic dials.
            dc.DrawGeometry(brush, null, ArcBand(a0, a1, ArcRadius - 5, ArcRadius - 24, ArcRadius + 3));
        }

        // Minor ticks
        foreach (var v in scale.MinorTicks)
        {
            double a = AngleOf(v);
            var pen = VintageArt.FrozenPen(VintageArt.Frozen(ZoneInk(scale.ZoneAt(v))), 3);
            dc.DrawLine(pen, Polar(ArcRadius, a), Polar(ArcRadius + MinorTickLength, a));
        }

        // Major ticks and labels
        double labelSize = scale.MajorTicks.Count <= 11 && scale.MajorTicks.All(t => (t.Label?.Length ?? 0) <= 2) ? 46 : 33;
        foreach (var t in scale.MajorTicks)
        {
            double a = AngleOf(t.Value);
            var brush = VintageArt.Frozen(ZoneInk(scale.ZoneAt(t.Value)));
            dc.DrawLine(VintageArt.FrozenPen(brush, 5), Polar(ArcRadius - 2, a), Polar(ArcRadius + MajorTickLength, a));
            if (!string.IsNullOrEmpty(t.Label))
            {
                var ft = VintageArt.Text(t.Label, labelSize, brush, FontWeights.SemiBold, ppd);
                VintageArt.DrawCenteredText(dc, ft, Polar(ArcRadius + MajorTickLength + labelSize * 0.62, a), a * 0.85);
            }
        }

        // End caps
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(ZoneInk(scale.ZoneAt(scale.Minimum))), 6), Polar(ArcRadius - 12, aMin), Polar(ArcRadius + 3, aMin));
        dc.DrawLine(VintageArt.FrozenPen(VintageArt.Frozen(ZoneInk(scale.ZoneAt(scale.Maximum))), 6), Polar(ArcRadius - 26, aMax), Polar(ArcRadius + 3, aMax));

        // Secondary (percentage) scale: dots with small labels inside the arc
        foreach (var t in scale.SecondaryTicks)
        {
            double a = Arc.AngleFor(scale.ToNormalized(t.Value));
            dc.DrawEllipse(ink, null, Polar(ArcRadius - 38, a), 7, 7);
            if (!string.IsNullOrEmpty(t.Label))
            {
                var ft = VintageArt.Text(t.Label, 25, ink, FontWeights.SemiBold, ppd);
                VintageArt.DrawCenteredText(dc, ft, Polar(ArcRadius - 66, a), a * 0.85);
            }
        }

        // End glyphs
        if (scale.LowEndGlyph is { } lo)
        {
            var ft = VintageArt.Text(lo, 58, ink, FontWeights.Bold, ppd);
            VintageArt.DrawCenteredText(dc, ft, Polar(ArcRadius + 10, Arc.StartAngle - 11));
        }
        if (scale.HighEndGlyph is { } hi)
        {
            var ft = VintageArt.Text(hi, 58, VintageArt.Frozen(ZoneInk(scale.ZoneAt(scale.Maximum))), FontWeights.Bold, ppd);
            VintageArt.DrawCenteredText(dc, ft, Polar(ArcRadius + 10, Arc.EndAngle + 11));
        }

        // Legend
        bool hasCaption = !string.IsNullOrEmpty(scale.Caption);
        if (!string.IsNullOrEmpty(scale.Title))
        {
            double size = scale.Title.Length <= 2 ? 92 : 76;
            var ft = VintageArt.Text(scale.Title, size, ink, FontWeights.Bold, ppd);
            VintageArt.DrawCenteredText(dc, ft, new Point(500, hasCaption ? 468 : 520));
        }
        if (hasCaption)
        {
            var ft = VintageArt.Text(scale.Caption, 32, ink, FontWeights.SemiBold, ppd);
            var c = new Point(500, 528);
            VintageArt.DrawCenteredText(dc, ft, c);
            double half = ft.Width / 2 + 16;
            var rule = VintageArt.FrozenPen(ink, 3);
            dc.DrawLine(rule, new Point(c.X - half - 80, c.Y), new Point(c.X - half, c.Y));
            dc.DrawLine(rule, new Point(c.X + half, c.Y), new Point(c.X + half + 80, c.Y));
        }
    }

    private static Geometry? _podGeometry;
    private static Geometry? _podEdge;

    private static Geometry PodGeometry
    {
        get
        {
            if (_podGeometry != null) return _podGeometry;
            var path = Geometry.Parse("M 60,680 L 255,680 C 305,680 330,600 420,592 Q 500,585 580,592 C 670,600 695,680 745,680 L 940,680 L 940,960 L 60,960 Z");
            var g = Geometry.Combine(path, new EllipseGeometry(Center, FaceRadius, FaceRadius), GeometryCombineMode.Intersect, null);
            g.Freeze();
            _podGeometry = g;
            return g;
        }
    }

    private static Geometry PodEdge
    {
        get
        {
            if (_podEdge != null) return _podEdge;
            var edge = Geometry.Parse("M 100,680 L 255,680 C 305,680 330,600 420,592 Q 500,585 580,592 C 670,600 695,680 745,680 L 900,680");
            edge.Freeze();
            _podEdge = edge;
            return edge;
        }
    }

    private void DrawPod(DrawingContext dc)
    {
        double light = BacklightLevel;
        var pod = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x26, 0x34, 0x2C), 0.0),
            new(Color.FromRgb(0x17, 0x21, 0x1B), 0.25),
            new(Color.FromRgb(0x0B, 0x11, 0x0D), 1.0),
        }, new Point(0.5, 0), new Point(0.5, 1));
        pod.Freeze();
        dc.DrawGeometry(pod, null, PodGeometry);
        dc.DrawGeometry(VintageArt.GrainBrush(_builtDevicePixelsPerUnit, 0.35), null, PodGeometry);

        // Light from the dial catching the moulded top edge
        var edgeColor = VintageArt.Blend(Color.FromRgb(0x30, 0x40, 0x34), VintageArt.Blend(FaceColor, Colors.White, 0.3), 0.55 * light);
        dc.PushClip(new EllipseGeometry(Center, FaceRadius, FaceRadius));
        dc.DrawGeometry(null, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(edgeColor, 0.85)), 4), PodEdge);
        dc.Pop();

        VintageArt.DrawScrew(dc, new Point(500, 786), 25, 62);
    }

    private static void DrawGlass(DrawingContext dc)
    {
        // Curved specular reflection along the top of the window
        var crescent = Geometry.Combine(
            new EllipseGeometry(Center, FaceRadius - 8, FaceRadius - 8),
            new EllipseGeometry(new Point(Center.X, Center.Y + 52), FaceRadius - 2, FaceRadius - 14),
            GeometryCombineMode.Exclude, null);
        var spec = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(130, 255, 255, 255), 0.0),
            new(Color.FromArgb(40, 255, 255, 255), 0.5),
            new(Color.FromArgb(0, 255, 255, 255), 1.0),
        }, new Point(0.5, 0), new Point(0.5, 1))
        { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(500, 105), EndPoint = new Point(500, 190) };
        spec.Freeze();
        dc.DrawGeometry(spec, null, crescent);

        // Broad, faint sheen across the glass
        var sheen = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(28, 255, 255, 255), 0.0),
            new(Color.FromArgb(0, 255, 255, 255), 0.45),
            new(Color.FromArgb(0, 255, 255, 255), 0.8),
            new(Color.FromArgb(18, 255, 255, 255), 1.0),
        }, new Point(0.15, 0.1), new Point(0.85, 0.95));
        sheen.Freeze();
        dc.DrawEllipse(sheen, null, Center, FaceRadius, FaceRadius);
    }

    private static void DrawBezelScrews(DrawingContext dc)
    {
        void Screw(double angle, double slot)
        {
            var (x, y) = ArcGeometry.PointAt(Center.X, Center.Y, 452, angle);
            VintageArt.DrawScrew(dc, new Point(x, y), 21, slot);
        }
        Screw(0, 75);
        Screw(-124, 28);
        Screw(124, -40);
    }

    private void DrawNeedle()
    {
        var color = NeedleColor;
        double tip = ArcRadius + 20;
        var needle = new StreamGeometry();
        using (var ctx = needle.Open())
        {
            // Tapered blade from the pivot to a fine tip; the base is hidden behind the pod.
            ctx.BeginFigure(new Point(Pivot.X - 5.5, Pivot.Y), true, true);
            ctx.LineTo(new Point(Pivot.X - 1.6, Pivot.Y - tip), true, true);
            ctx.LineTo(new Point(Pivot.X + 1.6, Pivot.Y - tip), true, true);
            ctx.LineTo(new Point(Pivot.X + 5.5, Pivot.Y), true, true);
        }
        needle.Freeze();

        var body = new LinearGradientBrush(new GradientStopCollection
        {
            new(VintageArt.Blend(color, Colors.Black, 0.35), 0.0),
            new(VintageArt.Blend(color, Colors.White, 0.25), 0.45),
            new(color, 0.6),
            new(VintageArt.Blend(color, Colors.Black, 0.45), 1.0),
        }, new Point(0, 0.5), new Point(1, 0.5));
        body.Freeze();

        using (var dc = _needle.RenderOpen())
        {
            dc.DrawGeometry(body, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.WithAlpha(VintageArt.Blend(color, Colors.Black, 0.6), 0.6)), 0.8), needle);
        }
        using (var dc = _needleShadow.RenderOpen())
        {
            dc.DrawGeometry(VintageArt.Frozen(Color.FromArgb(60, 0, 20, 0)), null, needle);
        }
    }

    private void DrawPeakMarker()
    {
        using var dc = _peakMarker.RenderOpen();
        var pen = VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(230, 0xF0, 0x8A, 0x00)), 4, PenLineCap.Round);
        dc.DrawLine(pen, new Point(Pivot.X, Pivot.Y - ArcRadius + 30), new Point(Pivot.X, Pivot.Y - ArcRadius - 4));
    }

    private void UpdatePeakMarker()
    {
        if (!double.IsNaN(PeakHoldValue)) PlacePeakMarker(PeakHoldValue);
        else if (IsPeakHoldEnabled) PlacePeakMarker(_hold.Value);
        else _peakMarker.Opacity = 0;
    }

    private void PlacePeakMarker(double value)
    {
        var scale = EffectiveScale;
        if (double.IsNaN(value) || value < scale.Minimum)
        {
            _peakMarker.Opacity = 0;
            return;
        }
        _peakMarker.Opacity = 1;
        double angle = Arc.AngleFor(scale.ToNormalized(value));
        if (Math.Abs(_peakRotation.Angle - angle) > 1e-4) _peakRotation.Angle = angle;
    }

    private void DrawLamp()
    {
        using var dc = _lamp.RenderOpen();
        if (!ShowOverloadLamp) return;
        VintageArt.DrawLamp(dc, LampCenter, LampRadius, Color.FromRgb(0xFF, 0x22, 0x10), IsOverloadLit);
        var label = VintageArt.Text(OverloadLabel ?? "", 22, VintageArt.Frozen(Color.FromRgb(0x9A, 0xA8, 0x9E)), FontWeights.SemiBold, 1.0);
        VintageArt.DrawCenteredText(dc, label, new Point(LampCenter.X, LampCenter.Y + 46));
    }
}

/// <summary>Exposes the meter reading to UI Automation as a read-only range value.</summary>
public sealed class AnalogMeterAutomationPeer : FrameworkElementAutomationPeer, IRangeValueProvider
{
    public AnalogMeterAutomationPeer(AnalogMeter owner) : base(owner) { }

    private AnalogMeter Meter => (AnalogMeter)Owner;

    protected override string GetClassNameCore() => nameof(AnalogMeter);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ProgressBar;

    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        return string.IsNullOrEmpty(name) ? $"{Meter.EffectiveScale.Title} meter" : name;
    }

    public override object GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface)!;

    bool IRangeValueProvider.IsReadOnly => true;
    double IRangeValueProvider.LargeChange => 0;
    double IRangeValueProvider.SmallChange => 0;
    double IRangeValueProvider.Maximum => Meter.EffectiveScale.Maximum;
    double IRangeValueProvider.Minimum => Meter.EffectiveScale.Minimum;
    double IRangeValueProvider.Value =>
        Meter.IsValueValid && !double.IsNaN(Meter.Value) ? Math.Max(Meter.Value, Meter.EffectiveScale.Minimum) : Meter.EffectiveScale.Minimum;
    void IRangeValueProvider.SetValue(double value) => throw new ElementNotEnabledException();
}
