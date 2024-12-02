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

    public partial class InstallationTargetViewModel: INotifyPropertyChanged {
        public DirectoryInfo Path { get; set; }
        public bool IsSelected { get; set; }

       public InstallationTargetViewModel(): this(
            new DirectoryInfo("C:/Testing/purposes/only/do/not/use/default/constructor"),
            false
       ) {}
       

        public InstallationTargetViewModel(DirectoryInfo path, bool isSelected) {
            Path = path;
            IsSelected = isSelected;
        }

        public string PathString => Path.FullName.Replace('\\', '/');
    }
}
