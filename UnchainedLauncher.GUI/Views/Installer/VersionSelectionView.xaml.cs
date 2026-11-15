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

    }
}
