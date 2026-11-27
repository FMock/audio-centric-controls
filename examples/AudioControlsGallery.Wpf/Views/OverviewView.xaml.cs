using System.Windows;
using System.Windows.Controls;

namespace AudioControlsGallery.Views;

public partial class OverviewView : UserControl
{
    public OverviewView() => InitializeComponent();

    private void OnResetOverload(object sender, RoutedEventArgs e) => ViewHelpers.ResetAllOverloads(this);
}
