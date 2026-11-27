using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using AudioCentric.Controls.Input;
using AudioCentric.Controls.Scales;
using AudioCentric.Controls.Wpf.Controls;

namespace AudioCentric.Controls.Wpf.Tests;

public class InputControlTests
{
    [Fact]
    public void Knob_LogMappingPutsMidpointAt632Hz()
    {
        RenderHelper.RunSta(() =>
        {
            var knob = new RotaryKnob { Minimum = 20, Maximum = 20000, Mapping = KnobMapping.Logarithmic, Value = 632.455532 };
            Assert.Equal(0.5, knob.NormalizedValue, 6);
            Assert.Equal(0, knob.IndicatorAngle, 6); // 12 o'clock
        });
    }

    [Fact]
    public void Knob_ClampsAndExposesSettableRange()
    {
        RenderHelper.RunSta(() =>
        {
            var knob = new RotaryKnob { Minimum = -24, Maximum = 24, Value = 100 };
            Assert.Equal(24, knob.Value);
            var range = (IRangeValueProvider)UIElementAutomationPeer.CreatePeerForElement(knob).GetPattern(PatternInterface.RangeValue);
            range.SetValue(-6);
            Assert.Equal(-6, knob.Value);
            knob.IsReadOnly = true;
            Assert.Throws<System.Windows.Automation.ElementNotEnabledException>(() => range.SetValue(0));
        });
    }

    [Fact]
    public void Fader_UnityAtThreeQuartersAndMuteAtBottom()
    {
        RenderHelper.RunSta(() =>
        {
            var fader = new ConsoleFader { ValueDb = 0 };
            Assert.Equal(0.75, fader.Position, 9);
            Assert.Equal(1.0, fader.Gain, 9);
            fader.ValueDb = double.NegativeInfinity;
            Assert.Equal(0, fader.Position);
            Assert.Equal(0, fader.Gain);
            fader.ValueDb = 30;
            Assert.Equal(10, fader.ValueDb); // clamped to the taper maximum
            fader.ValueDb = 0;
            fader.IsMuted = true;
            Assert.Equal(0, fader.Gain);
        });
    }

    [Fact]
    public void Toggle_LampFollowsAcknowledgedState()
    {
        RenderHelper.RunSta(() =>
        {
            var t = new IlluminatedToggle { ActualState = false };
            t.IsChecked = true;
            Assert.False(t.IsLit);            // requested but not applied
            t.ActualState = true;
            Assert.True(t.IsLit);
            t.Status = ToggleStatus.Fault;
            Assert.False(t.IsLit);
        });
    }

    [Fact]
    public void Toggle_ExposesTogglePattern()
    {
        RenderHelper.RunSta(() =>
        {
            var t = new IlluminatedToggle();
            var toggle = (IToggleProvider)UIElementAutomationPeer.CreatePeerForElement(t).GetPattern(PatternInterface.Toggle);
            toggle.Toggle();
            Assert.True(t.IsChecked);
        });
    }

    [Fact]
    public void Led_DecaysAt20DbPerSecondAndLatchesClip()
    {
        RenderHelper.RunSta(() =>
        {
            var led = new LedBarMeter { Scale = ScalePresets.LedPeak, ClipLatchMode = AudioCentric.Controls.Dynamics.OverloadLatchMode.Latched };
            RenderHelper.Render(led, new Size(300, 30));
            led.Value = -3;
            Assert.Equal(10, led.LitSegments(0));
            led.IsClipping = true;
            led.IsClipping = false;
            Assert.True(led.IsClipLit);
            led.ResetClip();
            Assert.False(led.IsClipLit);
        });
    }

    [Fact]
    public void RendersInputControlsAndLeds()
    {
        RenderHelper.RunSta(() =>
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x1B)) };
            panel.Children.Add(new RotaryKnob { Width = 120, Height = 120, Minimum = -24, Maximum = 24, Detent = 0, Value = 6, Margin = new Thickness(10) });
            panel.Children.Add(new ConsoleFader { Height = 280, ValueDb = -6, Margin = new Thickness(10) });
            var switches = new StackPanel { Margin = new Thickness(10) };
            switches.Children.Add(new IlluminatedToggle { ShowPowerIcon = true, OnLabel = "Power on", Width = 132, IsChecked = true, Margin = new Thickness(4) });
            switches.Children.Add(new IlluminatedToggle { OnLabel = "Bypass", LampColor = Color.FromRgb(0xFF, 0xB0, 0x1E), Margin = new Thickness(4) });
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new IlluminatedToggle { SwitchStyle = IlluminatedToggleStyle.SquareCap, OnLabel = "SOLO", IsChecked = true, LampColor = Color.FromRgb(0xFF, 0xD2, 0x1E), Width = 48, Height = 48, Margin = new Thickness(4) });
            row.Children.Add(new IlluminatedToggle { SwitchStyle = IlluminatedToggleStyle.SquareCap, OnLabel = "MUTE", LampColor = Color.FromRgb(0xFF, 0x3A, 0x26), Width = 48, Height = 48, Margin = new Thickness(4) });
            row.Children.Add(new IlluminatedToggle { SwitchStyle = IlluminatedToggleStyle.Lever, OnLabel = "+48V", IsChecked = true, LampColor = Color.FromRgb(0xFF, 0x2E, 0x1E), Margin = new Thickness(4) });
            row.Children.Add(new IlluminatedToggle { SwitchStyle = IlluminatedToggleStyle.Lever, OnLabel = "PHASE", Margin = new Thickness(4) });
            switches.Children.Add(row);
            var led = new LedBarMeter { Width = 300, Height = 44, ChannelCount = 2, ShowScaleLabels = true, Value = -8, Value2 = -14, IsClipping = true, Margin = new Thickness(4) };
            switches.Children.Add(led);
            panel.Children.Add(switches);
            panel.Children.Add(new LedBarMeter { Orientation = Orientation.Vertical, Height = 280, ChannelCount = 2, ShowScaleLabels = true, Value = -2, Value2 = -20, Margin = new Thickness(10) });

            var bmp = RenderHelper.Render(panel, new Size(760, 300), 144);
            Assert.True(RenderHelper.InkCoverage(bmp) > 0.3);
            RenderHelper.MaybeSave(bmp, "inputs-144");
        });
    }
}
