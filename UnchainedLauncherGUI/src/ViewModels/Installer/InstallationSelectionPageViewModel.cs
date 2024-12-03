using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LanguageExt;
using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using UnchainedLauncher.Core.JsonModels;
using System.ComponentModel;
using Microsoft.Win32;
using System.Xaml.Schema;
using System.Threading.Tasks;
using log4net;
using System.Windows;
using PropertyChanged;
using UnchainedLauncher.Core.Installer;

namespace UnchainedLauncher.GUI.ViewModels.Installer {

    public partial class InstallationSelectionPageViewModel : IInstallerPageViewModel, INotifyPropertyChanged {

        private static readonly ILog logger = LogManager.GetLogger(typeof(InstallationSelectionPageViewModel));

        public string ContinueButtonText => "Continue";
        public bool CanContinue { get; set; }
        public string GoBackButtonText => "Back";
        public bool CanGoBack { get { return true; } }
        public string TitleText => "Select Chivalry 2 Installations where you want to install the Unchained Launcher";
        public string DescriptionText => "For each selected installation, the Unchained Launcher will install to Chivalry2Launcher.exe and move the original launcher to Chivalry2Launcher-ORIGINAL.exe";

        public IChivalry2InstallationFinder InstallationFinder { get; }
        public ObservableCollection<InstallationTargetViewModel> Installations { get; }
        public ICommand ScanForInstallationsCommand { get; }
        public ICommand BrowseForInstallationCommand { get; }
        public InstallationSelectionPageViewModel() : this(new Chivalry2InstallationFinder()) { }

        public InstallationSelectionPageViewModel(IChivalry2InstallationFinder installFinder) {
            InstallationFinder = installFinder;

            Installations = new ObservableCollection<InstallationTargetViewModel>();

            ScanForInstallationsCommand = new RelayCommand(ScanForInstallations);
            BrowseForInstallationCommand = new RelayCommand(BrowseForInstallation);

            Installations.CollectionChanged += (_, _) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Installations)));

            ScanForInstallations();
        }

        public Task Continue() => Task.CompletedTask;
        public Task Load() => new Task(ScanForInstallations);

        private void ScanForInstallations() {
            Installations.Clear();

            try {
                var steamDir = InstallationFinder.FindSteamDir();
                if(steamDir != null)
                    AddInstallation(new InstallationTargetViewModel(steamDir, InstallationType.Steam, false));

                var egsDir = InstallationFinder.FindEGSDir();
                if(egsDir != null)
                    AddInstallation(new InstallationTargetViewModel(egsDir, InstallationType.EpicGamesStore, false));

            } catch (Exception ex) {
                logger.Error("Error scanning for installations", ex);
                MessageBox.Show("Error scanning for installations: " + ex.Message);
            }
        }

        private void BrowseForInstallation() {
            var dialog = new OpenFileDialog {
                Title = "Select Chivalry2Launcher.exe from your Chivalry 2 Installation Directory"
            };

            if (dialog.ShowDialog() == true) {
                var dirInfo = Directory.GetParent(dialog.FileName);
                if (dirInfo != null && InstallationFinder.IsValidInstallation(dirInfo)) {
                    AddInstallation(new InstallationTargetViewModel(
                        dirInfo,
                        InstallationType.Steam,
                        true
                    ));
                } else {
                    MessageBox.Show("Selected folder is not a valid Chivalry 2 installation");
                }
            }
        }

        private void AddInstallation(InstallationTargetViewModel target) {
            Installations.Add(target);
            target.PropertyChanged += UpdateCanContinue;
        }

        private void UpdateCanContinue(object? sender, PropertyChangedEventArgs e) {
            if(e.PropertyName == "IsSelected") {
                CanContinue = Installations.Any(inst => inst.IsSelected);
            }
        }
    }
}
