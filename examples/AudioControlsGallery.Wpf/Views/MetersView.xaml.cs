using System.Windows;
using System.Windows.Controls;
using AudioControlsGallery.ViewModels;

namespace AudioControlsGallery.Views;

public partial class MetersView : UserControl
{
    public MetersView()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncFaceSelection();
    }

    private void OnResetOverload(object sender, RoutedEventArgs e) => ViewHelpers.ResetAllOverloads(this);

    private void OnFaceChecked(object sender, RoutedEventArgs e)
    {
        if (DataContext is GalleryViewModel vm && sender is FrameworkElement { Tag: FaceOption face }) vm.Face = face;
    }

    private void SyncFaceSelection()
    {
        if (DataContext is not GalleryViewModel vm) return;
        foreach (var rb in FindRadioButtons(this))
            if (rb.Tag is FaceOption f && f == vm.Face) rb.IsChecked = true;
    }

    private static IEnumerable<RadioButton> FindRadioButtons(DependencyObject root)
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is RadioButton rb) yield return rb;
            foreach (var x in FindRadioButtons(c)) yield return x;
        }
    }
}
