using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using AudioCentric.Controls.Scales;
using AudioCentric.Controls.Wpf.Controls;

namespace AudioCentric.Controls.Wpf.Tests;

public class VintageControlTests
{
    [Fact]
    public void TuningDial_KeepsPositionAndFrequencyInSync()
    {
        RenderHelper.RunSta(() =>
        {
            var d = new TuningDial { SelectedBandIndex = 2 };              // 5.3 – 16 MHz
            d.Frequency = 7.1;
            var band = DialPresets.ShortwaveReceiver[2];
            Assert.Equal(band.ToNormalized(7.1), d.Position, 9);
            Assert.Equal(d.Position * 180, d.DiscAngle, 9);

            d.Position = 0.5;
            Assert.Equal(band.FromNormalized(0.5), d.Frequency, 9);

            // Switching band leaves the disc where it is and reads the new band.
            d.SelectedBandIndex = 3;
            Assert.Equal(0.5, d.Position, 9);
            Assert.Equal(DialPresets.ShortwaveReceiver[3].FromNormalized(0.5), d.Frequency, 9);

            d.SelectedBandIndex = 99;
            Assert.Equal(3, d.SelectedBandIndex);                         // coerced to the last band
            d.Position = 2;
            Assert.Equal(1, d.Position);
        });
    }

    [Fact]
    public void TuningDial_RendersWithAndWithoutCoverPlate()
    {
        RenderHelper.RunSta(() =>
        {
            var d = new TuningDial { SelectedBandIndex = 2, Frequency = 7.15 };
            var full = RenderHelper.Render(d, new Size(560, 560), 144);
            Assert.True(RenderHelper.InkCoverage(full) > 0.6);
            RenderHelper.MaybeSave(full, "tuning-dial-144");

            var audio = new TuningDial { Bands = DialPresets.AudioOscillator, SelectedBandIndex = 1, Frequency = 1000, ShowCoverPlate = false };
            audio.Measure(new Size(600, 600));
            Assert.Equal(600 * 0.52, audio.DesiredSize.Height, 3);
            RenderHelper.MaybeSave(RenderHelper.Render(audio, new Size(600, 312), 144), "tuning-dial-audio-144");
        });
    }

    [Fact]
    public void Bandspread_WrapsAndTurnsTheWheel()
    {
        RenderHelper.RunSta(() =>
        {
            var b = new BandspreadDial { Value = 97 };
            b.Value = 104;
            Assert.Equal(4, b.Value, 9);
            Assert.Equal(-4 * 3.6, b.WheelAngle, 9);
            b.Value = -2;
            Assert.Equal(98, b.Value, 9);

            b.IsWrapping = false;
            b.Value = 140;
            Assert.Equal(100, b.Value);

            var w = new BandspreadDial { Value = 0, Caption = "BAND SPREAD" };
            RenderHelper.MaybeSave(RenderHelper.Render(w, new Size(500, 210), 144), "bandspread-144");
        });
    }

    [Fact]
    public void JewelLamp_LitIsBrighterThanUnlit()
    {
        RenderHelper.RunSta(() =>
        {
            var lamp = new JewelLamp();
            var off = RenderHelper.Render(lamp, new Size(120, 120), 144);
            lamp.IsLit = true;
            Assert.Equal(1, lamp.Intensity);                               // not animating off-screen: applied at once
            var on = RenderHelper.Render(lamp, new Size(120, 120), 144);
            Assert.True(CentreRed(on) > CentreRed(off) + 60);
            RenderHelper.MaybeSave(off, "jewel-off-144");
            RenderHelper.MaybeSave(on, "jewel-on-144");

            var amber = new JewelLamp { IsLit = true, LampColor = Color.FromRgb(0xFF, 0xA8, 0x20), BezelColor = Color.FromRgb(0xC8, 0xCC, 0xD0) };
            RenderHelper.MaybeSave(RenderHelper.Render(amber, new Size(120, 120), 144), "jewel-amber-chrome-144");
        });
    }

    private static int CentreRed(System.Windows.Media.Imaging.BitmapSource bmp)
    {
        var px = new byte[4];
        bmp.CopyPixels(new Int32Rect(bmp.PixelWidth / 2, bmp.PixelHeight / 2 + bmp.PixelHeight / 10, 1, 1), px, 4, 0);
        return px[2];
    }

    [Fact]
    public void ChickenHead_WorksAsALabelledSelector()
    {
        RenderHelper.RunSta(() =>
        {
            var labels = (IReadOnlyList<string>)new StringListConverter().ConvertFrom(null, null, "EXT, +INT ,−INT,LINE")!;
            Assert.Equal(new[] { "EXT", "+INT", "−INT", "LINE" }, labels);
            Assert.Equal("EXT,+INT,−INT,LINE", new StringListConverter().ConvertTo(labels, typeof(string)));

            var k = new RotaryKnob
            {
                KnobStyle = KnobStyle.ChickenHead, ScaleLabels = labels, Minimum = 0, Maximum = 3, Step = 1, SweepAngle = 120,
                ShowValueArc = false, Value = 2,
            };
            Assert.Equal(2, k.Value);
            Assert.Equal(-60 + 2 * 40, k.IndicatorAngle, 9);
            var bmp = RenderHelper.Render(k, new Size(160, 160), 144);
            RenderHelper.MaybeSave(bmp, "chicken-selector-144");
            Assert.True(RenderHelper.InkCoverage(bmp) > 0.04); // a black knob on a dark panel

            var plain = new RotaryKnob { KnobStyle = KnobStyle.ChickenHead, ShowValueArc = false, Minimum = 0, Maximum = 10, Value = 3 };
            RenderHelper.MaybeSave(RenderHelper.Render(plain, new Size(120, 120), 144), "chicken-plain-144");
        });
    }
}
