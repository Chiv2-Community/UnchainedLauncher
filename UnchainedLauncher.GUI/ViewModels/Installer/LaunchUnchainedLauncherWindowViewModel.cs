using CommunityToolkit.Mvvm.Input;
using log4net;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using UnchainedLauncher.Core.JsonModels;
using UnchainedLauncher.Core.Processes;
using UnchainedLauncher.Core.Utilities;

namespace UnchainedLauncher.GUI.ViewModels.Installer {

    public partial class LaunchUnchainedLauncherWindowViewModel : INotifyPropertyChanged {
        private static readonly ILog logger = LogManager.GetLogger(typeof(LaunchUnchainedLauncherWindowViewModel));

        public LaunchUnchainedLauncherWindowViewModel() : this(
            new List<InstallationTargetViewModel> { new InstallationTargetViewModel() },
            () => { }
        ) { }

        public IEnumerable<InstallationTargetViewModel> LaunchTargets { get; }

        public InstallationTargetViewModel SelectedTarget { get; set; }

        public ICommand LaunchCommand { get; }

        private Action CloseWindow { get; }

        public LaunchUnchainedLauncherWindowViewModel(IEnumerable<InstallationTargetViewModel> targets, Action closeWindow) {
            LaunchTargets = targets.Filter(x => x.IsSelected);
            CloseWindow = closeWindow;

            if (LaunchTargets.Count() < 0) {
                MessageBox.Show("Error: No installation targets found after installation complete. This is a bug. Please file a report.");
            }

            SelectedTarget = LaunchTargets.First();

            LaunchCommand = new RelayCommand(Launch);
        }

        public void Launch() {
            string fileName;
            switch (SelectedTarget.InstallationType) {
                case InstallationType.Steam:
                    // For steam, just run the launcher normally
                    fileName = Path.Combine(SelectedTarget.Path.FullName, FilePaths.LauncherPath);
                    break;

                case InstallationType.EpicGamesStore:
                    // For EGS the launcher must be run via the Epic Games Launcher
                    fileName = "com.epicgames.launcher://apps/bd46d4ce259349e5bd8b3ded20274737%3A4c4a6c0767304c9d830f3f36f2b29018%3APeppermint?action=launch";
                    break;

                default:
                    MessageBox.Show($"Error: Unknown installation type '{SelectedTarget.InstallationType}'. This is a bug. Please file a report.");
                    return;
            }

            logger.Info($"Launching Chivalry 2 Launcher at {fileName}");

            Process.Start(new ProcessStartInfo {
                FileName = fileName,
                WorkingDirectory = SelectedTarget.Path.FullName,
                UseShellExecute = true
            });

            CloseWindow();
        }
    }
}