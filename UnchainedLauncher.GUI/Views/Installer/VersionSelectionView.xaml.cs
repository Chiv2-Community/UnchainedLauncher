using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
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
using UnchainedLauncher.GUI.ViewModels.Installer;

namespace UnchainedLauncher.GUI.Views.Installer
{
    // All of this codebehind exists because WebView doesn't support binding to a string for content
    public partial class VersionSelectionView : UserControl
    {
        private bool WebViewInitialized = false;

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

        private async Task InitWebView() {
            if (WebViewInitialized) return;

            var userDataFolder = Path.Combine(
                Path.GetTempPath(),
                Path.GetRandomFileName()
            );

            var options = new CoreWebView2EnvironmentOptions();
            var environment = await CoreWebView2Environment.CreateAsync(
                null, // Use default browser version
                userDataFolder,
                options
            );

            await WebView.EnsureCoreWebView2Async(environment);
            WebViewInitialized = true;
        }

        private async Task HandleSelectedVersionChanged(VersionSelectionPageViewModel vm) {
            await InitWebView();
            WebView.NavigateToString(vm.SelectedVersionDescriptionHtml);
        }

        // And this jank is to make it so the selected dropdown item is
        // centered, rather than at the top when the dropdown is opened
        private void VersionComboBox_DropDownOpened(object sender, EventArgs e) {
            if (VersionComboBox.SelectedItem == null) return;

            VersionComboBox.Dispatcher.BeginInvoke(() => {
                if (VersionComboBox.IsDropDownOpen) {
                    if (VersionComboBox.Template.FindName("DropDownScrollViewer", VersionComboBox) is ScrollViewer scrollViewer) {
                        double targetOffset = scrollViewer.VerticalOffset - scrollViewer.ViewportHeight / 2;
                        scrollViewer.ScrollToVerticalOffset(targetOffset);
                    }
                }
            });
        }
    }
}
