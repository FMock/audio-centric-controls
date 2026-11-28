using System.Windows;
using System.Windows.Controls;
using AudioControlsGallery.ViewModels;

namespace AudioControlsGallery.Views;

public partial class DisplaysView : UserControl
{
    public DisplaysView()
    {
        InitializeComponent();
        // Copy scope history only while this page is on screen.
        Loaded += (_, _) =>
        {
            if (DataContext is not GalleryViewModel vm) return;
            vm.ScopeActive = true;
            if (vm.Recording == null) vm.Capture();
        };
        Unloaded += (_, _) => { if (DataContext is GalleryViewModel vm) vm.ScopeActive = false; };
    }

    private void OnArm(object sender, RoutedEventArgs e) => Scope.Arm();

    private void OnZoomToFit(object sender, RoutedEventArgs e) => Waveform.ZoomToFit();
}
