using System.Windows;

namespace UnchainedLauncher.GUI.Views.Controls;

public static class InputOptions {
    public static readonly DependencyProperty IsHoverHighlightEnabledProperty = DependencyProperty.RegisterAttached(
        "IsHoverHighlightEnabled", typeof(bool), typeof(InputOptions), new PropertyMetadata(true));

    public static bool GetIsHoverHighlightEnabled(DependencyObject element) => (bool)element.GetValue(IsHoverHighlightEnabledProperty);
    public static void SetIsHoverHighlightEnabled(DependencyObject element, bool value) => element.SetValue(IsHoverHighlightEnabledProperty, value);
}