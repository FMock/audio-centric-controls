using System.Windows;
using System.Windows.Media;
using AudioCentric.Controls.Wpf.Controls;

namespace AudioControlsGallery.Views;

internal static class ViewHelpers
{
    /// <summary>The engineer's reset button: clears every latched overload lamp and clip LED under <paramref name="root"/>.</summary>
    public static void ResetAllOverloads(DependencyObject root)
    {
        int n = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            switch (child)
            {
                case AnalogMeter m: m.ResetOverload(); break;
                case LedBarMeter l: l.ResetClip(); break;
            }
            ResetAllOverloads(child);
        }
    }
}
