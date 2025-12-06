using System;
using System.Windows.Controls;

namespace UnchainedLauncher.GUI.Views.Installer {
    public partial class VersionSelectionView : UserControl {
        public VersionSelectionView() {
            InitializeComponent();
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