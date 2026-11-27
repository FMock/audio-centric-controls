using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AudioCentric.Controls.Input;
using AudioCentric.Controls.Wpf.Rendering;

namespace AudioCentric.Controls.Wpf.Controls;

public enum IlluminatedToggleStyle
{
    /// <summary>Rounded push-button with an illuminated lens and legend.</summary>
    PushButton,
    /// <summary>Chrome bat-handle lever switch with a status lamp above it.</summary>
    Lever,
    /// <summary>Square illuminated cap (record-arm / solo / mute style).</summary>
    SquareCap,
}

/// <summary>
/// Illuminated switch for binary states such as power, mute, bypass, solo or record-arm.
/// <see cref="ToggleButton.IsChecked"/> is the user's request. <see cref="ActualState"/> (when the host sets it)
/// is what the engine applied, and the illumination follows it. Pending and fault states stay visible.
/// <see cref="ToggleMode.Momentary"/> is on only while pressed.
/// Keyboard (Space), commands and UI Automation's Toggle pattern come from <see cref="ToggleButton"/>.
/// </summary>
public class IlluminatedToggle : ToggleButton
{
    static IlluminatedToggle()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(IlluminatedToggle), new FrameworkPropertyMetadata(typeof(IlluminatedToggle)));
        IsCheckedProperty.OverrideMetadata(typeof(IlluminatedToggle), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        IsEnabledProperty.OverrideMetadata(typeof(IlluminatedToggle), new UIPropertyMetadata(true, (d, _) => ((UIElement)d).InvalidateVisual()));
    }

    protected override void OnGotKeyboardFocus(System.Windows.Input.KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); InvalidateVisual(); }
    protected override void OnLostKeyboardFocus(System.Windows.Input.KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); InvalidateVisual(); }

    public IlluminatedToggleStyle SwitchStyle { get => (IlluminatedToggleStyle)GetValue(SwitchStyleProperty); set => SetValue(SwitchStyleProperty, value); }
    public static readonly DependencyProperty SwitchStyleProperty = DependencyProperty.Register(
        nameof(SwitchStyle), typeof(IlluminatedToggleStyle), typeof(IlluminatedToggle),
        new FrameworkPropertyMetadata(IlluminatedToggleStyle.PushButton, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    public ToggleMode Mode { get => (ToggleMode)GetValue(ModeProperty); set => SetValue(ModeProperty, value); }
    public static readonly DependencyProperty ModeProperty = DependencyProperty.Register(
        nameof(Mode), typeof(ToggleMode), typeof(IlluminatedToggle), new FrameworkPropertyMetadata(ToggleMode.Latching));

    /// <summary>Host-acknowledged state. Null = follow <see cref="ToggleButton.IsChecked"/> (no acknowledgement needed).</summary>
    public bool? ActualState { get => (bool?)GetValue(ActualStateProperty); set => SetValue(ActualStateProperty, value); }
    public static readonly DependencyProperty ActualStateProperty = DependencyProperty.Register(
        nameof(ActualState), typeof(bool?), typeof(IlluminatedToggle), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ToggleStatus Status { get => (ToggleStatus)GetValue(StatusProperty); set => SetValue(StatusProperty, value); }
    public static readonly DependencyProperty StatusProperty = DependencyProperty.Register(
        nameof(Status), typeof(ToggleStatus), typeof(IlluminatedToggle), new FrameworkPropertyMetadata(ToggleStatus.Ready, FrameworkPropertyMetadataOptions.AffectsRender));

    public string OnLabel { get => (string)GetValue(OnLabelProperty); set => SetValue(OnLabelProperty, value); }
    public static readonly DependencyProperty OnLabelProperty = DependencyProperty.Register(
        nameof(OnLabel), typeof(string), typeof(IlluminatedToggle), new FrameworkPropertyMetadata("ON", FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Legend when off; empty = same as <see cref="OnLabel"/>.</summary>
    public string OffLabel { get => (string)GetValue(OffLabelProperty); set => SetValue(OffLabelProperty, value); }
    public static readonly DependencyProperty OffLabelProperty = DependencyProperty.Register(
        nameof(OffLabel), typeof(string), typeof(IlluminatedToggle), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public Color LampColor { get => (Color)GetValue(LampColorProperty); set => SetValue(LampColorProperty, value); }
    public static readonly DependencyProperty LampColorProperty = DependencyProperty.Register(
        nameof(LampColor), typeof(Color), typeof(IlluminatedToggle),
        new FrameworkPropertyMetadata(Color.FromRgb(0x5C, 0xF0, 0x40), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Draw a power symbol next to the legend (PushButton style).</summary>
    public bool ShowPowerIcon { get => (bool)GetValue(ShowPowerIconProperty); set => SetValue(ShowPowerIconProperty, value); }
    public static readonly DependencyProperty ShowPowerIconProperty = DependencyProperty.Register(
        nameof(ShowPowerIcon), typeof(bool), typeof(IlluminatedToggle), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Whether the lamp is lit: the acknowledged state, if any, else the requested state.</summary>
    public bool IsLit => IsEnabled && Status != ToggleStatus.Fault && (ActualState ?? IsChecked == true);

    protected override void OnToggle()
    {
        if (Mode == ToggleMode.Momentary) return;
        base.OnToggle();
    }

    protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsPressedChanged(e);
        if (Mode == ToggleMode.Momentary) SetCurrentValue(IsCheckedProperty, IsPressed);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size constraint)
    {
        base.MeasureOverride(constraint);
        var natural = SwitchStyle switch
        {
            IlluminatedToggleStyle.Lever => new Size(56, 96),
            IlluminatedToggleStyle.SquareCap => new Size(54, 54),
            _ => new Size(Math.Max(96, 46 + MeasureLabel() + (ShowPowerIcon ? 22 : 0)), 38),
        };
        // Never ask for more than offered: the artwork scales to the arranged size.
        return new Size(Math.Min(natural.Width, constraint.Width), Math.Min(natural.Height, constraint.Height));
    }

    private double MeasureLabel()
    {
        var text = string.IsNullOrEmpty(OnLabel) ? " " : OnLabel;
        return VintageArt.Text(text, 14, Brushes.White, FontWeights.SemiBold, VisualTreeHelper.GetDpi(this).PixelsPerDip).Width;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        switch (SwitchStyle)
        {
            case IlluminatedToggleStyle.Lever: RenderLever(dc, size); break;
            case IlluminatedToggleStyle.SquareCap: RenderSquare(dc, size); break;
            default: RenderPush(dc, size); break;
        }
    }

    private Color EffectiveLamp => Status switch
    {
        ToggleStatus.Pending => Color.FromRgb(0xFF, 0xB0, 0x1E),
        ToggleStatus.Fault => Color.FromRgb(0xFF, 0x30, 0x20),
        _ => LampColor,
    };

    private bool LampOn => Status == ToggleStatus.Pending || Status == ToggleStatus.Fault || IsLit;

    private string Legend => IsChecked == true || string.IsNullOrEmpty(OffLabel) ? OnLabel : OffLabel;

    private void RenderPush(DrawingContext dc, Size size)
    {
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double h = size.Height, w = size.Width, r = h / 2;
        var outer = new Rect(0.5, 0.5, w - 1, h - 1);
        bool pressed = IsPressed;
        var lamp = EffectiveLamp;
        bool on = LampOn;

        // Bezel
        var bezel = new LinearGradientBrush(Color.FromRgb(0x30, 0x32, 0x30), Color.FromRgb(0x0A, 0x0B, 0x0A), 90);
        bezel.Freeze();
        dc.DrawRoundedRectangle(bezel, VintageArt.FrozenPen(VintageArt.Frozen(on ? VintageArt.WithAlpha(lamp, 0.55) : Color.FromRgb(0x3E, 0x42, 0x3E)), 1.2), outer, r, r);

        // Cap
        var cap = new Rect(3, pressed ? 4 : 3, w - 6, h - 7);
        var capBrush = new LinearGradientBrush(
            pressed ? Color.FromRgb(0x14, 0x15, 0x14) : Color.FromRgb(0x2C, 0x2E, 0x2C),
            pressed ? Color.FromRgb(0x1C, 0x1E, 0x1C) : Color.FromRgb(0x12, 0x13, 0x12), 90);
        capBrush.Freeze();
        dc.DrawRoundedRectangle(capBrush, null, cap, cap.Height / 2, cap.Height / 2);
        if (!pressed)
        {
            var hi = new LinearGradientBrush(Color.FromArgb(60, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90);
            hi.Freeze();
            dc.DrawRoundedRectangle(hi, null, new Rect(cap.X + 4, cap.Y + 1, cap.Width - 8, cap.Height * 0.45), cap.Height / 4, cap.Height / 4);
        }

        // Lens / icon
        double x = r + 2;
        var c = new Point(x, cap.Y + cap.Height / 2);
        if (ShowPowerIcon)
        {
            var brush = VintageArt.Frozen(on ? lamp : Color.FromRgb(0x5A, 0x62, 0x5A));
            if (on)
            {
                var halo = new RadialGradientBrush(VintageArt.WithAlpha(lamp, 0.45), VintageArt.WithAlpha(lamp, 0));
                halo.Freeze();
                dc.DrawEllipse(halo, null, c, h * 0.42, h * 0.42);
            }
            var pen = VintageArt.FrozenPen(brush, 1.8, PenLineCap.Round);
            double ir = h * 0.2;
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                var (sx, sy) = Scales.ArcGeometry.PointAt(c.X, c.Y, ir, 35);
                var (ex, ey) = Scales.ArcGeometry.PointAt(c.X, c.Y, ir, -35);
                ctx.BeginFigure(new Point(sx, sy), false, false);
                ctx.ArcTo(new Point(ex, ey), new Size(ir, ir), 0, true, SweepDirection.Clockwise, true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
            dc.DrawLine(pen, new Point(c.X, c.Y - ir * 1.25), new Point(c.X, c.Y - ir * 0.1));
            x += h * 0.45;
        }
        else
        {
            VintageArt.DrawLamp(dc, c, h * 0.16, lamp, on, 0.8);
            x += h * 0.3;
        }

        var text = VintageArt.Text(Legend, 14, VintageArt.Frozen(on ? VintageArt.Blend(lamp, Colors.White, 0.35) : Color.FromRgb(0xC8, 0xCE, 0xC8)),
            FontWeights.SemiBold, ppd);
        double tx = x + (w - x - r * 0.6 - text.Width) / 2;
        dc.DrawText(text, new Point(Math.Max(x + 2, tx), c.Y - text.Height / 2));

        if (!IsEnabled) dc.DrawRoundedRectangle(VintageArt.Frozen(Color.FromArgb(120, 0, 0, 0)), null, outer, r, r);
        if (IsKeyboardFocused) DrawFocus(dc, outer, r);
    }

    private void RenderSquare(DrawingContext dc, Size size)
    {
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double s = Math.Min(size.Width, size.Height);
        var outer = new Rect((size.Width - s) / 2 + 0.5, (size.Height - s) / 2 + 0.5, s - 1, s - 1);
        var lamp = EffectiveLamp;
        bool on = LampOn;
        bool pressed = IsPressed;

        var bezel = new LinearGradientBrush(Color.FromRgb(0x34, 0x36, 0x34), Color.FromRgb(0x08, 0x08, 0x08), 90);
        bezel.Freeze();
        dc.DrawRoundedRectangle(bezel, VintageArt.FrozenPen(VintageArt.Frozen(Colors.Black), 1), outer, s * 0.12, s * 0.12);

        var cap = outer;
        cap.Inflate(-s * 0.1, -s * 0.1);
        if (pressed) cap.Offset(0, 1);
        GradientStopCollection stops = on
            ? new() { new(VintageArt.Blend(lamp, Colors.White, 0.55), 0), new(lamp, 0.5), new(VintageArt.Blend(lamp, Colors.Black, 0.3), 1) }
            : new() { new(VintageArt.Blend(lamp, Colors.Black, 0.65), 0), new(VintageArt.Blend(lamp, Colors.Black, 0.8), 1) };
        var capBrush = new RadialGradientBrush(stops) { GradientOrigin = new Point(0.5, 0.35) };
        capBrush.Freeze();
        if (on)
        {
            var glow = outer;
            glow.Inflate(s * 0.08, s * 0.08);
            var halo = new RadialGradientBrush(VintageArt.WithAlpha(lamp, 0.35), VintageArt.WithAlpha(lamp, 0));
            halo.Freeze();
            dc.DrawRoundedRectangle(halo, null, glow, s * 0.2, s * 0.2);
        }
        dc.DrawRoundedRectangle(capBrush, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(160, 0, 0, 0)), 1), cap, s * 0.08, s * 0.08);
        var spec = new LinearGradientBrush(Color.FromArgb(90, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90);
        spec.Freeze();
        dc.DrawRoundedRectangle(spec, null, new Rect(cap.X + 2, cap.Y + 2, cap.Width - 4, cap.Height * 0.4), s * 0.06, s * 0.06);

        var text = VintageArt.Text(Legend, Math.Max(8, s * 0.2), VintageArt.Frozen(on ? Color.FromRgb(0x10, 0x14, 0x10) : Color.FromRgb(0xC0, 0xC6, 0xC0)), FontWeights.Bold, ppd);
        VintageArt.DrawCenteredText(dc, text, new Point(cap.X + cap.Width / 2, cap.Y + cap.Height / 2));

        if (!IsEnabled) dc.DrawRoundedRectangle(VintageArt.Frozen(Color.FromArgb(120, 0, 0, 0)), null, outer, s * 0.12, s * 0.12);
        if (IsKeyboardFocused) DrawFocus(dc, outer, s * 0.12);
    }

    private void RenderLever(DrawingContext dc, Size size)
    {
        double ppd = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        // Design space 56 × 96, scaled uniformly.
        double k = Math.Min(size.Width / 56, size.Height / 96);
        dc.PushTransform(new MatrixTransform(k, 0, 0, k, (size.Width - 56 * k) / 2, (size.Height - 96 * k) / 2));

        var lamp = EffectiveLamp;
        bool on = LampOn;
        bool up = IsChecked == true;

        VintageArt.DrawLamp(dc, new Point(28, 10), 5.5, lamp, on, 0.9);

        // Panel nut (hex)
        var c = new Point(28, 52);
        var hex = new StreamGeometry();
        using (var ctx = hex.Open())
        {
            for (int i = 0; i < 6; i++)
            {
                var (x, y) = Scales.ArcGeometry.PointAt(c.X, c.Y, 15, 30 + i * 60);
                if (i == 0) ctx.BeginFigure(new Point(x, y), true, true); else ctx.LineTo(new Point(x, y), true, true);
            }
        }
        hex.Freeze();
        var nut = new LinearGradientBrush(Color.FromRgb(0xD8, 0xD8, 0xD4), Color.FromRgb(0x52, 0x52, 0x50), new Point(0.2, 0), new Point(0.8, 1));
        nut.Freeze();
        dc.DrawGeometry(nut, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0x20, 0x20, 0x20)), 1), hex);
        var bushing = new RadialGradientBrush(Color.FromRgb(0x9A, 0x9A, 0x96), Color.FromRgb(0x2A, 0x2A, 0x28)) { GradientOrigin = new Point(0.4, 0.35) };
        bushing.Freeze();
        dc.DrawEllipse(bushing, VintageArt.FrozenPen(VintageArt.Frozen(Colors.Black), 0.8), c, 8.5, 8.5);

        // Bat handle: tapers toward the tip; the tip is drawn larger (closer to the viewer)
        double dir = up ? -1 : 1;
        var tip = new Point(c.X, c.Y + dir * 30);
        var bat = new StreamGeometry();
        using (var ctx = bat.Open())
        {
            ctx.BeginFigure(new Point(c.X - 4, c.Y), true, true);
            ctx.LineTo(new Point(tip.X - 5.5, tip.Y), true, true);
            ctx.LineTo(new Point(tip.X + 5.5, tip.Y), true, true);
            ctx.LineTo(new Point(c.X + 4, c.Y), true, true);
        }
        bat.Freeze();
        var chrome = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0x4A, 0x4A, 0x48), 0.0),
            new(Color.FromRgb(0xF4, 0xF4, 0xF0), 0.35),
            new(Color.FromRgb(0x9A, 0x9A, 0x96), 0.6),
            new(Color.FromRgb(0x30, 0x30, 0x2E), 1.0),
        }, new Point(0, 0.5), new Point(1, 0.5));
        chrome.Freeze();
        dc.DrawGeometry(VintageArt.Frozen(Color.FromArgb(90, 0, 0, 0)), null, Translate(bat, 2.5, 3));
        dc.DrawGeometry(chrome, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0x22, 0x22, 0x22)), 0.6), bat);
        var ball = new RadialGradientBrush(new GradientStopCollection
        {
            new(Colors.White, 0.0), new(Color.FromRgb(0xA8, 0xA8, 0xA4), 0.45), new(Color.FromRgb(0x3A, 0x3A, 0x38), 1.0),
        })
        { GradientOrigin = new Point(0.38, 0.32) };
        ball.Freeze();
        dc.DrawEllipse(ball, VintageArt.FrozenPen(VintageArt.Frozen(Color.FromRgb(0x22, 0x22, 0x22)), 0.6), tip, 6.2, 6.2);

        var text = VintageArt.Text(Legend, 9.5, VintageArt.Frozen(on ? VintageArt.Blend(lamp, Colors.White, 0.3) : Color.FromRgb(0xB0, 0xB8, 0xB0)), FontWeights.SemiBold, ppd);
        VintageArt.DrawCenteredText(dc, text, new Point(28, 90));
        if (IsKeyboardFocused) DrawFocus(dc, new Rect(1, 1, 54, 94), 6);
        dc.Pop();
    }

    private static Geometry Translate(Geometry g, double dx, double dy)
    {
        var clone = g.Clone();
        clone.Transform = new TranslateTransform(dx, dy);
        clone.Freeze();
        return clone;
    }

    private void DrawFocus(DrawingContext dc, Rect r, double radius)
    {
        var pen = new Pen(VintageArt.Frozen(VintageArt.WithAlpha(LampColor, 0.8)), 1.2) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        var fr = r;
        fr.Inflate(-1.5, -1.5);
        dc.DrawRoundedRectangle(null, pen, fr, radius, radius);
    }
}
