using System.Windows.Controls;
using AudioControlsGallery.ViewModels;

namespace AudioControlsGallery.Views;

public partial class VintageView : UserControl
{
    public VintageView()
    {
        InitializeComponent();
        // The oscillator panel shows the live spectrum and scope, so ask the view model for them while visible.
        Loaded += (_, _) => (DataContext as GalleryViewModel)?.SetDisplayViewer(true);
        Unloaded += (_, _) => (DataContext as GalleryViewModel)?.SetDisplayViewer(false);
    }
}
