using CommunityToolkit.Mvvm.Input;
using LanguageExt;
using log4net;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Services.Installer;

namespace UnchainedLauncher.GUI.ViewModels.Installer {

    public partial class InstallationSelectionPageViewModel : IInstallerPageViewModel, INotifyPropertyChanged {

        private static readonly ILog Logger = LogManager.GetLogger(typeof(InstallationSelectionPageViewModel));

        public string ContinueButtonText => "Continue";
        public bool CanContinue { get; set; }
        public string? GoBackButtonText => null;
        public bool CanGoBack => false;
        public string TitleText => "📍 Select Installation Locations";
        public string DescriptionText => "Choose where to install Unchained. We'll safely move your original Chivalry2Launcher.exe to Chivalry2Launcher-ORIGINAL.exe as a backup.";

        public IChivalry2InstallationFinder InstallationFinder { get; }
        public ObservableCollection<InstallationTargetViewModel> Installations { get; }
        public InstallationSelectionPageViewModel() : this(new Chivalry2InstallationFinder()) { }

        public InstallationSelectionPageViewModel(IChivalry2InstallationFinder installFinder) {
            InstallationFinder = installFinder;

            Installations = new ObservableCollection<InstallationTargetViewModel>();

            Installations.CollectionChanged += (_, e) => {
                if (e.OldItems != null)
                    foreach (InstallationTargetViewModel target in e.OldItems)
                        target.PropertyChanged -= UpdateCanContinue;
                if (e.NewItems != null)
                    foreach (InstallationTargetViewModel target in e.NewItems)
                        target.PropertyChanged += UpdateCanContinue;
                UpdateCanContinue();
            };
        }

        public Task Continue() => Task.CompletedTask;
        private bool _loaded;
        public bool IsScanning { get; private set; }

        public async Task Load() {
            if (_loaded) return;
            await ScanForInstallations();
            _loaded = true;
        }

        [RelayCommand]
        private async Task ScanForInstallations() {
            if (IsScanning) return;
            IsScanning = true;
            UpdateCanContinue();
            try {
                var steamDir = await DiscoverInstallation(InstallationFinder.FindSteamDir);
                var egsDir = await DiscoverInstallation(InstallationFinder.FindEGSDir);

                foreach (var target in Installations)
                    target.PropertyChanged -= UpdateCanContinue;
                Installations.Clear();
                if (steamDir != null)
                    AddInstallation(new InstallationTargetViewModel(steamDir, InstallationType.Steam, false));

                if (egsDir != null)
                    AddInstallation(new InstallationTargetViewModel(egsDir, InstallationType.EpicGamesStore, false));

            }
            catch (Exception ex) {
                Logger.Error("Error scanning for installations", ex);
                MessageBox.Show("Error scanning for installations: " + ex.Message);
            }
            finally {
                IsScanning = false;
                UpdateCanContinue();
            }
        }

        private static async Task<DirectoryInfo?> DiscoverInstallation(Func<DirectoryInfo?> findDirectory) {
            try {
                return await Task.Run(findDirectory);
            }
            catch (Exception ex) {
                Logger.Error("Error scanning for installations", ex);
                MessageBox.Show("Error scanning for installations: " + ex.Message);
                return null;
            }
        }

        [RelayCommand]
        private void BrowseForInstallation() {
            if (IsScanning) return;
            var dialog = new OpenFileDialog {
                Title = "Select Chivalry2Launcher.exe from your Chivalry 2 Installation Directory"
            };

            if (dialog.ShowDialog() != true) {
                return;
            }

            var dirInfo = Directory.GetParent(dialog.FileName);
            if (dirInfo != null && InstallationFinder.IsValidInstallation(dirInfo)) {
                AddInstallation(new InstallationTargetViewModel(
                    dirInfo,
                    InstallationType.Steam,
                    true
                ));
            }
            else {
                MessageBox.Show("Selected folder is not a valid Chivalry 2 installation");
            }
        }

        private void AddInstallation(InstallationTargetViewModel target) {
            Installations.Add(target);
        }

        private void UpdateCanContinue() {
            CanContinue = !IsScanning && Installations.Any(inst => inst.IsSelected);
        }

        private void UpdateCanContinue(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(InstallationTargetViewModel.IsSelected)) {
                UpdateCanContinue();
            }
        }
    }
}