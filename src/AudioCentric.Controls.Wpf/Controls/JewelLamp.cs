using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Media;
using AudioCentric.Controls.Wpf.Rendering;

namespace AudioCentric.Controls.Wpf.Controls;

/// <summary>
/// Faceted jewel pilot lamp in a metal bezel, as on vintage test equipment. Unlit it shows a dark faceted jewel that
/// catches the room light. Lit, the bulb behind it glows through the facets and spills a soft halo onto the panel.
/// Switching fades the light like a small incandescent bulb (<see cref="WarmUpSeconds"/>); only the glow layers'
/// opacity changes per frame. Display only: bind <see cref="IsLit"/> to the state it indicates.
/// </summary>
public class JewelLamp : FrameworkElement, IRenderTickable
{
    private const double DesignSize = 200, JewelR = 57;
    private static readonly Point C = new(100, 100);

    private readonly VisualCollection _children;
    private readonly ContainerVisual _root = new();
    private readonly DrawingVisual _bezel = new();
    private readonly DrawingVisual _unlit = new();
    private readonly DrawingVisual _lit = new();
    private readonly DrawingVisual _glass = new();
    private readonly DrawingVisual _halo = new();
    private readonly MatrixTransform _rootTransform = new();
    private double _intensity;
    private bool _registered;

    public JewelLamp()
    {
        _children = new VisualCollection(this) { _root };
        _root.Transform = _rootTransform;
        _root.Children.Add(_bezel);
        _root.Children.Add(_unlit);
        _root.Children.Add(_lit);
        _root.Children.Add(_glass);
        _root.Children.Add(_halo);
        Loaded += (_, _) => { if (!_registered) { _registered = true; RenderScheduler.Register(this); } };
        Unloaded += (_, _) => { if (_registered) { _registered = false; RenderScheduler.Unregister(this); } };
        RebuildAll();
        ApplyIntensity(Target);
    }

    #region Dependency properties

    public bool IsLit { get => (bool)GetValue(IsLitProperty); set => SetValue(IsLitProperty, value); }
    public static readonly DependencyProperty IsLitProperty = DependencyProperty.Register(
        nameof(IsLit), typeof(bool), typeof(JewelLamp), new FrameworkPropertyMetadata(false, OnLevelChanged));

    /// <summary>Lit brightness 0…1 (e.g. to dim a panel at night). Default 1.</summary>
    public double Brightness { get => (double)GetValue(BrightnessProperty); set => SetValue(BrightnessProperty, value); }
    public static readonly DependencyProperty BrightnessProperty = DependencyProperty.Register(
        nameof(Brightness), typeof(double), typeof(JewelLamp), new FrameworkPropertyMetadata(1.0, OnLevelChanged));

    /// <summary>Colour of the jewel. Default ruby red; amber, green, blue and clear jewels are common too.</summary>
    public Color LampColor { get => (Color)GetValue(LampColorProperty); set => SetValue(LampColorProperty, value); }
    public static readonly DependencyProperty LampColorProperty = DependencyProperty.Register(
        nameof(LampColor), typeof(Color), typeof(JewelLamp), new FrameworkPropertyMetadata(Color.FromRgb(0xFF, 0x1E, 0x2E), OnLookChanged));

    /// <summary>Bezel metal colour. Default aged brass; try silver for chrome.</summary>
    public Color BezelColor { get => (Color)GetValue(BezelColorProperty); set => SetValue(BezelColorProperty, value); }
    public static readonly DependencyProperty BezelColorProperty = DependencyProperty.Register(
        nameof(BezelColor), typeof(Color), typeof(JewelLamp), new FrameworkPropertyMetadata(Color.FromRgb(0xC2, 0xA0, 0x5C), OnLookChanged));

    /// <summary>Time constant of the bulb's warm-up in seconds (cool-down is a little slower). 0 = instant.</summary>
    public double WarmUpSeconds { get => (double)GetValue(WarmUpSecondsProperty); set => SetValue(WarmUpSecondsProperty, value); }
    public static readonly DependencyProperty WarmUpSecondsProperty = DependencyProperty.Register(
        nameof(WarmUpSeconds), typeof(double), typeof(JewelLamp), new FrameworkPropertyMetadata(0.08), v => (double)v >= 0);

    #endregion

    /// <summary>Current light output 0…1 (follows <see cref="IsLit"/> × <see cref="Brightness"/> with the warm-up).</summary>
    public double Intensity => _intensity;

    private double Target => IsLit ? Math.Clamp(Brightness, 0, 1) : 0;

    private static void OnLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var l = (JewelLamp)d;
        if (!l._registered || l.WarmUpSeconds <= 0) l.ApplyIntensity(l.Target);
        (UIElementAutomationPeer.FromElement(l))?.InvalidatePeer();
    }

    private static void OnLookChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((JewelLamp)d).RebuildAll();

    void IRenderTickable.Tick(double elapsedSeconds)
    {
        double target = Target;
        if (Math.Abs(target - _intensity) < 1e-3)
        {
            if (_intensity != target) ApplyIntensity(target);
            return;
        }
        double tau = target > _intensity ? WarmUpSeconds : WarmUpSeconds * 1.8;
        double a = tau <= 0 ? 1 : 1 - Math.Exp(-elapsedSeconds / tau);
        ApplyIntensity(_intensity + (target - _intensity) * a);
    }

    private void ApplyIntensity(double value)
    {
        _intensity = value;
        _lit.Opacity = value;
        _halo.Opacity = value;
    }

    // ---------------------------------------------------------------- layout

    protected override int VisualChildrenCount => _children.Count;
    protected override Visual GetVisualChild(int index) => _children[index];

    protected override Size MeasureOverride(Size availableSize)
    {
        double s = Math.Min(availableSize.Width, availableSize.Height);
        if (double.IsInfinity(s)) s = double.IsInfinity(availableSize.Width) && double.IsInfinity(availableSize.Height) ? 32 : Math.Min(availableSize.Width, availableSize.Height);
        if (double.IsInfinity(s)) s = 32;
        return new Size(s, s);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double s = Math.Min(finalSize.Width, finalSize.Height), k = s / DesignSize;
        _rootTransform.Matrix = new Matrix(k, 0, 0, k, (finalSize.Width - s) / 2, (finalSize.Height - s) / 2);
        return finalSize;
    }

    // ---------------------------------------------------------------- drawing

    private void RebuildAll()
    {
        DrawBezel();
        DrawJewel(_unlit, lit: false);
        DrawJewel(_lit, lit: true);
        DrawGlass();
        DrawHalo();
    }

    private static Point P(double r, double deg)
    {
        double a = deg * Math.PI / 180;
        return new Point(C.X + r * Math.Sin(a), C.Y - r * Math.Cos(a));
    }

    private void DrawBezel()
    {
        using var dc = _bezel.RenderOpen();
        var metal = BezelColor;
        var shadow = new RadialGradientBrush(Color.FromArgb(150, 0, 0, 0), Color.FromArgb(0, 0, 0, 0)) { GradientOrigin = new Point(0.5, 0.5) };
        shadow.Freeze();
        dc.DrawEllipse(shadow, null, new Point(C.X + 3, C.Y + 5), 84, 84);

        var ring = new LinearGradientBrush(new GradientStopCollection
        {
            new(VintageArt.Blend(metal, Colors.White, 0.55), 0), new(metal, 0.3),
            new(VintageArt.Blend(metal, Colors.Black, 0.35), 0.7), new(VintageArt.Blend(metal, Colors.Black, 0.55), 1),
        }, new Point(0.15, 0.1), new Point(0.85, 0.95));
        ring.Freeze();
        dc.DrawEllipse(ring, VintageArt.FrozenPen(VintageArt.Frozen(VintageArt.Blend(metal, Colors.Black, 0.7)), 1.5), C, 76, 76);
        dc.DrawEllipse(VintageArt.GrainBrush(2.0, 0.25), null, C, 76, 76);
        // Inner bevel turned the other way, so the ring reads as a rolled lip
        var bevel = new LinearGradientBrush(VintageArt.Blend(metal, Colors.Black, 0.6), VintageArt.Blend(metal, Colors.White, 0.35), new Point(0.2, 0.1), new Point(0.8, 0.9));
        bevel.Freeze();
        dc.DrawEllipse(bevel, null, C, JewelR + 6, JewelR + 6);
        dc.DrawEllipse(VintageArt.Frozen(Color.FromRgb(0x08, 0x04, 0x04)), null, C, JewelR + 1.5, JewelR + 1.5);
    }

    /// <summary>Facets of a rose-cut dome: an octagonal table, two rings of eight triangles, and an outer ring of 24 triangles.</summary>
    private static IEnumerable<Point[]> Facets()
    {
        const double rTable = 15, rMid = 35;
        Point T(int i) => P(rTable, i * 45);
        Point M(int i) => P(rMid, i * 45 + 22.5);
        Point O(int j) => P(JewelR + 1, j * 22.5);
        yield return Enumerable.Range(0, 8).Select(T).ToArray();
        for (int i = 0; i < 8; i++)
        {
            yield return new[] { T(i), T(i + 1), M(i) };
            yield return new[] { M(i - 1 < 0 ? 7 : i - 1), T(i), M(i) };
        }
        for (int i = 0; i < 8; i++)
        {
            yield return new[] { M(i), O(2 * i), O(2 * i + 1) };
            yield return new[] { M(i), O(2 * i + 1), O(2 * i + 2) };
            yield return new[] { M(i), M((i + 1) % 8), O(2 * i + 2) };
        }
    }

    private void DrawJewel(DrawingVisual target, bool lit)
    {
        using var dc = target.RenderOpen();
        var c = LampColor;
        var deep = VintageArt.Blend(c, Colors.Black, lit ? 0.35 : 0.93);
        dc.PushClip(new EllipseGeometry(C, JewelR, JewelR));
        dc.DrawEllipse(VintageArt.Frozen(deep), null, C, JewelR, JewelR);

        // Light from the top left, slightly in front.
        double lx = -0.45, ly = -0.62, lz = 0.64;
        int index = 0;
        foreach (var poly in Facets())
        {
            double cx = poly.Average(p => p.X) - C.X, cy = poly.Average(p => p.Y) - C.Y;
            double r = Math.Sqrt(cx * cx + cy * cy) / JewelR;
            double phi = Math.Atan2(cy, cx), tilt = r * 0.95;
            double nx = Math.Sin(tilt) * Math.Cos(phi), ny = Math.Sin(tilt) * Math.Sin(phi), nz = Math.Cos(tilt);
            double facing = Math.Max(0, nx * lx + ny * ly + nz * lz);
            double jitter = Hash(index++);
            Color col;
            if (lit)
            {
                // The bulb shines through: hottest in the middle, every facet a slightly different brightness, a few sparkle.
                double glow = 0.55 + 0.45 * (1 - r) + 0.25 * (jitter - 0.5);
                col = VintageArt.Blend(VintageArt.Blend(c, Colors.Black, 0.45), c, Math.Clamp(glow, 0, 1));
                if (jitter > 0.82) col = VintageArt.Blend(col, Colors.White, 0.35 * (1 - r) + 0.15);
                col = VintageArt.Blend(col, Colors.White, 0.25 * Math.Pow(facing, 6));
            }
            else
            {
                // Dark glass reflecting the room: facets facing the light pick up a dull sheen.
                double sheen = 0.03 + 0.38 * Math.Pow(facing, 4) + 0.08 * jitter;
                col = VintageArt.Blend(VintageArt.Blend(c, Colors.Black, 0.93), VintageArt.Blend(c, Colors.Black, 0.25), Math.Clamp(sheen, 0, 1));
                if (facing > 0.94) col = VintageArt.Blend(col, Colors.White, 0.45);
            }
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(poly[0], true, true);
                for (int i = 1; i < poly.Length; i++) ctx.LineTo(poly[i], true, false);
            }
            g.Freeze();
            dc.DrawGeometry(VintageArt.Frozen(col), VintageArt.FrozenPen(VintageArt.Frozen(Color.FromArgb(lit ? (byte)70 : (byte)110, 0, 0, 0)), 0.9), g);
        }

        if (lit)
        {
            var core = new RadialGradientBrush(Color.FromArgb(150, 255, 236, 200), Color.FromArgb(0, 255, 236, 200));
            core.Freeze();
            dc.DrawEllipse(core, null, C, JewelR * 0.55, JewelR * 0.55);
        }
        // Darker rim where the dome meets the bezel
        var rim = new RadialGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb(0, 0, 0, 0), 0.78), new(Color.FromArgb(lit ? (byte)90 : (byte)160, 0, 0, 0), 1),
        });
        rim.Freeze();
        dc.DrawEllipse(rim, null, C, JewelR, JewelR);
        dc.Pop();
    }

    private void DrawGlass()
    {
        using var dc = _glass.RenderOpen();
        var spec = new RadialGradientBrush(Color.FromArgb(170, 255, 255, 255), Color.FromArgb(0, 255, 255, 255));
        spec.Freeze();
        dc.DrawEllipse(spec, null, new Point(C.X - 20, C.Y - 24), 16, 10);
        dc.DrawEllipse(VintageArt.Frozen(Color.FromArgb(200, 255, 255, 255)), null, new Point(C.X - 22, C.Y - 26), 3.2, 2.2);
    }

    private void DrawHalo()
    {
        using var dc = _halo.RenderOpen();
        var c = LampColor;
        var halo = new RadialGradientBrush(new GradientStopCollection
        {
            new(VintageArt.WithAlpha(c, 0.0), 0.0), new(VintageArt.WithAlpha(c, 0.0), 0.58),
            new(VintageArt.WithAlpha(c, 0.35), 0.66), new(VintageArt.WithAlpha(c, 0.12), 0.82), new(VintageArt.WithAlpha(c, 0), 1),
        });
        halo.Freeze();
        dc.DrawEllipse(halo, null, C, 99, 99);
    }

    private static double Hash(int i)
    {
        uint x = (uint)i * 2654435761u ^ 0x9E3779B9u;
        x ^= x >> 15; x *= 2246822519u; x ^= x >> 13;
        return (x & 0xFFFF) / 65535.0;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new JewelLampAutomationPeer(this);
}

public sealed class JewelLampAutomationPeer : FrameworkElementAutomationPeer
{
    public JewelLampAutomationPeer(JewelLamp owner) : base(owner) { }
    protected override string GetClassNameCore() => nameof(JewelLamp);
    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Image;
    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        return !string.IsNullOrEmpty(name) ? name : ((JewelLamp)Owner).IsLit ? "Pilot lamp, on" : "Pilot lamp, off";
    }
}
