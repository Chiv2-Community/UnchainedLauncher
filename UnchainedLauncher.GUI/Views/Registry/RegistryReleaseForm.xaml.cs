using Microsoft.Win32;
using PropertyChanged;
using Semver;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UnchainedLauncher.GUI.ViewModels.Registry;

namespace UnchainedLauncher.GUI.Views.Registry {
    public class VersionStringRule : ValidationRule {
        public override ValidationResult Validate(object? value, CultureInfo cultureInfo) {
            if (value is string stringValue) {
                return SemVersion.TryParse(stringValue, SemVersionStyles.AllowV, out var _)
                    ? ValidationResult.ValidResult
                    : new ValidationResult(false, "Invalid version string");
            }

            return new ValidationResult(false, "Value was not string.");
        }
    }

    [AddINotifyPropertyChangedInterface]
    public partial class RegistryReleaseForm : UserControl {
        public RegistryReleaseForm() {
            InitializeComponent();
        }

        public string LastDragDropComplaint { get; set; } = "";

        private void ImagePanel_Drop(object sender, DragEventArgs e) {
            var fileData = (string[]?)e.Data.GetData(DataFormats.FileDrop);
            if (fileData == null || fileData.Length == 0) {
                LastDragDropComplaint = "Please drop a pak file";
                return;
            }

            var pickedFile = fileData[0];
            var fileName = Path.GetFileName(pickedFile);
            if (fileName == string.Empty) {
                LastDragDropComplaint = "Please drag a file, not a directory";
                return;
            }

            if (Path.GetExtension(pickedFile) != ".pak") {
                LastDragDropComplaint = "Please drag a pak file";
                return;
            }

            ((RegistryReleaseFormVM)DataContext).PakFilePath = pickedFile;
        }

        private void UIElement_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e) {
            var openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Pak files (*.pak)|*.pak";
            openFileDialog.Multiselect = false;
            if (!(openFileDialog.ShowDialog() ?? false)) {
                return;
            }

            if (openFileDialog.FileName != string.Empty) {
                ((RegistryReleaseFormVM)DataContext).PakFilePath = openFileDialog.FileName;
            }
        }
    }
}