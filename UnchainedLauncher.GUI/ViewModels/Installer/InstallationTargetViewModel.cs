using PropertyChanged;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnchainedLauncher.Core.JsonModels;

namespace UnchainedLauncher.GUI.ViewModels.Installer {

    public partial class InstallationTargetViewModel : INotifyPropertyChanged {
        public static List<InstallationType> AvailableInstallationTypes => new List<InstallationType> {
            InstallationType.Steam,
            InstallationType.EpicGamesStore
        };

        public DirectoryInfo Path { get; set; }
        public InstallationType InstallationType { get; set; }
        public bool IsSelected { get; set; }

        public string DisplayText => $"{InstallationType.ToFriendlyString()} ({Path.FullName})";

        public InstallationTargetViewModel() : this(
             new DirectoryInfo("C:/Testing/purposes/only/do/not/use/default/constructor"),
             InstallationType.Steam,
             false
        ) { }


        public InstallationTargetViewModel(DirectoryInfo path, InstallationType type, bool isSelected) {
            Path = path;
            InstallationType = type;
            IsSelected = isSelected;
        }

        public string PathString => Path.FullName.Replace('\\', '/');
    }
}