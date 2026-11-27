using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AudioCentric.Controls.Input;

namespace AudioControlsGallery.Views;

public partial class InputsView : UserControl
{
    public InputsView()
    {
        InitializeComponent();
        SlowSwitch.ActualState = false;
        FaultSwitch.ActualState = false;
    }

    // Simulates a host that needs time to apply a request (e.g. reconfiguring a device).
    private void OnSlowSwitchChanged(object sender, RoutedEventArgs e)
    {
        bool requested = SlowSwitch.IsChecked == true;
        SlowSwitch.Status = ToggleStatus.Pending;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (SlowSwitch.IsChecked != requested) return; // superseded by a newer request
            SlowSwitch.ActualState = requested;
            SlowSwitch.Status = ToggleStatus.Ready;
        };
        timer.Start();
    }

    // Simulates a host that cannot apply the request: the lamp shows the fault and the actual state stays off.
    private void OnFaultSwitchChanged(object sender, RoutedEventArgs e)
    {
        if (FaultSwitch.IsChecked == true)
        {
            FaultSwitch.Status = ToggleStatus.Pending;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (FaultSwitch.IsChecked == true) FaultSwitch.Status = ToggleStatus.Fault;
            };
            timer.Start();
        }
        else
        {
            FaultSwitch.Status = ToggleStatus.Ready;
        }
    }
}
