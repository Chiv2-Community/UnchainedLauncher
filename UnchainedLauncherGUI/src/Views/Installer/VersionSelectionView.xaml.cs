using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using UnchainedLauncher.GUI.ViewModels.Installer;

namespace UnchainedLauncher.GUI.Views.Installer
{
    // All of this codebehind exists because WebView doesn't support binding to a string for content
    public partial class VersionSelectionView : UserControl
    {
        public VersionSelectionView() {
            InitializeComponent();

            if (DataContext is VersionSelectionPageViewModel) {
                // We need to first initialize the handlers with the current DataContext
                HandleDataContextChanged();
            }

            DataContextChanged += (_, _) => HandleDataContextChanged();
        }

        private async void HandleDataContextChanged() {
            if (DataContext == null || DataContext is not VersionSelectionPageViewModel) return;

            VersionSelectionPageViewModel vm = (VersionSelectionPageViewModel)DataContext;

            // We need to act as though SelectedVersion changed, because we have a changed ViewModel
            await HandleSelectedVersionChanged(vm);

            // We need to handle the PropertyChanged event to update the WebView automatically
            vm.PropertyChanged += async (sender, args) => {
                if (args.PropertyName == nameof(vm.SelectedVersion)) {
                    await HandleSelectedVersionChanged(vm);
                }
            };
        }

        private async Task HandleSelectedVersionChanged(VersionSelectionPageViewModel vm) {
            await WebView.EnsureCoreWebView2Async();
            WebView.NavigateToString(vm.SelectedVersionDescriptionHtml);
        }
    }
}
