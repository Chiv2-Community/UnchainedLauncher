using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using UnchainedLauncher.GUI.Services;

namespace UnchainedLauncher.GUI.Views.Installer {
    public partial class VersionSelectionView : UserControl {
        public VersionSelectionView() {
            InitializeComponent();

            Loaded += (_, _) => ThemeService.ThemeChanged += ThemeService_ThemeChanged;
            Unloaded += (_, _) => ThemeService.ThemeChanged -= ThemeService_ThemeChanged;
        }

        private void ThemeService_ThemeChanged(object? sender, ThemeVariant e) {
            // Force re-evaluation of `SelectedVersionDescriptionHtml` so `MarkdownRenderer` picks up new theme resources.
            Dispatcher.Invoke(() => {
                var be = BindingOperations.GetBindingExpression(VersionDescriptionHtml, Views.Controls.HtmlWebView.HtmlProperty);
                be?.UpdateTarget();
            });
        }

        // And this jank is to make it so the selected dropdown item is
        // centered, rather than at the top when the dropdown is opened
        private void VersionComboBox_DropDownOpened(object sender, EventArgs e) {
            if (VersionComboBox.SelectedItem == null) {
                return;
            }

            VersionComboBox.Dispatcher.BeginInvoke(() => {
                if (VersionComboBox.IsDropDownOpen) {
                    if (VersionComboBox.Template.FindName("DropDownScrollViewer", VersionComboBox) is ScrollViewer
                        scrollViewer) {
                        var targetOffset = scrollViewer.VerticalOffset - (scrollViewer.ViewportHeight / 2);
                        scrollViewer.ScrollToVerticalOffset(targetOffset);
                    }
                }
            });
        }
    }
}